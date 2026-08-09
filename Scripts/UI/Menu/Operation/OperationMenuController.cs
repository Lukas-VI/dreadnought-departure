using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DreadnoughtDeparture.Core;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.UI.Menu.Operation;

/// <summary>作战菜单：章节选择 + 数轴关卡，关卡命名约定为 章节-关卡.json。</summary>
public partial class OperationMenuController : Control
{
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string BattleScenePath = "res://Scenes/Battle/battle_scene.tscn";

	private static readonly Regex LevelNamePattern = new(@"^(\d{2})-(\d{2})\.json$");
	private const string StateFilePath = "user://operation_state.cfg";

	private ItemList _chapterList;
	private HFlowContainer _levelAxis;
	private Label _statusLabel;
	private string _selectedChapter = "";
	private readonly SortedDictionary<string, SortedDictionary<int, string>> _chapters = new();

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);

		_chapterList = GetNode<ItemList>("Body/ChapterPanel/Margin/Box/ChapterList");
		_levelAxis = GetNode<HFlowContainer>("Body/LevelPanel/Margin/Box/LevelAxis");
		_statusLabel = GetNode<Label>("Body/LevelPanel/Margin/Box/StatusLabel");
		GetNode<Button>("TopBar/BackButton").Pressed += () =>
			GetTree().ChangeSceneToFile(MainMenuPath);

		ScanLevels();
		LoadSelectedChapter();
		RefreshChapters();
		_chapterList.ItemSelected += OnChapterSelected;
	}

	private void ScanLevels()
	{
		_chapters.Clear();
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		DirAccess dir = DirAccess.Open(LevelDataManager.DefaultExportFolder);
		if (dir == null) return;
		foreach (string file in dir.GetFiles())
		{
			Match match = LevelNamePattern.Match(file);
			if (!match.Success) continue;
			string chapter = match.Groups[1].Value;
			int level = int.Parse(match.Groups[2].Value);
			if (!_chapters.TryGetValue(chapter, out var levels))
			{
				levels = new SortedDictionary<int, string>();
				_chapters[chapter] = levels;
			}
			levels[level] = file;
		}
	}

	private void LoadSelectedChapter()
	{
		var config = new ConfigFile();
		if (config.Load(StateFilePath) == Error.Ok)
		{
			_selectedChapter = config.GetValue("operation", "chapter", "").AsString();
		}
		if (string.IsNullOrEmpty(_selectedChapter) && _chapters.Count > 0)
		{
			_selectedChapter = _chapters.Keys.First();
		}
	}

	private void RefreshChapters()
	{
		_chapterList.Clear();
		foreach (string chapter in _chapters.Keys)
		{
			_chapterList.AddItem($"第 {chapter} 章");
			_chapterList.SetItemMetadata(_chapterList.ItemCount - 1, chapter);
		}
		if (_chapters.Count == 0)
		{
			_statusLabel.Text = "未找到官方关卡，请按 01-01.json 命名放置到 export/maps";
			return;
		}
		foreach (int index in Enumerable.Range(0, _chapterList.ItemCount))
		{
			if (_chapterList.GetItemMetadata(index).AsString() == _selectedChapter)
			{
				_chapterList.Select(index);
				break;
			}
		}
		RefreshLevels();
	}

	private void OnChapterSelected(long index)
	{
		if (index < 0 || index >= _chapterList.ItemCount) return;
		string chapter = _chapterList.GetItemMetadata((int)index).AsString();
		SelectChapter(chapter);
	}

	private void SelectChapter(string chapter)
	{
		if (!_chapters.ContainsKey(chapter)) return;
		_selectedChapter = chapter;
		var config = new ConfigFile();
		config.SetValue("operation", "chapter", chapter);
		config.Save(StateFilePath);
		StoryDirector.Instance?.Trigger("chapter_enter", chapter);
		RefreshLevels();
	}

	private void RefreshLevels()
	{
		foreach (Node child in _levelAxis.GetChildren())
		{
			_levelAxis.RemoveChild(child);
			child.QueueFree();
		}
		if (string.IsNullOrEmpty(_selectedChapter) || !_chapters.TryGetValue(_selectedChapter, out var levels))
		{
			return;
		}
		foreach (var (level, fileName) in levels)
		{
			int capturedLevel = level;
			string capturedFile = fileName;
			var button = new Button
			{
				Text = level.ToString("00"),
				CustomMinimumSize = new Vector2(88, 56),
			};
			button.Pressed += () => OpenLevel(capturedLevel, capturedFile);
			_levelAxis.AddChild(button);
		}
		_statusLabel.Text = $"第 {_selectedChapter} 章：共 {levels.Count} 关";
	}

	private void OpenLevel(int level, string fileName)
	{
		LevelDataManager.RuntimeMapRequest = fileName;
		LevelDataManager.ActiveCampaignMap = fileName;
		string levelId = $"{_selectedChapter}-{level:00}";
		StoryDirector.Instance?.Trigger("level_enter", levelId);
		GetTree().ChangeSceneToFile(BattleScenePath);
	}
}

using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DreadnoughtDeparture.Core;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.UI.Menu.Operation;

/// <summary>章节选择场景：扫描章节，记忆上次选择，触发 chapter_enter。</summary>
public partial class ChapterSelectController : Control
{
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string LevelSelectPath = "res://Scenes/UI/Menu/Operation/level_select.tscn";

	private static readonly Regex LevelNamePattern = new(@"^(\d{2})-(\d{2})\.json$");
	private const string StateFilePath = "user://operation_state.cfg";

	private ItemList _chapterList;
	private Label _statusLabel;
	private readonly SortedSet<string> _chapters = new();

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);

		_chapterList = GetNode<ItemList>("Center/Panel/Margin/Box/ChapterList");
		_statusLabel = GetNode<Label>("Center/Panel/Margin/Box/StatusLabel");
		GetNode<Button>("TopBar/BackButton").Pressed += () =>
			GetTree().ChangeSceneToFile(MainMenuPath);
		GetNode<Button>("Center/Panel/Margin/Box/EnterButton").Pressed += EnterChapter;
		_chapterList.ItemActivated += _ => EnterChapter();

		ScanChapters();
		LoadSelectedChapter();
		RefreshList();
	}

	private void ScanChapters()
	{
		_chapters.Clear();
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		DirAccess dir = DirAccess.Open(LevelDataManager.DefaultExportFolder);
		if (dir == null) return;
		foreach (string file in dir.GetFiles())
		{
			Match match = LevelNamePattern.Match(file);
			if (match.Success)
			{
				_chapters.Add(match.Groups[1].Value);
			}
		}
	}

	private void LoadSelectedChapter()
	{
		var config = new ConfigFile();
		if (config.Load(StateFilePath) == Error.Ok)
		{
			LevelSelectController.PendingChapter =
				config.GetValue("operation", "chapter", "").AsString();
		}
	}

	private void RefreshList()
	{
		_chapterList.Clear();
		foreach (string chapter in _chapters)
		{
			_chapterList.AddItem($"第 {chapter} 章");
			_chapterList.SetItemMetadata(_chapterList.ItemCount - 1, chapter);
		}
		if (_chapters.Count == 0)
		{
			_statusLabel.Text = "未找到官方关卡，请按 01-01.json 命名放置到 export/maps";
			return;
		}
		string selected = LevelSelectController.PendingChapter;
		if (string.IsNullOrEmpty(selected) || !_chapters.Contains(selected))
		{
			selected = _chapters.First();
		}
		for (int i = 0; i < _chapterList.ItemCount; i++)
		{
			if (_chapterList.GetItemMetadata(i).AsString() == selected)
			{
				_chapterList.Select(i);
				break;
			}
		}
		_statusLabel.Text = $"已记忆章节：第 {selected} 章";
	}

	private void EnterChapter()
	{
		if (_chapterList.GetSelectedItems().Length == 0) return;
		int index = _chapterList.GetSelectedItems()[0];
		string chapter = _chapterList.GetItemMetadata(index).AsString();
		LevelSelectController.PendingChapter = chapter;
		var config = new ConfigFile();
		config.SetValue("operation", "chapter", chapter);
		config.Save(StateFilePath);
		StoryDirector.Instance?.Trigger("chapter_enter", chapter);
		GetTree().ChangeSceneToFile(LevelSelectPath);
	}
}

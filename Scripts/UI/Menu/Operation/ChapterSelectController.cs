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
	[Export] public Texture2D ScrollBackgroundTexture;

	private static readonly Regex LevelNamePattern = new(@"^(\d{2})-(\d{2})\.json$");
	private const string StateFilePath = "user://operation_state.cfg";

	private HBoxContainer _chapterAxis;
	private Label _statusLabel;
	private readonly SortedSet<string> _chapters = new();
	private string _highlightedChapter = "";

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);

		_chapterAxis = GetNode<HBoxContainer>("Margin/Panel/Box/ChapterScroll/ChapterAxis");
		_statusLabel = GetNode<Label>("Margin/Panel/Box/StatusLabel");
		var scrollBackground = GetNodeOrNull<TextureRect>("Margin/Panel/Box/ScrollBackground");
		if (scrollBackground != null && ScrollBackgroundTexture != null)
		{
			scrollBackground.Texture = ScrollBackgroundTexture;
		}
		GetNode<Button>("TopMarginContainer/TopBar/BackButton").Pressed += () =>
			GetTree().ChangeSceneToFile(MainMenuPath);

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
		foreach (Node child in _chapterAxis.GetChildren())
		{
			_chapterAxis.RemoveChild(child);
			child.QueueFree();
		}
		foreach (string chapter in _chapters)
		{
			string captured = chapter;
			var button = new Button
			{
				Text = $"第 {chapter} 章",
				CustomMinimumSize = new Vector2(180, 90),
			};
			button.Pressed += () => EnterChapter(captured);
			_chapterAxis.AddChild(button);
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
		foreach (Node child in _chapterAxis.GetChildren())
		{
			if (child is Button button
				&& button.Text == $"第 {selected} 章")
			{
				button.Modulate = new Color(1f, 0.85f, 0.45f, 1f);
				_highlightedChapter = selected;
			}
		}
		_statusLabel.Text = $"已记忆章节：第 {selected} 章";
	}

	private async void EnterChapter(string chapter)
	{
		LevelSelectController.PendingChapter = chapter;
		var config = new ConfigFile();
		config.SetValue("operation", "chapter", chapter);
		config.Save(StateFilePath);
		StoryDirector.Instance?.Trigger("chapter_enter", chapter);
		if (StoryDirector.Instance?.IsPlaying == true)
		{
			await StoryDirector.Instance.WhenStoryFinishedAsync();
		}
		GetTree().ChangeSceneToFile(LevelSelectPath);
	}
}

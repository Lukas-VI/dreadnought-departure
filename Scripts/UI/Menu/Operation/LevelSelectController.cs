using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DreadnoughtDeparture.Core;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.UI.Menu.Operation;

/// <summary>关卡选择场景：按章节显示关卡数轴，触发 level_enter。</summary>
public partial class LevelSelectController : Control
{
	public static string PendingChapter = "";

	[Export] public string ChapterSelectPath = "res://Scenes/UI/Menu/Operation/chapter_select.tscn";
	[Export] public string BattleScenePath = "res://Scenes/Battle/battle_scene.tscn";
	[Export] public Texture2D ScrollBackgroundTexture;

	private static readonly Regex LevelNamePattern = new(@"^(\d{2})-(\d{2})\.json$");

	private HBoxContainer _levelAxis;
	private Label _statusLabel;
	private Label _titleLabel;
	private readonly SortedDictionary<int, string> _levels = new();

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);

		_titleLabel = GetNode<Label>("TopBar/Title");
		_levelAxis = GetNode<HBoxContainer>("Body/Box/LevelScroll/LevelAxis");
		_statusLabel = GetNode<Label>("Body/Box/StatusLabel");
		var scrollBackground = GetNodeOrNull<TextureRect>("Body/Box/ScrollBackground");
		if (scrollBackground != null && ScrollBackgroundTexture != null)
		{
			scrollBackground.Texture = ScrollBackgroundTexture;
		}
		GetNode<Button>("TopBar/BackButton").Pressed += () =>
			GetTree().ChangeSceneToFile(ChapterSelectPath);

		if (string.IsNullOrEmpty(PendingChapter))
		{
			_statusLabel.Text = "未选择章节";
			return;
		}
		_titleLabel.Text = $"第 {PendingChapter} 章 · 选择关卡";
		ScanLevels();
		RefreshLevels();
	}

	private void ScanLevels()
	{
		_levels.Clear();
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		DirAccess dir = DirAccess.Open(LevelDataManager.DefaultExportFolder);
		if (dir == null) return;
		foreach (string file in dir.GetFiles())
		{
			Match match = LevelNamePattern.Match(file);
			if (!match.Success || match.Groups[1].Value != PendingChapter) continue;
			int level = int.Parse(match.Groups[2].Value);
			_levels[level] = file;
		}
	}

	private void RefreshLevels()
	{
		foreach (Node child in _levelAxis.GetChildren())
		{
			_levelAxis.RemoveChild(child);
			child.QueueFree();
		}
		foreach (var (level, fileName) in _levels)
		{
			int capturedLevel = level;
			string capturedFile = fileName;
			var button = new Button
			{
				Text = level.ToString("00"),
				CustomMinimumSize = new Vector2(96, 60),
			};
			button.Pressed += () => OpenLevel(capturedLevel, capturedFile);
			_levelAxis.AddChild(button);
		}
		_statusLabel.Text = _levels.Count == 0
			? $"第 {PendingChapter} 章没有关卡"
			: $"第 {PendingChapter} 章：共 {_levels.Count} 关";
	}

	private void OpenLevel(int level, string fileName)
	{
		LevelDataManager.RuntimeMapRequest = fileName;
		LevelDataManager.ActiveCampaignMap = fileName;
		string levelId = $"{PendingChapter}-{level:00}";
		StoryDirector.Instance?.Trigger("level_enter", levelId);
		GetTree().ChangeSceneToFile(BattleScenePath);
	}
}

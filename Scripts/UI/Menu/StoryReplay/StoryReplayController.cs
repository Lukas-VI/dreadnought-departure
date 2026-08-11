using Godot;
using System.Collections.Generic;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.UI.Menu.StoryReplay;

/// <summary>剧情回放：列出所有已解锁剧情，点击即可重新观看。</summary>
public partial class StoryReplayController : Control
{
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private VBoxContainer _storyList;
	private Label _statusLabel;

	public override void _Ready()
	{
		var story = new StoryDirector();
		AddChild(story);

		_storyList = GetNode<VBoxContainer>("Body/Box/Scroll/StoryList");
		_statusLabel = GetNode<Label>("Body/Box/StatusLabel");
		GetNode<Button>("TopBar/BackButton").Pressed += () =>
			GetTree().ChangeSceneToFile(MainMenuPath);

		RefreshList();
	}

	private void RefreshList()
	{
		foreach (Node child in _storyList.GetChildren())
		{
			_storyList.RemoveChild(child);
			child.QueueFree();
		}

		List<StoryNode> stories = StoryDirector.Instance?.GetUnlockedStories() ?? new();
		if (stories.Count == 0)
		{
			_statusLabel.Text = "暂无已解锁剧情。进入战役观看剧情后会自动解锁。";
			return;
		}
		_statusLabel.Text = $"已解锁 {stories.Count} 段剧情";
		foreach (StoryNode node in stories)
		{
			string captured = node.Script;
			string title = string.IsNullOrEmpty(node.Title) ? node.Script : node.Title;
			var button = new Button
			{
				Text = $"{title}  [{node.Script}]",
				CustomMinimumSize = new Vector2(0, 56),
			};
			button.Pressed += () => StoryDirector.Instance?.Play(captured);
			_storyList.AddChild(button);
		}
	}
}

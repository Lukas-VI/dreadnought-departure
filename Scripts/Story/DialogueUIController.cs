using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Story;

/// <summary>节点化对话 UI：CanvasLayer 置顶，半透明背景，底部对话框与选项区。</summary>
public partial class DialogueUIController : CanvasLayer
{
	[Signal] public delegate void ContinuePressedEventHandler();
	[Signal] public delegate void OptionSelectedEventHandler(int index);

	private Control _root;
	private ColorRect _background;
	private TextureRect _backgroundImage;
	private TextureRect _avatarRect;
	private Label _speakerLabel;
	private Label _textLabel;
	private Button _historyButton;
	private PanelContainer _historyPanel;
	private RichTextLabel _historyText;
	private VBoxContainer _optionsBox;
	private PanelContainer _debugPanel;
	private Label _debugLabel;
	private Button _debugBackButton;
	private Button _debugNextButton;
	private LineEdit _debugJumpEdit;
	private Button _debugJumpButton;
	private Button _debugSnapshotButton;
	private Button _debugRestoreButton;
	private readonly List<string> _historyLines = new();
	private ulong _showAt;

	public override void _Ready()
	{
		Layer = 100;
		_root = GetNode<Control>("Root");
		_background = GetNode<ColorRect>("Root/Background");
		_backgroundImage = GetNodeOrNull<TextureRect>("Root/BackgroundImage");
		_avatarRect = GetNodeOrNull<TextureRect>("Root/Avatar");
		_speakerLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/HBoxContainer/SpeakerLabel");
		_textLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/TextLabel");
		_historyButton = GetNode<Button>("Root/DialogPanel/Margin/VBox/HBoxContainer/HistoryButton");
		_historyPanel = GetNode<PanelContainer>("Root/HistoryPanel");
		_historyText = GetNode<RichTextLabel>("Root/HistoryPanel/HistoryText");
		_optionsBox = GetNode<VBoxContainer>("Root/OptionsBox");
		_debugPanel = GetNodeOrNull<PanelContainer>("Root/DebugPanel");
		_debugLabel = GetNodeOrNull<Label>("Root/DebugPanel/Margin/VBox/DebugLabel");
		_debugBackButton = GetNodeOrNull<Button>("Root/DebugPanel/Margin/VBox/Row1/BackButton");
		_debugNextButton = GetNodeOrNull<Button>("Root/DebugPanel/Margin/VBox/Row1/NextButton");
		_debugJumpEdit = GetNodeOrNull<LineEdit>("Root/DebugPanel/Margin/VBox/Row1/JumpEdit");
		_debugJumpButton = GetNodeOrNull<Button>("Root/DebugPanel/Margin/VBox/Row1/JumpButton");
		_debugSnapshotButton = GetNodeOrNull<Button>("Root/DebugPanel/Margin/VBox/Row2/SnapshotButton");
		_debugRestoreButton = GetNodeOrNull<Button>("Root/DebugPanel/Margin/VBox/Row2/RestoreButton");
		_root.GuiInput += OnRootInput;
		_historyButton.Pressed += ToggleHistory;
		if (_debugBackButton != null)
		{
			_debugBackButton.Pressed += () => StoryDirector.Instance?.DebugBack();
		}
		if (_debugNextButton != null)
		{
			_debugNextButton.Pressed += () => StoryDirector.Instance?.DebugNext();
		}
		if (_debugJumpButton != null)
		{
			_debugJumpButton.Pressed += OnJumpPressed;
		}
		if (_debugSnapshotButton != null)
		{
			_debugSnapshotButton.Pressed += () => StoryDirector.Instance?.SaveDebugSnapshot();
		}
		if (_debugRestoreButton != null)
		{
			_debugRestoreButton.Pressed += () => StoryDirector.Instance?.LoadDebugSnapshot();
		}
		HideUi();
	}

	private void OnRootInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mouse
			|| !mouse.Pressed
			|| mouse.ButtonIndex != MouseButton.Left)
		{
			return;
		}
		if (_debugPanel != null && _debugPanel.GetGlobalRect().HasPoint(mouse.Position))
		{
			return;
		}
		if (_historyPanel != null
			&& _historyPanel.Visible
			&& _historyPanel.GetGlobalRect().HasPoint(mouse.Position))
		{
			return;
		}
		if (Time.GetTicksMsec() - _showAt < 200)
		{
			return;
		}
		EmitSignal(SignalName.ContinuePressed);
	}

	public void ShowSay(string speaker, string text)
	{
		_root.Visible = true;
		_showAt = Time.GetTicksMsec();
		_speakerLabel.Text = speaker;
		_textLabel.Text = text;
		_optionsBox.Visible = false;
	}

	public void ShowOptions(string prompt, IReadOnlyList<string> options)
	{
		_root.Visible = true;
		_showAt = Time.GetTicksMsec();
		_speakerLabel.Text = "";
		_textLabel.Text = prompt;
		_optionsBox.Visible = true;
		ClearOptions();
		for (int i = 0; i < options.Count; i++)
		{
			int captured = i;
			var button = new Button
			{
				Text = options[i],
				CustomMinimumSize = new Vector2(0, 48),
			};
			button.Pressed += () => EmitSignal(SignalName.OptionSelected, captured);
			_optionsBox.AddChild(button);
		}
	}

	public void ApplyBackground(string color, float alpha, string image, string overlay)
	{
		if (_backgroundImage != null)
		{
			if (!string.IsNullOrEmpty(image))
			{
				Texture2D texture = ResourceLoader.Load<Texture2D>(image);
				if (texture != null)
				{
					_backgroundImage.Texture = texture;
					_backgroundImage.Visible = true;
				}
			}
			else if (_backgroundImage.Texture == null)
			{
				_backgroundImage.Visible = false;
			}
		}
		if (!string.IsNullOrEmpty(overlay))
		{
			_background.Color = NarrativeState.ParseColor(overlay);
		}
		else if (!string.IsNullOrEmpty(color))
		{
			_background.Color = NarrativeState.ParseColor(color, alpha);
		}
	}

	public void SetBackgroundColor(string color)
	{
		ApplyBackground(color, -1f, "", "");
	}

	public void ApplyAvatar(string path, string position, float scale)
	{
		if (_avatarRect == null) return;
		if (string.IsNullOrEmpty(path))
		{
			if (position == "hidden")
			{
				HideAvatar();
			}
			return;
		}
		if (path == "hidden" || position == "hidden")
		{
			HideAvatar();
			return;
		}
		Texture2D texture = ResourceLoader.Load<Texture2D>(path);
		if (texture == null) return;
		_avatarRect.Texture = texture;
		_avatarRect.Visible = true;
		ApplyAvatarPosition(string.IsNullOrEmpty(position) ? "left" : position,
			scale > 0f ? scale : 1f);
	}

	public void SetHistory(IReadOnlyList<string> lines)
	{
		_historyLines.Clear();
		if (lines != null)
		{
			_historyLines.AddRange(lines);
		}
		if (_historyPanel != null
			&& _historyPanel.Visible
			&& _historyText != null)
		{
			_historyText.Text = string.Join("\n", _historyLines);
		}
	}

	public void ShowDebugState(NarrativeState state)
	{
		if (_debugPanel == null || state == null) return;
		_debugPanel.Visible = true;
		if (_debugLabel != null)
		{
			string status = state.Status switch
			{
				NarrativeStatus.Playing => "播放中",
				NarrativeStatus.WaitingChoice => "等待选项",
				NarrativeStatus.Completed => "已完成",
				_ => "空闲",
			};
			_debugLabel.Text =
				$"{state.ScriptId} · {state.Index + 1}/{state.StepCount} · {status}";
		}
		if (_debugBackButton != null)
		{
			_debugBackButton.Disabled = !state.CanBack;
		}
		if (_debugNextButton != null)
		{
			_debugNextButton.Disabled = !state.CanAdvance;
		}
	}

	public void HideUi()
	{
		if (_root != null)
		{
			_root.Visible = false;
			if (_historyPanel != null)
			{
				_historyPanel.Visible = false;
			}
			if (_debugPanel != null)
			{
				_debugPanel.Visible = false;
			}
			if (_avatarRect != null)
			{
				_avatarRect.Visible = false;
			}
		}
	}

	private void ApplyAvatarPosition(string position, float scale)
	{
		if (_avatarRect == null) return;
		float width = 360f * scale;
		float height = 540f * scale;
		const float sideGap = 32f;
		const float bottomGap = 40f;
		_avatarRect.AnchorTop = 1f;
		_avatarRect.AnchorBottom = 1f;
		switch (position)
		{
			case "right":
				_avatarRect.AnchorLeft = 1f;
				_avatarRect.AnchorRight = 1f;
				_avatarRect.OffsetLeft = -sideGap - width;
				_avatarRect.OffsetRight = -sideGap;
				break;
			case "center":
				_avatarRect.AnchorLeft = 0.5f;
				_avatarRect.AnchorRight = 0.5f;
				_avatarRect.OffsetLeft = -width * 0.5f;
				_avatarRect.OffsetRight = width * 0.5f;
				break;
			default:
				_avatarRect.AnchorLeft = 0f;
				_avatarRect.AnchorRight = 0f;
				_avatarRect.OffsetLeft = sideGap;
				_avatarRect.OffsetRight = sideGap + width;
				break;
		}
		_avatarRect.OffsetTop = -bottomGap - height;
		_avatarRect.OffsetBottom = -bottomGap;
	}

	private void HideAvatar()
	{
		if (_avatarRect != null)
		{
			_avatarRect.Visible = false;
		}
	}

	private void OnJumpPressed()
	{
		if (_debugJumpEdit == null) return;
		string text = _debugJumpEdit.Text.Trim();
		if (int.TryParse(text, out int index) && index >= 1)
		{
			StoryDirector.Instance?.DebugJump(index - 1);
		}
	}

	private void ToggleHistory()
	{
		if (_historyPanel == null) return;
		_historyPanel.Visible = !_historyPanel.Visible;
		if (_historyPanel.Visible && _historyText != null)
		{
			_historyText.Text = string.Join("\n", _historyLines);
		}
	}

	private void ClearOptions()
	{
		foreach (Node child in _optionsBox.GetChildren())
		{
			_optionsBox.RemoveChild(child);
			child.QueueFree();
		}
	}
}

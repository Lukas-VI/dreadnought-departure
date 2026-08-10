using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Story;

/// <summary>节点化对话 UI：CanvasLayer 置顶，半透明背景，底部对话框与选项区。</summary>
public partial class DialogueUIController : CanvasLayer
{
	[Signal] public delegate void ContinuePressedEventHandler();
	[Signal] public delegate void OptionSelectedEventHandler(int index);

	private Control _root;
	private ColorRect _background;
	private Label _speakerLabel;
	private Label _textLabel;
	private Button _historyButton;
	private PanelContainer _historyPanel;
	private RichTextLabel _historyText;
	private VBoxContainer _optionsBox;
	private readonly List<string> _historyLines = new();
	private ulong _showAt;

	public override void _Ready()
	{
		Layer = 100;
		_root = GetNode<Control>("Root");
		_background = GetNode<ColorRect>("Root/Background");
		_speakerLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/HBoxContainer/SpeakerLabel");
		_textLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/TextLabel");
		_historyButton = GetNode<Button>("Root/DialogPanel/Margin/VBox/HBoxContainer/HistoryButton");
		_historyPanel = GetNode<PanelContainer>("Root/HistoryPanel");
		_historyText = GetNode<RichTextLabel>("Root/HistoryPanel/HistoryText");
		_optionsBox = GetNode<VBoxContainer>("Root/OptionsBox");
		_root.GuiInput += OnRootInput;
		_historyButton.Pressed += ToggleHistory;
		HideUi();
	}

	private void OnRootInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouse
			&& mouse.Pressed
			&& mouse.ButtonIndex == MouseButton.Left)
		{
			if (Time.GetTicksMsec() - _showAt < 200)
			{
				return;
			}
			EmitSignal(SignalName.ContinuePressed);
		}
	}

	public void ShowSay(string speaker, string text)
	{
		_root.Visible = true;
		_showAt = Time.GetTicksMsec();
		_speakerLabel.Text = speaker;
		_textLabel.Text = text;
		_optionsBox.Visible = false;
		if (!string.IsNullOrEmpty(text))
		{
			_historyLines.Add(string.IsNullOrEmpty(speaker) ? text : $"{speaker}：{text}");
		}
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

	public void SetBackgroundColor(string color)
	{
		if (string.IsNullOrEmpty(color)) return;
		_background.Color = Color.FromHtml(color);
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

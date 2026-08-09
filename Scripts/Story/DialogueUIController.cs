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
	private Button _continueButton;
	private VBoxContainer _optionsBox;

	public override void _Ready()
	{
		Layer = 100;
		_root = GetNode<Control>("Root");
		_background = GetNode<ColorRect>("Root/Background");
		_speakerLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/SpeakerLabel");
		_textLabel = GetNode<Label>("Root/DialogPanel/Margin/VBox/TextLabel");
		_continueButton = GetNode<Button>("Root/DialogPanel/Margin/VBox/ContinueButton");
		_optionsBox = GetNode<VBoxContainer>("Root/OptionsBox");
		_continueButton.Pressed += () => EmitSignal(SignalName.ContinuePressed);
		HideUi();
	}

	public void ShowSay(string speaker, string text)
	{
		_root.Visible = true;
		_speakerLabel.Text = speaker;
		_textLabel.Text = text;
		_continueButton.Visible = true;
		_optionsBox.Visible = false;
	}

	public void ShowOptions(string prompt, IReadOnlyList<string> options)
	{
		_root.Visible = true;
		_speakerLabel.Text = "";
		_textLabel.Text = prompt;
		_continueButton.Visible = false;
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

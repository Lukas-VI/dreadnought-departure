using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DreadnoughtDeparture.Story;

/// <summary>基础对话演出：背景、对话框、角色名、正文、选项与脚本控制。</summary>
public partial class DialogueRunner : CanvasLayer
{
	private Control _root;
	private ColorRect _background;
	private Label _speakerLabel;
	private Label _textLabel;
	private Button _continueButton;
	private VBoxContainer _optionsBox;
	private bool _playing;

	public override void _Ready()
	{
		BuildUi();
		HideUi();
	}

	public void Play(string scriptId)
	{
		if (_playing) return;
		_playing = true;
		_ = RunScriptAsync(scriptId);
	}

	public void Stop()
	{
		_playing = false;
		HideUi();
		StoryDirector.Instance?.NotifyFinished();
	}

	private async System.Threading.Tasks.Task RunScriptAsync(string scriptId)
	{
		string path = $"res://Data/Stories/{scriptId}.json";
		if (!FileAccess.FileExists(path))
		{
			StoryDirector.Instance?.NotifyFinished();
			_playing = false;
			return;
		}
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		string json = file.GetAsText();
		using var document = JsonDocument.Parse(json);
		JsonElement root = document.RootElement;

		ApplyBackground(Str(root, "background"));
		var steps = root.TryGetProperty("steps", out JsonElement stepsProp)
			? stepsProp.EnumerateArray().ToList()
			: new List<JsonElement>();

		int index = 0;
		while (_playing && index >= 0 && index < steps.Count)
		{
			JsonElement step = steps[index];
			string type = Str(step, "type");
			if (type == "say")
			{
				await ShowSayAsync(Str(step, "speaker"), Str(step, "text"));
				index++;
			}
			else if (type == "choice")
			{
				int next = await ShowChoiceAsync(Str(step, "text"), step);
				index = next >= 0 ? next : index + 1;
			}
			else if (type == "wait")
			{
				float seconds = step.TryGetProperty("seconds", out JsonElement secondsProp)
					? secondsProp.GetSingle()
					: 1f;
				await ToSignal(GetTree().CreateTimer(seconds), "timeout");
				index++;
			}
			else if (type == "flag")
			{
				string key = Str(step, "key");
				bool value = step.TryGetProperty("value", out JsonElement valueProp)
					&& valueProp.ValueKind == JsonValueKind.True;
				StoryDirector.Instance?.SetFlag(key, value);
				index++;
			}
			else if (type == "background")
			{
				ApplyBackground(Str(step, "color"));
				index++;
			}
			else
			{
				index++;
			}
		}

		_playing = false;
		HideUi();
		StoryDirector.Instance?.NotifyFinished();
	}

	private async System.Threading.Tasks.Task ShowSayAsync(string speaker, string text)
	{
		_root.Visible = true;
		_speakerLabel.Text = speaker;
		_textLabel.Text = text;
		_continueButton.Visible = true;
		_optionsBox.Visible = false;
		await ToSignal(_continueButton, BaseButton.SignalName.Pressed);
	}

	private async System.Threading.Tasks.Task<int> ShowChoiceAsync(string prompt, JsonElement step)
	{
		_root.Visible = true;
		_speakerLabel.Text = "";
		_textLabel.Text = prompt;
		_continueButton.Visible = false;
		_optionsBox.Visible = true;
		ClearOptions();

		var tasks = new List<(Button Button, int Next)>();
		if (step.TryGetProperty("options", out JsonElement options))
		{
			foreach (JsonElement option in options.EnumerateArray())
			{
				var button = new Button
				{
					Text = Str(option, "text"),
					CustomMinimumSize = new Vector2(0, 48),
				};
				int next = option.TryGetProperty("next", out JsonElement nextProp)
					? nextProp.GetInt32()
					: -1;
				tasks.Add((button, next));
				_optionsBox.AddChild(button);
			}
		}
		if (tasks.Count == 0)
		{
			return -1;
		}

		var completion = new GodotObject();
		int selected = -1;
		foreach (var (button, next) in tasks)
		{
			button.Pressed += () => selected = next;
		}
		while (selected < 0)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		return selected;
	}

	private void BuildUi()
	{
		_root = new Control();
		_root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(_root);

		_background = new ColorRect
		{
			Color = new Color(0.03f, 0.05f, 0.09f, 0.96f),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_root.AddChild(_background);

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		panel.OffsetTop = -260;
		panel.OffsetBottom = -40;
		panel.OffsetLeft = 120;
		panel.OffsetRight = -120;
		_root.AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 12);
		panel.AddChild(box);

		_speakerLabel = new Label { Text = "" };
		_speakerLabel.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(_speakerLabel);

		_textLabel = new Label
		{
			Text = "",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0, 120),
		};
		_textLabel.AddThemeFontSizeOverride("font_size", 24);
		box.AddChild(_textLabel);

		_continueButton = new Button { Text = "继续 ▸", CustomMinimumSize = new Vector2(180, 48) };
		box.AddChild(_continueButton);

		_optionsBox = new VBoxContainer();
		_optionsBox.Visible = false;
		_optionsBox.SetAnchorsPreset(Control.LayoutPreset.Center);
		_optionsBox.OffsetTop = 80;
		_optionsBox.AddThemeConstantOverride("separation", 10);
		_root.AddChild(_optionsBox);
	}

	private void ClearOptions()
	{
		foreach (Node child in _optionsBox.GetChildren())
		{
			_optionsBox.RemoveChild(child);
			child.QueueFree();
		}
	}

	private void ApplyBackground(string color)
	{
		if (string.IsNullOrEmpty(color)) return;
		_background.Color = Color.FromHtml(color);
	}

	private void HideUi()
	{
		if (_root != null)
		{
			_root.Visible = false;
		}
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";
}

using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DreadnoughtDeparture.Story;

/// <summary>对话脚本驱动：读取 JSON 脚本并驱动节点化 DialogueUI。</summary>
public partial class DialogueRunner : Node
{
	private DialogueUIController _ui;
	private bool _playing;

	public override void _Ready()
	{
		var scene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/Dialogue/dialogue_ui.tscn");
		if (scene != null)
		{
			_ui = scene.Instantiate<DialogueUIController>();
			AddChild(_ui);
		}
	}

	public void Play(string scriptId)
	{
		if (_playing || _ui == null) return;
		_playing = true;
		_ = RunScriptAsync(scriptId);
	}

	private async System.Threading.Tasks.Task RunScriptAsync(string scriptId)
	{
		string path = $"res://Data/Stories/{scriptId}.json";
		if (!FileAccess.FileExists(path))
		{
			Finish();
			return;
		}
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		using var document = JsonDocument.Parse(file.GetAsText());
		JsonElement root = document.RootElement;

		_ui.SetBackgroundColor(Str(root, "background"));
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
				_ui.SetBackgroundColor(Str(step, "color"));
				index++;
			}
			else
			{
				index++;
			}
		}

		Finish();
	}

	private async System.Threading.Tasks.Task ShowSayAsync(string speaker, string text)
	{
		_ui.ShowSay(speaker, text);
		await ToSignal(_ui, DialogueUIController.SignalName.ContinuePressed);
	}

	private async System.Threading.Tasks.Task<int> ShowChoiceAsync(string prompt, JsonElement step)
	{
		var texts = new List<string>();
		if (step.TryGetProperty("options", out JsonElement options))
		{
			foreach (JsonElement option in options.EnumerateArray())
			{
				texts.Add(Str(option, "text"));
			}
		}
		_ui.ShowOptions(prompt, texts);

		int selected = -1;
		void OnSelected(int index)
		{
			selected = index;
			int next = -1;
			if (step.TryGetProperty("options", out JsonElement optionArray))
			{
				var optionsList = optionArray.EnumerateArray().ToList();
				if (index >= 0 && index < optionsList.Count
					&& optionsList[index].TryGetProperty("next", out JsonElement nextProp))
				{
					next = nextProp.GetInt32();
				}
			}
			_selectedNext = next;
		}
		_ui.OptionSelected += OnSelected;
		while (selected < 0)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		_ui.OptionSelected -= OnSelected;
		return _selectedNext;
	}

	private int _selectedNext = -1;

	private void Finish()
	{
		_playing = false;
		_ui?.HideUi();
		StoryDirector.Instance?.NotifyFinished();
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";
}

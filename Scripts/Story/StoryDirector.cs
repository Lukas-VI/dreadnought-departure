using Godot;
using System.Collections.Generic;
using System.Text.Json;
using DreadnoughtDeparture.Core;

namespace DreadnoughtDeparture.Story;

/// <summary>
/// 剧情/演绎调度器：读取 triggers.json，监听 EventBus 事件，
/// 在满足条件时播放对应对话脚本，并维护剧情 flag。
/// </summary>
public partial class StoryDirector : Node
{
	public static StoryDirector Instance { get; private set; }

	private readonly List<TriggerRule> _triggers = new();
	private readonly HashSet<string> _played = new();
	private readonly Dictionary<string, bool> _flags = new();
	private DialogueRunner _runner;
	private string _playingScript = "";

	private sealed class TriggerRule
	{
		public string Event;
		public string Key;
		public string Script;
	}

	public override void _Ready()
	{
		Instance = this;
		_runner = new DialogueRunner();
		AddChild(_runner);
		LoadTriggers();
		var bus = GetNodeOrNull<EventBus>("../EventBus");
		if (bus != null)
		{
			bus.BattleStarted += () => Trigger("battle_start");
			bus.BattleEnded += (result, detail) => Trigger("battle_end", result);
			bus.PlayerActionPerformed += (actionId) => Trigger("player_action", actionId);
			bus.SpecialCellEntered += (hex, specialId) => Trigger("special_cell", specialId.ToString());
		}
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void Play(string scriptId)
	{
		if (_runner == null || _playingScript.Length > 0) return;
		_playingScript = scriptId;
		_played.Add(scriptId);
		_runner.Play(scriptId);
	}

	public void Trigger(string eventName, string key = "")
	{
		foreach (TriggerRule rule in _triggers)
		{
			if (rule.Event != eventName) continue;
			if (!string.IsNullOrEmpty(rule.Key) && rule.Key != key) continue;
			if (_played.Contains(rule.Script)) continue;
			Play(rule.Script);
			return;
		}
	}

	public void NotifyFinished()
	{
		_playingScript = "";
	}

	public void SetFlag(string key, bool value)
	{
		_flags[key] = value;
	}

	public bool GetFlag(string key)
		=> _flags.TryGetValue(key, out bool value) && value;

	private void LoadTriggers()
	{
		const string path = "res://Data/Stories/triggers.json";
		if (!FileAccess.FileExists(path)) return;
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		using var document = JsonDocument.Parse(file.GetAsText());
		JsonElement root = document.RootElement;
		if (!root.TryGetProperty("triggers", out JsonElement triggers)) return;
		foreach (JsonElement entry in triggers.EnumerateArray())
		{
			_triggers.Add(new TriggerRule
			{
				Event = entry.TryGetProperty("event", out JsonElement eventProp)
					? eventProp.GetString() ?? ""
					: "",
				Key = entry.TryGetProperty("key", out JsonElement keyProp)
					? keyProp.GetString() ?? ""
					: "",
				Script = entry.TryGetProperty("script", out JsonElement scriptProp)
					? scriptProp.GetString() ?? ""
					: "",
			});
		}
	}
}

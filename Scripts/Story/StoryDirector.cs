using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
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
	public event Action Finished;

	private readonly List<TriggerRule> _triggers = new();
	private readonly HashSet<string> _played = new();
	private readonly Dictionary<string, bool> _flags = new();
	private DialogueRunner _runner;
	private string _playingScript = "";
	private string _currentMapName = "";
	private string _pendingCheckpoint = "";
	private System.Threading.Tasks.TaskCompletionSource<bool> _finishTcs =
		new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);

	public bool IsPlaying => _playingScript.Length > 0;

	public System.Threading.Tasks.Task WhenStoryFinishedAsync() => _finishTcs.Task;

	private sealed class TriggerRule
	{
		public string Event;
		public string Key;
		public string Map;
		public string Checkpoint;
		public string Script;
	}

	public override void _Ready()
	{
		Instance = this;
		_runner = new DialogueRunner();
		AddChild(_runner);
		_currentMapName = GetNodeOrNull<LevelDataManager>("../LevelDataManager")?.CurrentMapName ?? "";
		LoadFlags();
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

	public void Play(string scriptId, string checkpoint = "")
	{
		if (_runner == null || _playingScript.Length > 0) return;
		_playingScript = scriptId;
		_played.Add(scriptId);
		_pendingCheckpoint = checkpoint;
		_finishTcs = new System.Threading.Tasks.TaskCompletionSource<bool>(
			System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
		EventBus.Instance?.EmitSignal("StoryPlaybackStarted");
		_runner.Play(scriptId);
	}

	public void Trigger(string eventName, string key = "")
	{
		foreach (TriggerRule rule in _triggers)
		{
			if (rule.Event != eventName) continue;
			if (!string.IsNullOrEmpty(rule.Key) && rule.Key != key) continue;
			if (!string.IsNullOrEmpty(rule.Map) && rule.Map != _currentMapName) continue;
			if (!string.IsNullOrEmpty(rule.Checkpoint) && GetFlag(rule.Checkpoint)) continue;
			if (_played.Contains(rule.Script)) continue;
			Play(rule.Script, rule.Checkpoint);
			return;
		}
	}

	public void NotifyFinished()
	{
		if (_pendingCheckpoint.Length > 0)
		{
			SetFlag(_pendingCheckpoint, true);
		}
		_pendingCheckpoint = "";
		_playingScript = "";
		EventBus.Instance?.EmitSignal("StoryPlaybackEnded");
		Finished?.Invoke();
		_finishTcs.TrySetResult(true);
	}

	public void SetFlag(string key, bool value)
	{
		_flags[key] = value;
		SaveFlags();
	}

	public void SetMapName(string mapName)
	{
		_currentMapName = mapName ?? "";
		LoadTriggers();
	}

	public bool GetFlag(string key)
		=> _flags.TryGetValue(key, out bool value) && value;

	public IEnumerable<string> GetTrueFlags()
		=> _flags.Where(pair => pair.Value).Select(pair => pair.Key);

	private void LoadTriggers()
	{
		_triggers.Clear();
		LoadTriggerFile("res://Data/Stories/global.json");
		if (_currentMapName.Length > 0)
		{
			LoadTriggerFile($"res://Data/Stories/maps/{_currentMapName}.json");
		}
	}

	private void LoadTriggerFile(string path)
	{
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
				Map = entry.TryGetProperty("map", out JsonElement mapProp)
					? mapProp.GetString() ?? ""
					: "",
				Checkpoint = entry.TryGetProperty("checkpoint", out JsonElement checkpointProp)
					? checkpointProp.GetString() ?? ""
					: "",
				Script = entry.TryGetProperty("script", out JsonElement scriptProp)
					? scriptProp.GetString() ?? ""
					: "",
			});
		}
	}

	private const string FlagFilePath = "user://story_flags.json";

	private void LoadFlags()
	{
		if (!FileAccess.FileExists(FlagFilePath)) return;
		using var file = FileAccess.Open(FlagFilePath, FileAccess.ModeFlags.Read);
		using var document = JsonDocument.Parse(file.GetAsText());
		JsonElement root = document.RootElement;
		foreach (JsonProperty property in root.EnumerateObject())
		{
			_flags[property.Name] = property.Value.ValueKind == JsonValueKind.True;
		}
	}

	private void SaveFlags()
	{
		using var file = FileAccess.Open(FlagFilePath, FileAccess.ModeFlags.Write);
		if (file == null) return;
		file.StoreString(JsonSerializer.Serialize(_flags));
	}
}

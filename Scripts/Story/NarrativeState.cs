using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace DreadnoughtDeparture.Story;

public enum NarrativeStatus
{
	Idle,
	Playing,
	WaitingChoice,
	Completed,
}

public sealed class NarrativeOption
{
	public string Text = "";
	public int Next = -1;
}

public sealed class NarrativeStep
{
	public string Type = "";
	public string Speaker = "";
	public string Text = "";
	public float Seconds;
	public string Key = "";
	public bool Value;
	public string Background = "";
	public List<NarrativeOption> Options = new();
}

public sealed class NarrativeSnapshot
{
	public string ScriptId = "";
	public int Index;
	public NarrativeStatus Status = NarrativeStatus.Idle;
	public string Background = "";
	public List<string> History = new();
	public Dictionary<string, bool> Flags = new();
}

/// <summary>剧情状态机：可加载、推进、回退、跳转、快照/恢复，方便演示与调试。</summary>
public sealed class NarrativeState
{
	public string ScriptId { get; private set; } = "";
	public string Background { get; private set; } = "";
	public NarrativeStatus Status { get; private set; } = NarrativeStatus.Idle;
	public int Index { get; private set; }
	public List<NarrativeStep> Steps { get; private set; } = new();
	public List<string> History { get; } = new();
	public Dictionary<string, bool> Flags { get; } = new();

	public NarrativeStep Current
		=> Index >= 0 && Index < Steps.Count ? Steps[Index] : null;

	public bool CanBack => Index > 0;
	public bool CanAdvance => Index < Steps.Count - 1;
	public int StepCount => Steps.Count;

	/// <summary>
	/// 加载剧情脚本。scriptId 是逻辑 id；filePath 可指向 Data/Stories 下的嵌套文件，
	/// 默认为 Data/Stories/&lt;scriptId&gt;.json，scriptId 支持斜杠路径。
	/// </summary>
	public bool Load(string scriptId, string filePath = null)
	{
		string path = string.IsNullOrEmpty(filePath)
			? $"res://Data/Stories/{scriptId}.json"
			: filePath;
		if (!FileAccess.FileExists(path)) return false;
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		using var document = JsonDocument.Parse(file.GetAsText());
		JsonElement root = document.RootElement;

		ScriptId = scriptId;
		Background = Str(root, "background");
		Index = 0;
		Status = NarrativeStatus.Playing;
		Steps = new List<NarrativeStep>();
		History.Clear();
		Flags.Clear();
		if (root.TryGetProperty("steps", out JsonElement stepsProp))
		{
			foreach (JsonElement stepElement in stepsProp.EnumerateArray())
			{
				Steps.Add(ParseStep(stepElement));
			}
		}
		return Steps.Count > 0;
	}

	public void RecordHistory(NarrativeStep step)
	{
		if (step == null) return;
		string text = string.IsNullOrEmpty(step.Speaker)
			? step.Text
			: $"{step.Speaker}：{step.Text}";
		if (!string.IsNullOrEmpty(text))
		{
			History.Add(text);
		}
	}

	public void Advance()
	{
		if (Index < Steps.Count - 1)
		{
			Index++;
			Status = NarrativeStatus.Playing;
		}
		else
		{
			Status = NarrativeStatus.Completed;
		}
	}

	public void Back()
	{
		if (Index > 0)
		{
			Index--;
			Status = NarrativeStatus.Playing;
		}
	}

	public void Jump(int index)
	{
		Index = System.Math.Clamp(index, 0, System.Math.Max(0, Steps.Count - 1));
		Status = NarrativeStatus.Playing;
	}

	public void SelectChoice(int optionIndex)
	{
		NarrativeStep step = Current;
		if (step == null || step.Type != "choice") return;
		if (optionIndex < 0 || optionIndex >= step.Options.Count) return;
		NarrativeOption option = step.Options[optionIndex];
		Index = option.Next >= 0 ? option.Next : Index + 1;
		Status = Index < Steps.Count ? NarrativeStatus.Playing : NarrativeStatus.Completed;
	}

	public void Complete()
	{
		Status = NarrativeStatus.Completed;
	}

	public NarrativeSnapshot Capture()
		=> new()
		{
			ScriptId = ScriptId,
			Index = Index,
			Status = Status,
			Background = Background,
			History = new List<string>(History),
			Flags = new Dictionary<string, bool>(Flags),
		};

	public void Restore(NarrativeSnapshot snapshot)
	{
		if (snapshot == null) return;
		ScriptId = snapshot.ScriptId;
		Index = snapshot.Index;
		Status = snapshot.Status;
		Background = snapshot.Background;
		History.Clear();
		History.AddRange(snapshot.History);
		Flags.Clear();
		foreach (var (key, value) in snapshot.Flags)
		{
			Flags[key] = value;
		}
	}

	private static NarrativeStep ParseStep(JsonElement element)
	{
		var step = new NarrativeStep
		{
			Type = Str(element, "type"),
			Speaker = Str(element, "speaker"),
			Text = Str(element, "text"),
			Seconds = element.TryGetProperty("seconds", out JsonElement seconds)
				? seconds.GetSingle()
				: 0f,
			Key = Str(element, "key"),
			Value = element.TryGetProperty("value", out JsonElement value)
				&& value.ValueKind == JsonValueKind.True,
			Background = Str(element, "color"),
		};
		if (element.TryGetProperty("options", out JsonElement options))
		{
			foreach (JsonElement option in options.EnumerateArray())
			{
				step.Options.Add(new NarrativeOption
				{
					Text = Str(option, "text"),
					Next = option.TryGetProperty("next", out JsonElement next)
						? next.GetInt32()
						: -1,
				});
			}
		}
		return step;
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";
}

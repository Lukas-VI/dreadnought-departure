using Godot;
using System;
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
	public float BackgroundAlpha = 1f;
	public string BackgroundImage = "";
	public string BackgroundOverlay = "";
	public string Avatar = "";
	public string AvatarExpression = "";
	public string AvatarAction = "";
	public string AvatarPosition = "left";
	public float AvatarScale = 1f;
	public List<NarrativeOption> Options = new();
}

public sealed class NarrativeSnapshot
{
	public string ScriptId = "";
	public int Index;
	public NarrativeStatus Status = NarrativeStatus.Idle;
	public string Background = "";
	public float BackgroundAlpha = 1f;
	public string BackgroundImage = "";
	public string BackgroundOverlay = "";
	public List<string> History = new();
	public Dictionary<string, bool> Flags = new();
}

/// <summary>剧情状态机：可加载、推进、回退、跳转、快照/恢复，方便演示与调试。</summary>
public sealed class NarrativeState
{
	public string ScriptId { get; private set; } = "";
	public string Background { get; private set; } = "";
	public float BackgroundAlpha { get; private set; } = 1f;
	public string BackgroundImage { get; private set; } = "";
	public string BackgroundOverlay { get; private set; } = "";
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
		try
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file == null) return false;
			string json = file.GetAsText();
			if (string.IsNullOrWhiteSpace(json)) return false;
			using var document = JsonDocument.Parse(json);
			return ParseDocument(scriptId, document.RootElement);
		}
		catch
		{
			return false;
		}
	}

	private bool ParseDocument(string scriptId, JsonElement root)
	{
		ScriptId = scriptId;
		ParseBackground(root, "background",
			out string background, out float backgroundAlpha,
			out string backgroundImage, out string backgroundOverlay);
		Background = background;
		BackgroundAlpha = backgroundAlpha;
		BackgroundImage = backgroundImage;
		BackgroundOverlay = backgroundOverlay;
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
			BackgroundAlpha = BackgroundAlpha,
			BackgroundImage = BackgroundImage,
			BackgroundOverlay = BackgroundOverlay,
		};

	public void Restore(NarrativeSnapshot snapshot)
	{
		if (snapshot == null) return;
		ScriptId = snapshot.ScriptId;
		Index = snapshot.Index;
		Status = snapshot.Status;
		Background = snapshot.Background;
		BackgroundAlpha = snapshot.BackgroundAlpha;
		BackgroundImage = snapshot.BackgroundImage;
		BackgroundOverlay = snapshot.BackgroundOverlay;
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
		ParseBackground(element, "color",
			out string background, out float backgroundAlpha,
			out string backgroundImage, out string backgroundOverlay);
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
			Background = background,
			BackgroundAlpha = backgroundAlpha,
			BackgroundImage = backgroundImage,
			BackgroundOverlay = backgroundOverlay,
		};
		ParseAvatar(element, step);
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

	private static void ParseAvatar(JsonElement element, NarrativeStep step)
	{
		step.Avatar = "";
		step.AvatarExpression = "";
		step.AvatarAction = "";
		step.AvatarPosition = "left";
		step.AvatarScale = 1f;
		if (element.TryGetProperty("avatar", out JsonElement avatar))
		{
			if (avatar.ValueKind == JsonValueKind.String)
			{
				step.Avatar = avatar.GetString() ?? "";
			}
			else if (avatar.ValueKind == JsonValueKind.Object)
			{
				step.Avatar = Str(avatar, "path");
				if (string.IsNullOrEmpty(step.Avatar))
				{
					step.Avatar = Str(avatar, "src");
				}
				step.AvatarExpression = Str(avatar, "expression");
				step.AvatarAction = Str(avatar, "action");
				if (string.IsNullOrEmpty(step.AvatarAction))
				{
					step.AvatarAction = Str(avatar, "pose");
				}
				step.AvatarPosition = Str(avatar, "position");
				step.AvatarScale = avatar.TryGetProperty("scale", out JsonElement scale)
					? scale.GetSingle()
					: 1f;
			}
		}
		string expression = Str(element, "avatar_expression");
		if (!string.IsNullOrEmpty(expression))
		{
			step.AvatarExpression = expression;
		}
		string action = Str(element, "avatar_action");
		if (string.IsNullOrEmpty(action))
		{
			action = Str(element, "avatar_pose");
		}
		if (!string.IsNullOrEmpty(action))
		{
			step.AvatarAction = action;
		}
		string position = Str(element, "avatar_position");
		if (!string.IsNullOrEmpty(position))
		{
			step.AvatarPosition = position;
		}
		if (element.TryGetProperty("avatar_scale", out JsonElement avatarScale))
		{
			step.AvatarScale = avatarScale.GetSingle();
		}
	}

	/// <summary>
	/// 解析立绘差分路径。basePath 可含 {expression} / {action} / {pose} 占位符；
	/// 否则按 base_表情_动作、base_表情、base_动作 的顺序尝试候选文件。
	/// </summary>
	public static string ResolveAvatarPath(string basePath, string expression, string action)
	{
		if (string.IsNullOrEmpty(basePath)) return "";
		string safeExpression = string.IsNullOrEmpty(expression) ? "default" : expression;
		string safeAction = string.IsNullOrEmpty(action) ? "default" : action;
		string path = basePath;
		if (path.Contains("{expression}"))
		{
			path = path.Replace("{expression}", safeExpression);
		}
		if (path.Contains("{action}"))
		{
			path = path.Replace("{action}", safeAction);
		}
		if (path.Contains("{pose}"))
		{
			path = path.Replace("{pose}", safeAction);
		}
		if (FileAccess.FileExists(path)) return path;

		string extension = System.IO.Path.GetExtension(basePath);
		if (string.IsNullOrEmpty(extension))
		{
			extension = ".png";
		}
		string noExtension = basePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
			? basePath.Substring(0, basePath.Length - extension.Length)
			: basePath;
		var candidates = new List<string>();
		if (!string.IsNullOrEmpty(expression) && !string.IsNullOrEmpty(action))
		{
			candidates.Add($"{noExtension}_{expression}_{action}{extension}");
		}
		if (!string.IsNullOrEmpty(expression))
		{
			candidates.Add($"{noExtension}_{expression}{extension}");
		}
		if (!string.IsNullOrEmpty(action))
		{
			candidates.Add($"{noExtension}_{action}{extension}");
		}
		if (!string.IsNullOrEmpty(expression) && !string.IsNullOrEmpty(action))
		{
			candidates.Add($"{noExtension}/{expression}/{action}{extension}");
		}
		if (!string.IsNullOrEmpty(expression))
		{
			candidates.Add($"{noExtension}/{expression}{extension}");
		}
		if (!string.IsNullOrEmpty(action))
		{
			candidates.Add($"{noExtension}/{action}{extension}");
		}
		foreach (string candidate in candidates)
		{
			if (FileAccess.FileExists(candidate))
			{
				return candidate;
			}
		}
		return basePath;
	}

	private static void ParseBackground(JsonElement element, string colorKey,
		out string color, out float alpha, out string image, out string overlay)
	{
		color = "";
		alpha = 1f;
		image = "";
		overlay = "";
		if (element.TryGetProperty("background", out JsonElement background))
		{
			if (background.ValueKind == JsonValueKind.String)
			{
				color = background.GetString() ?? "";
			}
			else if (background.ValueKind == JsonValueKind.Object)
			{
				color = Str(background, "color");
				if (string.IsNullOrEmpty(color))
				{
					color = Str(background, "background");
				}
				if (background.TryGetProperty("alpha", out JsonElement alphaProp))
				{
					alpha = alphaProp.GetSingle();
				}
				image = Str(background, "image");
				overlay = Str(background, "overlay");
			}
		}
		if (string.IsNullOrEmpty(color))
		{
			color = Str(element, colorKey);
		}
		if (element.TryGetProperty("alpha", out JsonElement stepAlpha))
		{
			alpha = stepAlpha.GetSingle();
		}
		if (string.IsNullOrEmpty(image))
		{
			image = Str(element, "background_image");
		}
		if (string.IsNullOrEmpty(overlay))
		{
			overlay = Str(element, "background_overlay");
		}
	}

	/// <summary>解析 #RGB / #RGBA / #RRGGBB / #RRGGBBAA，alpha 参数可覆盖十六进制末两位。</summary>
	public static Color ParseColor(string value, float alpha = -1f)
	{
		if (string.IsNullOrEmpty(value)) return Colors.White;
		string hex = value.TrimStart('#');
		try
		{
			if (hex.Length == 8)
			{
				float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
				float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
				float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
				float a = alpha >= 0f
					? alpha
					: Convert.ToInt32(hex.Substring(6, 2), 16) / 255f;
				return new Color(r, g, b, a);
			}
			if (hex.Length == 6)
			{
				float r = Convert.ToInt32(hex.Substring(0, 2), 16) / 255f;
				float g = Convert.ToInt32(hex.Substring(2, 2), 16) / 255f;
				float b = Convert.ToInt32(hex.Substring(4, 2), 16) / 255f;
				return new Color(r, g, b, alpha >= 0f ? alpha : 1f);
			}
			if (hex.Length == 4)
			{
				float r = Convert.ToInt32(hex[0].ToString(), 16) / 15f;
				float g = Convert.ToInt32(hex[1].ToString(), 16) / 15f;
				float b = Convert.ToInt32(hex[2].ToString(), 16) / 15f;
				float a = alpha >= 0f
					? alpha
					: Convert.ToInt32(hex[3].ToString(), 16) / 15f;
				return new Color(r, g, b, a);
			}
			if (hex.Length == 3)
			{
				float r = Convert.ToInt32(hex[0].ToString(), 16) / 15f;
				float g = Convert.ToInt32(hex[1].ToString(), 16) / 15f;
				float b = Convert.ToInt32(hex[2].ToString(), 16) / 15f;
				return new Color(r, g, b, alpha >= 0f ? alpha : 1f);
			}
		}
		catch
		{
			// 交给 Godot 的 FromHtml 兜底。
		}
		return Color.FromHtml(value);
	}
}

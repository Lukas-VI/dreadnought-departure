using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// PV 评分求值器。
/// 简化计分：沉没记全额 PV，大破记一半，中破记四分之一，小破与完好不计分。
/// 具体剧本可能另有倍率与阈值，后续可在关卡 JSON 中扩展。
/// </summary>
public static class VictoryRulesEvaluator
{
	public enum VictoryResult
	{
		None = 0,
		PlayerWin = 1,
		EnemyWin = 2,
		Draw = 3,
	}

	public sealed class VictorySnapshot
	{
		public int Turn;
		public int MaxTurns;
		public int PlayerAlive;
		public int EnemyAlive;
		public int PlayerDestroyed;
		public int EnemyDestroyed;
		public HashSet<Vector2I> PlayerReached = new();
		public HashSet<Vector2I> EnemyReached = new();
		public HashSet<string> CompletedCheckpoints = new();
		public Dictionary<string, int> ActionCounts = new();
	}

	public static int PVScoreForState(int pv, DamageState state) => state switch
	{
		DamageState.Sunk => pv,
		DamageState.Heavy => pv / 2,
		DamageState.Moderate => pv / 4,
		_ => 0
	};

	public static int FleetScore(IEnumerable<ShipComponent> ships)
	{
		int score = 0;
		foreach (var ship in ships ?? Enumerable.Empty<ShipComponent>())
			if (GodotObject.IsInstanceValid(ship))
				score += PVScoreForState(ship.PV, ship.DamageState);
		return score;
	}

	/// <summary>按地图 Victory JSON 求值声明式胜负条件；未配置返回 None。</summary>
	public static VictoryResult Evaluate(string victoryJson, VictorySnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(victoryJson) || snapshot == null)
		{
			return VictoryResult.None;
		}
		try
		{
			using var document = JsonDocument.Parse(victoryJson);
			return EvaluateRoot(document.RootElement, snapshot);
		}
		catch
		{
			return VictoryResult.None;
		}
	}

	/// <summary>只有 Victory JSON 是合法对象且确实声明了条件时，才算“使用自定义胜利条件”。</summary>
	public static bool IsConfigured(string victoryJson)
	{
		if (string.IsNullOrWhiteSpace(victoryJson)) return false;
		try
		{
			using var document = JsonDocument.Parse(victoryJson);
			JsonElement root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object) return false;
			return root.TryGetProperty("conditions", out _)
				|| root.TryGetProperty("defeatConditions", out _)
				|| root.TryGetProperty("turnLimit", out _);
		}
		catch
		{
			return false;
		}
	}

	private static VictoryResult EvaluateRoot(JsonElement root, VictorySnapshot snapshot)
	{
		if (root.ValueKind != JsonValueKind.Object)
		{
			return VictoryResult.None;
		}
		if (!root.TryGetProperty("conditions", out JsonElement conditions))
		{
			conditions = default;
		}
		if (conditions.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement condition in conditions.EnumerateArray())
			{
				VictoryResult result = CheckCondition(condition, snapshot);
				if (result != VictoryResult.None)
				{
					return result;
				}
			}
		}

		if (root.TryGetProperty("defeatConditions", out JsonElement defeats)
			&& defeats.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement condition in defeats.EnumerateArray())
			{
				VictoryResult result = CheckCondition(condition, snapshot, true);
				if (result != VictoryResult.None)
				{
					return result;
				}
			}
		}

		if (root.TryGetProperty("turnLimit", out JsonElement turnLimit))
		{
			int limit = turnLimit.GetInt32();
			if (limit > 0 && snapshot.Turn >= limit)
			{
				bool draw = root.TryGetProperty("timeout", out JsonElement timeout)
					&& timeout.GetString() == "draw";
				return draw ? VictoryResult.Draw : VictoryResult.EnemyWin;
			}
		}
		return VictoryResult.None;
	}

	private static VictoryResult CheckCondition(
		JsonElement condition, VictorySnapshot snapshot, bool defeat = false)
	{
		if (condition.ValueKind != JsonValueKind.Object)
		{
			return VictoryResult.None;
		}
		string type = Str(condition, "type");
		string side = Str(condition, "side");
		bool enemySide = side == "enemy";
		VictoryResult baseResult = enemySide ? VictoryResult.EnemyWin : VictoryResult.PlayerWin;
		if (defeat)
		{
			baseResult = enemySide ? VictoryResult.PlayerWin : VictoryResult.EnemyWin;
		}
		int count = Int(condition, "count", 1);

		switch (type)
		{
			case "reach":
			{
				var reached = enemySide ? snapshot.EnemyReached : snapshot.PlayerReached;
				int hit = 0;
				if (condition.TryGetProperty("hexes", out JsonElement hexes))
				{
					foreach (JsonElement hex in hexes.EnumerateArray())
					{
						if (reached.Contains(ParseHex(hex.GetString())))
						{
							hit++;
						}
					}
				}
				return hit >= count ? baseResult : VictoryResult.None;
			}
			case "checkpoint":
			{
				if (condition.TryGetProperty("checkpoint", out JsonElement checkpoint))
				{
					return snapshot.CompletedCheckpoints.Contains(checkpoint.GetString() ?? "")
						? baseResult
						: VictoryResult.None;
				}
				if (condition.TryGetProperty("checkpoints", out JsonElement checkpoints))
				{
					int hit = 0;
					foreach (JsonElement item in checkpoints.EnumerateArray())
					{
						if (snapshot.CompletedCheckpoints.Contains(item.GetString() ?? ""))
						{
							hit++;
						}
					}
					return hit >= count ? baseResult : VictoryResult.None;
				}
				return VictoryResult.None;
			}
			case "action":
			{
				string action = Str(condition, "action");
				int current = snapshot.ActionCounts.TryGetValue(action, out int value) ? value : 0;
				return current >= count ? baseResult : VictoryResult.None;
			}
			case "destroy":
			{
				int destroyed = enemySide ? snapshot.PlayerDestroyed : snapshot.EnemyDestroyed;
				return destroyed >= count ? baseResult : VictoryResult.None;
			}
			case "alive":
			{
				int alive = enemySide ? snapshot.EnemyAlive : snapshot.PlayerAlive;
				return alive >= count ? baseResult : VictoryResult.None;
			}
			default:
				return VictoryResult.None;
		}
	}

	private static Vector2I ParseHex(string text)
	{
		if (string.IsNullOrEmpty(text)) return Vector2I.Zero;
		string[] parts = text.Split(',');
		if (parts.Length >= 2 && int.TryParse(parts[0], out int q) && int.TryParse(parts[1], out int r))
		{
			return new Vector2I(q, r);
		}
		return Vector2I.Zero;
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";

	private static int Int(JsonElement element, string property, int fallback)
		=> element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number
			? value.GetInt32()
			: fallback;
}

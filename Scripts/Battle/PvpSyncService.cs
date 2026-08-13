using Godot;
using System.Collections.Generic;
using System.Text.Json;

namespace DreadnoughtDeparture.Core;

/// <summary>PvP 远程状态同步：阶段映射与鱼雷实体同步。</summary>
public static class PvpSyncService
{
	public static BattlePhase RemotePhaseToLocal(string phase)
	{
		return phase switch
		{
			"speed" => BattlePhase.SpeedAdjust,
			"move1" => BattlePhase.MovePhase1,
			"move2" => BattlePhase.MovePhase2,
			"move3" => BattlePhase.MovePhase3,
			"recon" => BattlePhase.ReconLighting,
			"gunnery" => BattlePhase.Gunfire,
			"torpedo" => BattlePhase.Torpedo,
			_ => BattlePhase.SpeedAdjust,
		};
	}

	public static void SyncRemoteTorpedoes(
		JsonElement torpedoes,
		TorpedoController controller,
		MapGenerator map,
		Dictionary<string, TorpedoComponent> remoteTorpedoes)
	{
		if (controller == null || map == null) return;
		var seen = new HashSet<string>();
		foreach (JsonElement entry in torpedoes.EnumerateArray())
		{
			string id = entry.TryGetProperty("id", out JsonElement idProp)
				? idProp.GetString() ?? ""
				: "";
			if (string.IsNullOrEmpty(id)) continue;
			JsonElement hex = entry.TryGetProperty("hex", out JsonElement hexProp)
				? hexProp
				: default;
			if (hex.ValueKind != JsonValueKind.Array || hex.GetArrayLength() < 2) continue;
			int side = entry.TryGetProperty("side", out JsonElement sideProp)
				? sideProp.GetInt32()
				: 0;
			int direction = entry.TryGetProperty("direction", out JsonElement dirProp)
				? dirProp.GetInt32()
				: 0;
			int speed = entry.TryGetProperty("speed", out JsonElement speedProp)
				? speedProp.GetInt32()
				: 6;
			int range = entry.TryGetProperty("remainingRange", out JsonElement rangeProp)
				? rangeProp.GetInt32()
				: 4;
			int count = entry.TryGetProperty("count", out JsonElement countProp)
				? countProp.GetInt32()
				: 1;
			int hitMode = entry.TryGetProperty("hitMode", out JsonElement modeProp)
				? modeProp.GetInt32()
				: 7;
			int damage = entry.TryGetProperty("torpedoDamage", out JsonElement damageProp)
				? damageProp.GetInt32()
				: 30;
			string type = entry.TryGetProperty("torpedoType", out JsonElement typeProp)
				? typeProp.GetString() ?? ""
				: "鱼雷";
			int fanSide = entry.TryGetProperty("fanSide", out JsonElement fanSideProp)
				? fanSideProp.GetInt32()
				: -1;
			int fanBranch = entry.TryGetProperty("fanBranch", out JsonElement fanBranchProp)
				? fanBranchProp.GetInt32()
				: 0;
			Vector2I coords = new Vector2I(hex[0].GetInt32(), hex[1].GetInt32());
			seen.Add(id);

			if (!remoteTorpedoes.TryGetValue(id, out TorpedoComponent torpedo))
			{
				torpedo = controller.SpawnTorpedo(
					id, side, coords, (HexDirection)(direction % 6),
					speed, range, count, hitMode, damage, type, null,
					fanSide, fanBranch, map);
				if (torpedo == null) continue;
				remoteTorpedoes[id] = torpedo;
			}
			else
			{
				if (torpedo.Hex != coords)
				{
					torpedo.AnimateMoveTo(map, coords, 0.3f);
				}
				torpedo.Direction = (HexDirection)(direction % 6);
				torpedo.RemainingRange = range;
				torpedo.RangeSpent = entry.TryGetProperty("rangeSpent", out JsonElement spentProp)
					? spentProp.GetInt32()
					: torpedo.RangeSpent;
				torpedo.Count = count;
				torpedo.FanSide = fanSide;
				torpedo.FanBranch = fanBranch;
				torpedo.ApplyVisual();
			}
		}

		var stale = new List<string>();
		foreach (string id in remoteTorpedoes.Keys)
		{
			if (!seen.Contains(id))
			{
				stale.Add(id);
			}
		}
		foreach (string id in stale)
		{
			if (remoteTorpedoes.Remove(id, out TorpedoComponent torpedo))
			{
				controller.RemoveTorpedo(torpedo);
			}
		}
	}
}

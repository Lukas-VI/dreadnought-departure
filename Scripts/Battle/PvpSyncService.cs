using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace DreadnoughtDeparture.Core;

/// <summary>PvP 远程状态同步：阶段映射与鱼雷实体同步。</summary>
public static class PvpSyncService
{
	public sealed class RemoteShipSyncContext
	{
		public Dictionary<string, ShipComponent> RemoteShips = new();
		public Dictionary<string, Tween> RemoteTweens = new();
		public UnitSpawner UnitSpawner;
		public MapGenerator MapGenerator;
		public string MyUserId = "";
	}

	/// <summary>
	/// 同步服务端舰船：创建/更新/移除组件、堆叠并排、补间移动，
	/// 返回按阵营分好的列表。
	/// </summary>
	public static (List<ShipComponent> PlayerShips, List<ShipComponent> EnemyShips) ApplyShips(
		JsonElement state,
		RemoteShipSyncContext ctx)
	{
		if (ctx == null || ctx.UnitSpawner == null || ctx.MapGenerator == null)
		{
			return (new List<ShipComponent>(), new List<ShipComponent>());
		}
		if (!state.TryGetProperty("ships", out JsonElement ships))
		{
			return (new List<ShipComponent>(), new List<ShipComponent>());
		}

		var seen = new HashSet<string>();
		var pending = new List<(string Id, ShipComponent Ship, Vector2I Hex, bool IsNew, int StackIndex, int StackTotal)>();
		int mySide = 0;
		if (state.TryGetProperty("players", out JsonElement players))
		{
			int index = 0;
			foreach (JsonElement player in players.EnumerateArray())
			{
				if (player.GetString() == ctx.MyUserId)
				{
					mySide = index;
					break;
				}
				index++;
			}
		}
		foreach (JsonElement ship in ships.EnumerateArray())
		{
			bool isNew = false;
			string id = ship.TryGetProperty("id", out JsonElement idProp)
				? idProp.GetString() ?? ""
				: "";
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}

			JsonElement hex = ship.TryGetProperty("hex", out JsonElement hexProp)
				? hexProp
				: default;
			if (hex.ValueKind != JsonValueKind.Array || hex.GetArrayLength() < 2)
			{
				continue;
			}

			int side = ship.TryGetProperty("side", out JsonElement sideProp)
				? sideProp.GetInt32()
				: 0;
			int facing = ship.TryGetProperty("facing", out JsonElement facingProp)
				? facingProp.GetInt32()
				: 0;
			int speed = ship.TryGetProperty("speed", out JsonElement speedProp)
				? speedProp.GetInt32()
				: 0;
			int hp = ship.TryGetProperty("hp", out JsonElement hpProp)
				? hpProp.GetInt32()
				: 0;
			int maxHp = ship.TryGetProperty("maxHp", out JsonElement maxHpProp)
				? maxHpProp.GetInt32()
				: 0;
			int mainAmmo = ship.TryGetProperty("mainAmmo", out JsonElement mainAmmoProp)
				? mainAmmoProp.GetInt32()
				: -1;
			int torpedoLeft = ship.TryGetProperty("torpedoLeftRemaining", out JsonElement torpedoLeftProp)
				? torpedoLeftProp.GetInt32()
				: -1;
			int torpedoCenter = ship.TryGetProperty("torpedoCenterRemaining", out JsonElement torpedoCenterProp)
				? torpedoCenterProp.GetInt32()
				: -1;
			int torpedoRight = ship.TryGetProperty("torpedoRightRemaining", out JsonElement torpedoRightProp)
				? torpedoRightProp.GetInt32()
				: -1;
			int torpedoReloads = ship.TryGetProperty("torpedoReloadsRemaining", out JsonElement reloadProp)
				? reloadProp.GetInt32()
				: -1;
			int stackIndex = ship.TryGetProperty("stackIndex", out JsonElement stackIndexProp)
				? stackIndexProp.GetInt32()
				: 0;
			int stackTotal = ship.TryGetProperty("stackTotal", out JsonElement stackTotalProp)
				? stackTotalProp.GetInt32()
				: 1;

			if (!ctx.RemoteShips.TryGetValue(id, out ShipComponent component))
			{
				string shipId = ship.TryGetProperty("shipId", out JsonElement shipIdProp)
					? shipIdProp.GetString() ?? ""
					: "";
				PackedScene prefab = ShipCatalog.GetScene(shipId);
				if (prefab == null)
				{
					prefab = ResourceLoader.Load<PackedScene>(
						"res://Ships/BaseShip/ship_3d.tscn");
				}
				if (prefab == null)
				{
					continue;
				}
				component = prefab.Instantiate<ShipComponent>();
				ctx.UnitSpawner.AddChild(component);
				component.SetMeta("serverShipId", id);
				ShipCatalog.Entry entry = ShipCatalog.Get(shipId);
				if (entry?.Data != null)
				{
					component.ApplyData(entry.Data);
				}
				ctx.RemoteShips[id] = component;
				isNew = true;
			}

			seen.Add(id);
			component.BattleSide = side == mySide
				? GenerationSide.Player
				: GenerationSide.Enemy;
			Vector2I coords = new Vector2I(hex[0].GetInt32(), hex[1].GetInt32());
			int previousFacing = (int)component.Direction;
			if (isNew)
			{
				component.Direction = (HexDirection)(facing % 6);
				component.TurnedThisPhase = false;
			}
			else if (previousFacing != facing % 6)
			{
				component.AnimateTurnTo((HexDirection)(facing % 6));
			}
			component.CurrentSpeed = speed;
			if (maxHp > 0)
			{
				component.MaxHp = maxHp;
				component.CurrentHp = Math.Clamp(hp, 0, maxHp);
			}
			if (mainAmmo >= 0)
			{
				component.MainAmmo = mainAmmo;
			}
			if (torpedoLeft >= 0) component.TorpedoLeftRemaining = torpedoLeft;
			if (torpedoCenter >= 0) component.TorpedoCenterRemaining = torpedoCenter;
			if (torpedoRight >= 0) component.TorpedoRightRemaining = torpedoRight;
			if (torpedoReloads >= 0) component.TorpedoReloadsRemaining = torpedoReloads;
			component.TorpedoFiredThisTurn =
				ship.TryGetProperty("torpedoFiredThisTurn", out JsonElement firedProp)
					&& firedProp.ValueKind == JsonValueKind.True;
			component.TorpedoFiredLastTurn =
				ship.TryGetProperty("torpedoFiredLastTurn", out JsonElement firedLastProp)
					&& firedLastProp.ValueKind == JsonValueKind.True;
			LevelDataManager.BattlefieldUnits[coords] = component;
			pending.Add((id, component, coords, isNew, stackIndex, stackTotal));
		}

		var stale = new List<string>();
		foreach (string key in ctx.RemoteShips.Keys)
		{
			if (!seen.Contains(key))
			{
				stale.Add(key);
			}
		}
		foreach (string key in stale)
		{
			ShipComponent dead = ctx.RemoteShips[key];
			if (LevelDataManager.BattlefieldUnits.TryGetValue(dead.HexCoords, out ShipComponent current)
				&& current == dead)
			{
				LevelDataManager.BattlefieldUnits.Remove(dead.HexCoords);
			}
			dead.QueueFree();
			ctx.RemoteShips.Remove(key);
		}

		var stacks = new Dictionary<(Vector2I Hex, int Side), List<ShipComponent>>();
		foreach (var entry in pending)
		{
			if (!stacks.TryGetValue((entry.Hex, (int)entry.Ship.BattleSide), out var group))
			{
				group = new List<ShipComponent>();
				stacks[(entry.Hex, (int)entry.Ship.BattleSide)] = group;
			}
			group.Add(entry.Ship);
		}
		foreach (var group in stacks.Values)
		{
			group.Sort((a, b) =>
				pending.Find(entry => entry.Ship == a).StackIndex
					.CompareTo(pending.Find(entry => entry.Ship == b).StackIndex));
		}

		foreach (var kv in stacks)
		{
			Vector2I hex = kv.Key.Hex;
			List<ShipComponent> group = kv.Value;
			Vector3 center = ctx.MapGenerator.HexToWorld(hex.X, hex.Y);
			Vector2I forwardOff = HexDirectionUtility.Offset(group[0].Direction);
			Vector3 forward = ctx.MapGenerator.HexToWorld(forwardOff.X, forwardOff.Y)
				- ctx.MapGenerator.HexToWorld(0, 0);
			forward.Y = 0f;
			Vector3 lateral = new Vector3(forward.Z, 0f, -forward.X);
			if (lateral.LengthSquared() > 0f)
			{
				lateral = lateral.Normalized();
			}

			for (int i = 0; i < group.Count; i++)
			{
				ShipComponent ship = group[i];
				float zOffset = group.Count <= 1
					? 0f
					: (i - (group.Count - 1) / 2f) * ShipComponent.StackZStep;
				Vector3 to = new Vector3(center.X, ShipComponent.StackBaseY, center.Z)
					+ lateral * zOffset;
				ship.HexCoords = hex;
				string serverId = ship.GetMeta("serverShipId", "").AsString();
				bool isNew = pending.Find(entry => entry.Ship == ship).IsNew;
				if (isNew)
				{
					ship.Position = to;
				}
				else
				{
					if (ctx.RemoteTweens.TryGetValue(serverId, out Tween oldTween))
					{
						oldTween.Kill();
						ctx.RemoteTweens.Remove(serverId);
					}
					Tween tween = ship.CreateTween();
					tween.SetTrans(Tween.TransitionType.Quad);
					tween.SetEase(Tween.EaseType.InOut);
					tween.TweenProperty(ship, "position", to, 0.35f);
					ctx.RemoteTweens[serverId] = tween;
					tween.Finished += () =>
					{
						if (ctx.RemoteTweens.TryGetValue(serverId, out Tween current)
							&& current == tween)
						{
							ctx.RemoteTweens.Remove(serverId);
						}
					};
				}
			}
		}

		return (
			ctx.RemoteShips.Values
				.Where(ship => ship.BattleSide == GenerationSide.Player)
				.ToList(),
			ctx.RemoteShips.Values
				.Where(ship => ship.BattleSide == GenerationSide.Enemy)
				.ToList());
	}

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

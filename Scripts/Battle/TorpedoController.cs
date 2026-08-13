using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DreadnoughtDeparture.Core;

/// <summary>鱼雷管理器：生成占位实体、逐阶段移动、命中结算，并支持远程同步。</summary>
public partial class TorpedoController : Node3D
{
	private const string TorpedoPrefabPath = "res://Scenes/Battle/Torpedo/torpedo.tscn";

	private PackedScene _prefab;
	private readonly List<TorpedoComponent> _torpedoes = new();

	public IReadOnlyList<TorpedoComponent> Torpedoes => _torpedoes;

	public override void _Ready()
	{
		_prefab = ResourceLoader.Load<PackedScene>(TorpedoPrefabPath);
	}

	public TorpedoComponent SpawnTorpedo(string id, int side, Vector2I hex,
		HexDirection direction, int speed, int range, int count, int hitMode,
		int damage, string type, ShipComponent launcher, int fanSide,
		int fanBranch, MapGenerator map)
	{
		if (_prefab == null || map == null) return null;
		TorpedoComponent torpedo = _prefab.Instantiate<TorpedoComponent>();
		AddChild(torpedo);
		torpedo.Setup(id, side, hex, direction, speed, range, count, hitMode,
			damage, string.IsNullOrEmpty(type) ? "鱼雷" : type, launcher,
			fanSide, fanBranch);
		Vector3 world = map.HexToWorld(hex.X, hex.Y);
		torpedo.Position = new Vector3(world.X, 0.18f, world.Z);
		_torpedoes.Add(torpedo);
		return torpedo;
	}

	public void RemoveTorpedo(TorpedoComponent torpedo)
	{
		if (torpedo == null || !_torpedoes.Contains(torpedo)) return;
		_torpedoes.Remove(torpedo);
		torpedo.QueueFree();
	}

	public async Task MoveTorpedoesAsync(MapGenerator map, LevelDataManager data,
		int phase, bool oddTurn, IReadOnlyList<ShipComponent> ships)
	{
		if (_torpedoes.Count == 0 || map == null || data == null) return;
		float longest = 0f;
		var survivors = new List<TorpedoComponent>();
		var doomed = new List<TorpedoComponent>();
		foreach (TorpedoComponent torpedo in _torpedoes)
		{
			int steps = SpeedTable.MoveForPhase(torpedo.Speed, phase, oddTurn);
			if (steps <= 0)
			{
				survivors.Add(torpedo);
				continue;
			}

			var path = new List<Vector2I>();
			Vector2I cursor = torpedo.Hex;
			bool remove = false;
			ShipComponent nearest = FindNearestEnemy(ships, torpedo);
			for (int i = 0; i < steps; i++)
			{
				int branch = torpedo.FanBranch;
				if (nearest != null)
				{
					branch = TorpedoRulesEvaluator.ChooseBranch(
						cursor, torpedo.Direction, torpedo.FanSide,
						nearest.HexCoords, torpedo.FanBranch);
				}
				var candidates = TorpedoRulesEvaluator.CandidateOffsets(
					torpedo.Direction, torpedo.FanSide);
				Vector2I next = cursor + candidates[branch];
				if (data.IsIsland(next) || !data.TerrainSources.ContainsKey(next))
				{
					int other = 1 - branch;
					Vector2I alt = cursor + candidates[other];
					if (!data.IsIsland(alt) && data.TerrainSources.ContainsKey(alt))
					{
						branch = other;
						next = alt;
					}
					else
					{
						remove = true;
						break;
					}
				}
				torpedo.FanBranch = branch;
				cursor = next;
				if (data.IsIsland(cursor) || !data.TerrainSources.ContainsKey(cursor))
				{
					remove = true;
					break;
				}
				path.Add(cursor);
				if (torpedo.RemainingRange - 1 <= 0) break;
			}
			if (path.Count == 0)
			{
				if (remove)
				{
					doomed.Add(torpedo);
				}
				else
				{
					survivors.Add(torpedo);
				}
				continue;
			}

			float duration = Mathf.Max(0.18f, 0.1f + 0.18f / path.Count);
			longest = Mathf.Max(longest, duration * path.Count);
			torpedo.AnimateMovePath(map, path, duration);
			survivors.Add(torpedo);
			if (remove)
			{
				doomed.Add(torpedo);
			}
		}

		if (longest > 0f)
		{
			await ToSignal(GetTree().CreateTimer(longest + 0.15f), "timeout");
		}

		var toRemove = new List<TorpedoComponent>(doomed);
		foreach (TorpedoComponent torpedo in survivors)
		{
			if (toRemove.Contains(torpedo)) continue;
			if (torpedo.RemainingRange <= 0)
			{
				GetNode<EventBus>("../EventBus")?.EmitLog(
					$"{torpedo.TorpedoType} 鱼雷航程耗尽，落水消失");
				toRemove.Add(torpedo);
				continue;
			}
			ShipComponent target = FindTargetAt(ships, torpedo);
			if (target == null) continue;
			var (hit, damage, detail) = TorpedoRulesEvaluator.ResolveHit(torpedo, target);
			var bus = GetNode<EventBus>("../EventBus");
			bus?.EmitLog(detail);
			if (hit)
			{
				target.PendingDamage += damage;
				target.PendingShotChecks.Add(detail);
				if (GodotObject.IsInstanceValid(target))
				{
					bus?.EmitSignal("HitFeedbackRequested", target, true, damage);
				}
			}
			toRemove.Add(torpedo);
		}
		foreach (TorpedoComponent torpedo in toRemove)
		{
			RemoveTorpedo(torpedo);
		}
	}

	private static ShipComponent FindTargetAt(IReadOnlyList<ShipComponent> ships,
		TorpedoComponent torpedo)
	{
		foreach (ShipComponent ship in ships)
		{
			if (ship == null || !GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0) continue;
			if (ship.HexCoords == torpedo.Hex && ship.BattleSide != (GenerationSide)torpedo.Side)
			{
				return ship;
			}
		}
		return null;
	}

	private static ShipComponent FindNearestEnemy(IReadOnlyList<ShipComponent> ships,
		TorpedoComponent torpedo)
	{
		ShipComponent nearest = null;
		int best = int.MaxValue;
		foreach (ShipComponent ship in ships)
		{
			if (ship == null || !GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0) continue;
			if (ship.BattleSide == (GenerationSide)torpedo.Side) continue;
			int dist = BattleRulesEvaluator.GetHexDistance(torpedo.Hex, ship.HexCoords);
			if (dist < best)
			{
				best = dist;
				nearest = ship;
			}
		}
		return nearest;
	}

	public void Clear()
	{
		foreach (TorpedoComponent torpedo in _torpedoes)
		{
			torpedo.QueueFree();
		}
		_torpedoes.Clear();
	}
}

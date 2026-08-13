using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 移动结算服务：逐格路径、阻挡、堆叠占位、单纵阵轨迹与“先走再转”。
/// 只处理规则与 ShipComponent 状态，动画由调用方决定。
/// </summary>
public sealed class MoveSettlementService
{
	public sealed class FormationTrail
	{
		public readonly List<Vector2I> Cells = new();
		public readonly List<HexDirection> Headings = new();
		public readonly List<ShipComponent> Members = new();
	}

	public MapGenerator Map;
	public LevelDataManager Data;
	public Func<ShipComponent, bool> IsAlive;
	public Action RefreshCommandValues;

	public Dictionary<ShipComponent, FormationTrail> Trails { get; } = new();

	public Dictionary<Vector2I, List<ShipComponent>> PrepareOccupied(
		IEnumerable<ShipComponent> ships)
	{
		var occupied = new Dictionary<Vector2I, List<ShipComponent>>();
		foreach (ShipComponent ship in ships)
		{
			if (IsAlive(ship))
			{
				AddStackOccupant(occupied, ship.HexCoords, ship);
			}
		}
		return occupied;
	}

	public (List<ShipComponent> Ordered, List<List<ShipComponent>> Chains) OrderShips(
		IEnumerable<ShipComponent> ships)
	{
		var ordered = new List<ShipComponent>();
		var chains = new List<List<ShipComponent>>();
		var processed = new HashSet<ShipComponent>();
		foreach (GenerationSide side in new[] { GenerationSide.Player, GenerationSide.Enemy })
		{
			var sideShips = ships
				.Where(s => IsAlive(s) && s.BattleSide == side)
				.ToList();
			foreach (ShipComponent ship in sideShips)
			{
				if (processed.Contains(ship)) continue;
				if (ship.FormationLead != null && ReferenceEquals(ship.FormationLead, ship))
				{
					var chain = sideShips.Where(s => ReferenceEquals(s.FormationLead, ship))
						.OrderBy(s => s.FormationIndex)
						.ToList();
					if (chain.Count < 2)
					{
						ordered.Add(ship);
						processed.Add(ship);
						continue;
					}
					chains.Add(chain);
					foreach (ShipComponent member in chain)
					{
						processed.Add(member);
						ordered.Add(member);
					}
				}
				else
				{
					ordered.Add(ship);
					processed.Add(ship);
				}
			}
		}
		return (ordered, chains);
	}

	public float AnimateStraightShip(ShipComponent ship, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(ship.CurrentSpeed, phase, oddTurn);
		var path = MoveRulesEvaluator.BuildMovePath(ship.HexCoords, ship.Direction, requestedSteps);
		var moved = ResolveMovePath(ship, path, bus, occupiedShips);
		if (moved.Count <= 0) return 0f;

		Vector2I target = moved[^1].Hex;
		RemoveStackOccupant(occupiedShips, ship.HexCoords, ship);
		AddStackOccupant(occupiedShips, target, ship);
		if (!IsAlive(ship))
		{
			ship.HexCoords = target;
			return 0f;
		}

		float perStep = 0.2f + 0.35f / Math.Max(1, moved.Count);
		ship.AnimateMovePath(
			Map,
			moved.Select(step => step.Hex).ToList(),
			perStep,
			moved.Select(step => step.Heading).ToList());
		return perStep * moved.Count;
	}

	/// <summary>单纵阵按首舰轨迹推进：后船逐格消费首舰历史轨迹，到达每个转向格时立即转向。</summary>
	public float AnimateFormationChain(List<ShipComponent> chain, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		ShipComponent lead = chain[0];
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(lead.CurrentSpeed, phase, oddTurn);
		var plannedPath = MoveRulesEvaluator.BuildMovePath(lead.HexCoords, lead.Direction, requestedSteps);
		var moved = ResolveMovePath(lead, plannedPath, bus, occupiedShips);
		int steps = moved.Count;

		FormationTrail trail = GetOrBuildFormationTrail(lead, chain);
		int leadIndex = trail.Cells.Count - 1;
		// 先走再转：首舰本阶段先沿原航向移动，结束时转向；轨迹格立即记录转向后的航向。
		HexDirection leadAfterTurn = lead.PendingDirection ?? lead.Direction;
		for (int i = 0; i < trail.Cells.Count; i++)
		{
			if (trail.Cells[i] == lead.HexCoords)
			{
				trail.Headings[i] = leadAfterTurn;
			}
		}
		if (steps <= 0) return 0f;

		for (int i = 0; i < steps; i++)
		{
			trail.Cells.Add(moved[i].Hex);
			trail.Headings.Add(moved[i].Heading);
		}

		var leadPath = trail.Cells.GetRange(leadIndex + 1, steps);
		var leadHeadings = trail.Headings.GetRange(leadIndex + 1, steps);
		RemoveStackOccupant(occupiedShips, lead.HexCoords, lead);
		AddStackOccupant(occupiedShips, trail.Cells[^1], lead);
		float perStep = 0.2f + 0.35f / Math.Max(1, steps);
		if (IsAlive(lead))
		{
			lead.AnimateMovePath(Map, leadPath, perStep, leadHeadings);
		}
		else
		{
			lead.HexCoords = trail.Cells[^1];
		}

		for (int k = 1; k < chain.Count; k++)
		{
			ShipComponent follower = chain[k];
			if (!IsAlive(follower)) continue;
			int followerIndex = trail.Cells.LastIndexOf(follower.HexCoords);
			if (followerIndex < 0) continue;
			int followerSteps = Math.Min(steps, trail.Cells.Count - 1 - followerIndex);
			if (followerSteps <= 0) continue;
			var followerPath = trail.Cells.GetRange(followerIndex + 1, followerSteps);
			var headings = trail.Headings.GetRange(followerIndex + 1, followerSteps);
			RemoveStackOccupant(occupiedShips, follower.HexCoords, follower);
			AddStackOccupant(occupiedShips, followerPath[followerSteps - 1], follower);
			follower.AnimateMovePath(Map, followerPath, perStep, headings);
		}
		return 0.35f + steps * 0.2f;
	}

	/// <summary>移动阶段结束后执行待命转向：先沿原航向移动，再转向。</summary>
	public void ApplyPendingTurns(IEnumerable<ShipComponent> ships)
	{
		foreach (ShipComponent ship in ships)
		{
			if (IsAlive(ship) && ship.PendingDirection.HasValue)
			{
				HexDirection target = ship.PendingDirection.Value;
				ship.AnimateTurnTo(target);
				ship.PendingDirection = null;
			}
		}
	}

	/// <summary>按同格同阵营堆叠序号自动调整模型位置。</summary>
	public void RefreshStackOffsets(
		IEnumerable<ShipComponent> playerShips,
		IEnumerable<ShipComponent> enemyShips)
	{
		var groups = new Dictionary<Vector2I, List<ShipComponent>>();
		foreach (ShipComponent ship in playerShips.Concat(enemyShips))
		{
			if (IsAlive(ship))
			{
				AddStackOccupant(groups, ship.HexCoords, ship);
			}
		}

		foreach (var group in groups)
		{
			foreach (var sideGroup in group.Value.GroupBy(s => s.BattleSide))
			{
				var stacked = sideGroup.ToList();
				for (int i = 0; i < stacked.Count; i++)
				{
					ShipComponent ship = stacked[i];
					Vector3 hexCenter = Map.HexToWorld(ship.HexCoords.X, ship.HexCoords.Y);
					ship.ApplyStackOffset(i, stacked.Count, hexCenter, LateralAxisFor(ship));
				}
			}
		}
	}

	private Vector3 LateralAxisFor(ShipComponent ship)
	{
		Vector2I off = HexDirectionUtility.Offset(ship.Direction);
		Vector3 forward = Map.HexToWorld(off.X, off.Y)
			- Map.HexToWorld(0, 0);
		forward.Y = 0f;
		if (forward.LengthSquared() < 0.0001f) return Vector3.Right;
		forward = forward.Normalized();
		return new Vector3(forward.Z, 0f, -forward.X);
	}

	private FormationTrail GetOrBuildFormationTrail(ShipComponent lead, List<ShipComponent> chain)
	{
		if (Trails.TryGetValue(lead, out FormationTrail trail)
			&& trail.Cells.Count > 0
			&& trail.Cells[^1] == lead.HexCoords
			&& trail.Members.SequenceEqual(chain))
		{
			return trail;
		}

		trail = new FormationTrail();
		for (int i = chain.Count - 1; i >= 0; i--)
		{
			ShipComponent ship = chain[i];
			if (!IsAlive(ship)) continue;
			trail.Cells.Add(ship.HexCoords);
			trail.Headings.Add(ship.Direction);
		}
		trail.Members.AddRange(chain);
		Trails[lead] = trail;
		return trail;
	}

	private List<MoveRulesEvaluator.MovementStep> ResolveMovePath(ShipComponent ship,
		IReadOnlyList<MoveRulesEvaluator.MovementStep> path, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		var moved = new List<MoveRulesEvaluator.MovementStep>();
		int index = 0;
		for (; index < path.Count; index++)
		{
			Vector2I next = path[index].Hex;
			if (Data?.IsIsland(next) ?? false)
			{
				bus.EmitLog($"🪨 {ship.ShipName} 撞击岛屿，直接沉没！");
				ship.TakeDamage(ship.CurrentHp);
				RefreshCommandValues?.Invoke();
				return moved;
			}
			if (!CanStackEnter(ship, next, occupiedShips))
			{
				break;
			}
			moved.Add(path[index]);
		}

		if (index >= path.Count) return moved;
		Vector2I blockedHex = path[index].Hex;
		if (occupiedShips.TryGetValue(blockedHex, out var blockers) && blockers.Count > 0)
		{
			ShipComponent blocker = blockers[0];
			if (CollisionRulesEvaluator.IsCollision())
			{
				int hullSum = ship.MaxHp + blocker.MaxHp;
				var (rollA, dmgA) = CollisionRulesEvaluator.RollDamage(hullSum);
				var (rollB, dmgB) = CollisionRulesEvaluator.RollDamage(hullSum);
				bus.EmitLog($"💥 {ship.ShipName} 与 {blocker.ShipName} 发生冲撞！（{rollA}→{dmgA}，{rollB}→{dmgB}）");
				ship.TakeDamage(dmgA);
				blocker.TakeDamage(dmgB);
				RefreshCommandValues?.Invoke();
			}
			else
			{
				bus.EmitLog($"⚠️ {ship.ShipName} 前方有舰船但未发生冲撞，停在 {moved.Count} 格前");
			}
		}
		else
		{
			bus.EmitLog($"⚠️ {ship.ShipName} 前方受阻，仅推进 {moved.Count} 格");
		}
		return moved;
	}

	internal static void AddStackOccupant(
		Dictionary<Vector2I, List<ShipComponent>> occupants, Vector2I hex, ShipComponent ship)
	{
		if (!occupants.TryGetValue(hex, out var list))
		{
			list = new List<ShipComponent>();
			occupants[hex] = list;
		}
		if (!list.Contains(ship)) list.Add(ship);
	}

	internal static void RemoveStackOccupant(
		Dictionary<Vector2I, List<ShipComponent>> occupants, Vector2I hex, ShipComponent ship)
	{
		if (occupants.TryGetValue(hex, out var list))
		{
			list.Remove(ship);
			if (list.Count == 0) occupants.Remove(hex);
		}
	}

	/// <summary>同阵营单格最多 2 艘；敌舰占位或已满 2 艘时不可进入。</summary>
	internal static bool CanStackEnter(ShipComponent ship, Vector2I hex,
		Dictionary<Vector2I, List<ShipComponent>> occupants)
	{
		if (!occupants.TryGetValue(hex, out var list) || list.Count == 0) return true;
		if (hex == ship.HexCoords) return true;
		if (list.Any(s => s.BattleSide != ship.BattleSide)) return false;
		return list.Count < 2;
	}
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 机动规则求值器 —— 集中管理 A2 速力表、前方 3 格限制、转向消耗、单纵阵判定
/// </summary>
public static class MoveRulesEvaluator
{
	/// <summary>逐格移动路径的一步：到达格 + 到达后的航向。</summary>
	public sealed record MovementStep(Vector2I Hex, HexDirection Heading);

	// ═════════════════════════════════════════
	//  3.1  A2 速力→阶段位移映射（已在 SpeedTable 中）
	// ═════════════════════════════════════════
	public static int MovementForPhase(int speed, int phase, bool oddTurn)
		=> SpeedTable.MoveForPhase(speed, phase, oddTurn);

	/// <summary>
	/// 生成逐格移动路径。每步最多允许一次 60° 转向；turnDeltas[i] 表示
	/// 第 i 步出发前的转向（+1 右转 / -1 左转），缺省按当前航向直行。
	/// </summary>
	public static List<MovementStep> BuildMovePath(Vector2I start, HexDirection direction,
		int steps, IReadOnlyList<int> turnDeltas = null)
	{
		var path = new List<MovementStep>();
		HexDirection heading = direction;
		Vector2I cursor = start;
		for (int i = 0; i < steps; i++)
		{
			if (turnDeltas != null && i < turnDeltas.Count)
			{
				int delta = Math.Clamp(turnDeltas[i], -1, 1);
				if (delta > 0) heading = HexDirectionUtility.TurnRight(heading);
				else if (delta < 0) heading = HexDirectionUtility.TurnLeft(heading);
			}
			cursor += HexDirectionUtility.Offset(heading);
			path.Add(new MovementStep(cursor, heading));
		}
		return path;
	}

	/// <summary>按实际阻挡条件解析预览路径：遇到不可进入格即停，不跨格。</summary>
	public static List<MovementStep> ResolvePreviewPath(Vector2I start, HexDirection direction,
		int steps, System.Func<Vector2I, bool> isBlocked)
	{
		var result = new List<MovementStep>();
		foreach (MovementStep step in BuildMovePath(start, direction, steps))
		{
			if (isBlocked(step.Hex)) break;
			result.Add(step);
		}
		return result;
	}

	/// <summary>返回从 from 到 to 最接近的六向航向（用于射界/转向判定）。</summary>
	public static HexDirection DirectionTo(Vector2I from, Vector2I to)
	{
		HexDirection best = HexDirection.N;
		int bestDist = int.MaxValue;
		foreach (HexDirection dir in System.Enum.GetValues<HexDirection>())
		{
			Vector2I next = from + HexDirectionUtility.Offset(dir);
			int dist = BattleRulesEvaluator.GetHexDistance(next, to);
			if (dist < bestDist)
			{
				bestDist = dist;
				best = dir;
			}
		}
		return best;
	}

	/// <summary>
	/// 沿航向逐格推进，遇到 isBlocked 返回 true 的格子立即停下；
	/// 返回实际可走的步数（不超过 requestedSteps）。
	/// </summary>
	public static int AdvanceSteps(Vector2I start, HexDirection dir, int requestedSteps,
		System.Func<Vector2I, bool> isBlocked)
	{
		Vector2I off = HexDirectionUtility.Offset(dir);
		Vector2I cursor = start;
		for (int i = 0; i < requestedSteps; i++)
		{
			Vector2I next = cursor + off;
			if (isBlocked(next)) return i;
			cursor = next;
		}
		return requestedSteps;
	}

	// ═════════════════════════════════════════
	//  3.2  前方合法格子（正面 3 个方向）
	// ═════════════════════════════════════════
	public static Vector2I[] ForwardOffsets(HexDirection dir)
	{
		Vector2I f = HexDirectionUtility.Offset(dir);
		Vector2I fl = HexDirectionUtility.Offset(HexDirectionUtility.TurnLeft(dir));
		Vector2I fr = HexDirectionUtility.Offset(HexDirectionUtility.TurnRight(dir));
		return new[] { f, fl, fr };
	}

	/// <summary>
	/// 目标格是否在舰船前方 120° 扇面内
	/// </summary>
	public static bool IsInForwardArc(Vector2I origin, Vector2I target, HexDirection dir)
	{
		Vector2I delta = target - origin;
		foreach (var off in ForwardOffsets(dir))
			if (off == delta || (delta.X * off.Y == delta.Y * off.X && delta.LengthSquared() > 0))
				return true;
		return false;
	}

	// ═════════════════════════════════════════
	//  转向消耗：每次偏转 60°，扣移动力 1
	// ═════════════════════════════════════════
	public static int TurnCostToFace(HexDirection from, HexDirection to)
	{
		int diff = ((int)to - (int)from + 6) % 6;
		diff = diff > 3 ? 6 - diff : diff; // 取最短路径
		return diff * 1; // 每 60° 消耗 1 移动力
	}

	// ═════════════════════════════════════════
	//  3.3  单纵阵（Line Ahead）检测
	// ═════════════════════════════════════════
	public class FormationResult
	{
		public bool IsInFormation;
		public ShipComponent LeadShip;
		public List<ShipComponent> Followers = new();
		public List<ShipComponent> Ships = new();
	}

	/// <summary>
	/// 判定 unit 是否处于单纵阵中，若是则返回完整编队链、首舰与跟随者列表
	/// 条件：至少 2 艘同一方舰船，航速一致，朝向一致，首尾相邻排在一条纵线上
	/// </summary>
	public static FormationResult DetectLineAhead(
		ShipComponent unit,
		List<ShipComponent> allFriendly,
		HexDirection? directionOverride = null,
		int? speedOverride = null)
	{
		var result = new FormationResult();
		if (unit == null || allFriendly.Count < 2) return result;
		HexDirection unitDirection = directionOverride ?? unit.Direction;
		int unitSpeed = speedOverride ?? unit.CurrentSpeed;
		var forward = HexDirectionUtility.Offset(unitDirection);
		var backward = -forward;

		// 按格子收集同向同速的船，同格堆叠视为同一“列”。
		var cells = new Dictionary<Vector2I, List<ShipComponent>>();
		foreach (var s in allFriendly)
		{
			if (!GodotObject.IsInstanceValid(s) || s.CurrentHp <= 0
				|| s.Direction != unitDirection || s.CurrentSpeed != unitSpeed)
				continue;
			if (!cells.TryGetValue(s.HexCoords, out var list))
			{
				list = new List<ShipComponent>();
				cells[s.HexCoords] = list;
			}
			if (!list.Contains(s)) list.Add(s);
		}

		if (!cells.TryGetValue(unit.HexCoords, out var startColumn))
		{
			startColumn = new List<ShipComponent> { unit };
			cells[unit.HexCoords] = startColumn;
		}
		else if (!startColumn.Contains(unit))
		{
			startColumn.Add(unit);
		}

		var chain = new List<ShipComponent>();
		var visited = new HashSet<ShipComponent>();

		// 从紧邻的前格向队首推进，最后倒序放入链首。
		var aheadColumns = new List<List<ShipComponent>>();
		Vector2I cursor = unit.HexCoords + forward;
		while (cells.TryGetValue(cursor, out var column) && column.Count > 0)
		{
			aheadColumns.Add(column);
			cursor += forward;
		}
		for (int i = aheadColumns.Count - 1; i >= 0; i--)
			foreach (var s in aheadColumns[i])
				if (visited.Add(s))
					chain.Add(s);

		foreach (var s in startColumn)
			if (visited.Add(s))
				chain.Add(s);

		cursor = unit.HexCoords + backward;
		while (cells.TryGetValue(cursor, out var column) && column.Count > 0)
		{
			foreach (var s in column)
				if (visited.Add(s))
					chain.Add(s);
			cursor += backward;
		}

		if (chain.Count < 2) return result;
		result.IsInFormation = true;
		result.Ships = chain;
		result.LeadShip = chain[0];
		result.Followers = chain.Skip(1).ToList();
		return result;
	}

	/// <summary>按运行时编队标记返回成员；未建立标记时返回空列表。</summary>
	public static List<ShipComponent> RuntimeFormationMembers(
		ShipComponent lead, IEnumerable<ShipComponent> ships)
		=> ships
			.Where(s => GodotObject.IsInstanceValid(s) && ReferenceEquals(s.FormationLead, lead))
			.OrderBy(s => s.FormationIndex)
			.ToList();

	/// <summary>是否仍被运行时标记视为首舰（贪吃蛇跟随途中几何关系会暂时不同向）。</summary>
	public static bool IsRuntimeFormationLead(
		ShipComponent ship, IEnumerable<ShipComponent> ships)
	{
		if (ship?.FormationLead != ship) return false;
		var members = RuntimeFormationMembers(ship, ships);
		return members.Count >= 2 && ReferenceEquals(members[0], ship);
	}

	/// <summary>按当前几何关系重建全部单纵阵标记（首舰、链内序号），供列表与移动结算使用。</summary>
	public static void SyncFormationGroups(List<ShipComponent> ships)
	{
		if (ships == null) return;
		// 保存上一回合的编队标记：贪吃蛇跟随途中会出现暂时不同向/几何检测只能认出部分成员，
		// 必须先按旧整组保留，再做新的几何检测，避免把跟随中的尾巴拆成独立船。
		var previousGroups = ships
			.Where(s => s.FormationLead != null && GodotObject.IsInstanceValid(s))
			.GroupBy(s => s.FormationLead)
			.Select(g => new
			{
				Lead = g.Key,
				LeadWasSelf = ReferenceEquals(g.Key, g.Key.FormationLead),
				Members = g.OrderBy(s => s.FormationIndex).ToList()
			})
			.ToList();
		foreach (var ship in ships)
		{
			ship.FormationLead = null;
			ship.FormationIndex = -1;
		}
		var seen = new HashSet<ShipComponent>();

		foreach (var group in previousGroups)
		{
			if (!group.LeadWasSelf || !GodotObject.IsInstanceValid(group.Lead)
				|| group.Lead.CurrentHp <= 0)
				continue;
			var members = group.Members
				.Where(s => ships.Contains(s) && s.CurrentHp > 0)
				.OrderBy(s => s.FormationIndex)
				.ToList();
			if (members.Count < 2) continue;
			bool adjacent = true;
			for (int i = 1; i < members.Count; i++)
			{
				int step = BattleRulesEvaluator.GetHexDistance(
					members[i - 1].HexCoords, members[i].HexCoords);
				if (step != 0 && step != 1)
				{
					adjacent = false;
					break;
				}
			}
			if (!adjacent) continue;
			for (int i = 0; i < members.Count; i++)
			{
				members[i].FormationLead = group.Lead;
				members[i].FormationIndex = i;
				seen.Add(members[i]);
			}
		}

		// 剩余的船再做新的几何编队检测。
		foreach (var ship in ships)
		{
			if (seen.Contains(ship)) continue;
			var formation = DetectLineAhead(ship, ships);
			if (!formation.IsInFormation || !ReferenceEquals(formation.LeadShip, ship)
				|| formation.Ships.Any(seen.Contains))
				continue;
			for (int i = 0; i < formation.Ships.Count; i++)
			{
				formation.Ships[i].FormationLead = formation.LeadShip;
				formation.Ships[i].FormationIndex = i;
				seen.Add(formation.Ships[i]);
			}
		}
	}
}

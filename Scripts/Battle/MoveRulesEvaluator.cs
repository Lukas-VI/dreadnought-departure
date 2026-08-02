using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 机动规则求值器 —— 集中管理 A2 速力表、前方 3 格限制、转向消耗、单纵阵判定
/// </summary>
public static class MoveRulesEvaluator
{
	// ═════════════════════════════════════════
	//  3.1  A2 速力→阶段位移映射（已在 SpeedTable 中）
	// ═════════════════════════════════════════
	public static int MovementForPhase(int speed, int phase, bool oddTurn)
		=> SpeedTable.MoveForPhase(speed, phase, oddTurn);

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
		List<ShipComponent> allFriendly)
	{
		var result = new FormationResult();
		if (unit == null || allFriendly.Count < 2) return result;
		var forward = HexDirectionUtility.Offset(unit.Direction);
		var backward = -forward;
		var chain = new List<ShipComponent>();
		var visited = new HashSet<ShipComponent>();

		// 沿舰艏方向找前面的船，直到队首。
		ShipComponent current = unit;
		while (true)
		{
			visited.Add(current);
			chain.Insert(0, current);
			var ahead = allFriendly.FirstOrDefault(s => s != current && s.CurrentHp > 0
				&& s.Direction == unit.Direction && s.CurrentSpeed == unit.CurrentSpeed
				&& s.HexCoords == current.HexCoords + forward);
			if (ahead == null) break;
			current = ahead;
		}

		// 从 unit 沿船尾方向收集跟随者。
		current = unit;
		while (true)
		{
			var follower = allFriendly.FirstOrDefault(s => s != current && s.CurrentHp > 0
				&& s.Direction == unit.Direction && s.CurrentSpeed == unit.CurrentSpeed
				&& s.HexCoords == current.HexCoords + backward && !visited.Contains(s));
			if (follower == null) break;
			chain.Add(follower);
			visited.Add(follower);
			current = follower;
		}

		if (chain.Count < 2) return result;
		result.IsInFormation = true;
		result.Ships = chain;
		result.LeadShip = chain[0];
		result.Followers = chain.Skip(1).ToList();
		return result;
	}
}

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
	public static int MovementForPhase(int speed, int phase, bool isOddTurn)
		=> SpeedTable.MoveForPhase(speed, phase, isOddTurn);

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
	}

	/// <summary>
	/// 判定 unit 是否处于单纵阵中，若是则返回首舰与跟随者列表
	/// 条件：至少 2 艘同一方舰船，朝向一致，排在一条纵线上（相邻+同向）
	/// </summary>
	public static FormationResult DetectLineAhead(
		ShipComponent unit,
		List<ShipComponent> allFriendly)
	{
		var result = new FormationResult();
		if (allFriendly.Count < 2) return result;

		// 找所有朝向相同的友舰
		var sameDir = allFriendly
			.Where(s => s != unit && s.Direction == unit.Direction)
			.ToList();

		// 向前找首舰、向后找跟随者
		var forward = HexDirectionUtility.Offset(unit.Direction);
		var backward = -forward;

		ShipComponent current = unit;
		// 向前走：找首舰
		while (true)
		{
			Vector2I next = current.HexCoords + forward;
			var lead = sameDir.FirstOrDefault(s => s.HexCoords == next);
			if (lead == null) break;
			current = lead;
			sameDir.Remove(lead);
		}
		result.LeadShip = current;

		// 向后走：收集跟随者
		current = unit;
		while (true)
		{
			Vector2I next = current.HexCoords + backward;
			var follower = allFriendly
				.Where(s => s != current && s.Direction == unit.Direction)
				.FirstOrDefault(s => s.HexCoords == next);
			if (follower == null) break;
			result.Followers.Add(follower);
			current = follower;
		}

		result.IsInFormation = result.LeadShip != null &&
			(result.Followers.Count > 0 || result.LeadShip != unit);
		return result;
	}
}

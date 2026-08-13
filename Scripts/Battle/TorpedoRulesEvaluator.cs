using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 鱼雷简化规则：发射资格、直线移动、命中模式与损伤。
/// 纸质表 C1-C4 尚未数字化，当前按剧本鱼雷模式作为基础命中阈值占位。
/// </summary>
public static class TorpedoRulesEvaluator
{
	private static readonly Random _rng = new();

	public static bool CanLaunch(ShipComponent ship, int side)
	{
		if (ship == null || ship.Data == null) return false;
		if (ship.Data.TotalTorpedoTubes <= 0) return false;
		if (ship.DamageState is not (DamageState.Intact or DamageState.Light)) return false;
		if (ship.TorpedoFiredThisTurn) return false;
		return ship.TorpedoesAvailableOnSide(side) > 0;
	}

	/// <summary>鱼雷向顶点侧运动：主航向格 + 斜侧格两个候选。</summary>
	public static List<Vector2I> CandidateOffsets(HexDirection primary, int side)
	{
		HexDirection sideDir = side < 0
			? HexDirectionUtility.TurnLeft(primary)
			: HexDirectionUtility.TurnRight(primary);
		return new List<Vector2I>
		{
			HexDirectionUtility.Offset(primary),
			HexDirectionUtility.Offset(sideDir),
		};
	}

	public static Vector2I OffsetForBranch(HexDirection primary, int side, int branch)
	{
		var candidates = CandidateOffsets(primary, side);
		return candidates[branch == 0 ? 0 : 1];
	}

	/// <summary>从发射格出发，按扇面双候选格 BFS 出全部可达格与最小步数。</summary>
	public static Dictionary<Vector2I, int> ComputeReachable(
		Vector2I start,
		HexDirection direction,
		int range,
		Func<int, bool> sideAllowed)
	{
		var dist = new Dictionary<Vector2I, int>();
		var queue = new Queue<(Vector2I Cell, int Steps)>();
		dist[start] = 0;
		queue.Enqueue((start, 0));
		while (queue.Count > 0)
		{
			var (cell, steps) = queue.Dequeue();
			if (steps >= range) continue;
			foreach (int side in new[] { -1, 1 })
			{
				if (!sideAllowed(side)) continue;
				foreach (Vector2I offset in CandidateOffsets(direction, side))
				{
					Vector2I next = cell + offset;
					if (dist.ContainsKey(next)) continue;
					dist[next] = steps + 1;
					queue.Enqueue((next, steps + 1));
				}
			}
		}
		return dist;
	}

	public static List<Vector2I> FarthestReachable(
		Vector2I start,
		HexDirection direction,
		int range,
		Func<int, bool> sideAllowed)
		=> ComputeReachable(start, direction, range, sideAllowed)
			.Where(pair => pair.Value == range)
			.Select(pair => pair.Key)
			.ToList();

	/// <summary>按目的地相对舰艏选择发射舷侧；正前方默认右舷。</summary>
	public static int SideToward(Vector2I from, HexDirection shipDirection, Vector2I destination)
	{
		HexDirection best = MoveRulesEvaluator.DirectionTo(from, destination);
		int diff = ((int)best - (int)shipDirection + 6) % 6;
		return diff <= 3 ? 1 : -1;
	}

	public static int BranchToward(Vector2I from, HexDirection primary, int side,
		Vector2I destination)
	{
		var candidates = CandidateOffsets(primary, side);
		int d0 = BattleRulesEvaluator.GetHexDistance(from + candidates[0], destination);
		int d1 = BattleRulesEvaluator.GetHexDistance(from + candidates[1], destination);
		return d0 <= d1 ? 0 : 1;
	}

	/// <summary>贪心选择更接近目标的扇面分支；距离相同时保持当前分支。</summary>
	public static int ChooseBranch(Vector2I from, HexDirection primary, int side,
		Vector2I target, int currentBranch)
	{
		var candidates = CandidateOffsets(primary, side);
		int d0 = BattleRulesEvaluator.GetHexDistance(from + candidates[0], target);
		int d1 = BattleRulesEvaluator.GetHexDistance(from + candidates[1], target);
		if (d0 == d1) return currentBranch;
		return d0 < d1 ? 0 : 1;
	}

	/// <summary>占位 C3：基础命中模式随鱼雷航行距离衰减，低速目标更容易命中。</summary>
	public static int HitThreshold(int mode, int distanceHex, int targetSpeed)
	{
		int threshold = mode - distanceHex;
		if (targetSpeed is 1 or 2) threshold += 1;
		return Mathf.Clamp(threshold, 1, 10);
	}

	public static (bool hit, int damage, string detail) ResolveHit(
		TorpedoComponent torpedo, ShipComponent target)
	{
		int distance = Math.Max(1, torpedo.RangeSpent);
		int threshold = HitThreshold(torpedo.HitMode, distance, target.CurrentSpeed);
		int roll = _rng.Next(1, 11);
		if (roll > threshold)
		{
			string detail = $"{torpedo.TorpedoType} 鱼雷 → {target.ShipName}：" +
				$"命中检定 1D10={roll} > {threshold}（模式 {torpedo.HitMode}，航程 {distance}），未命中";
			return (false, 0, detail);
		}

		int countFactor = Math.Max(1, (int)Math.Ceiling(torpedo.Count / 4.0));
		int damage = Math.Max(1, torpedo.TorpedoDamage * countFactor);
		string hitDetail = $"{torpedo.TorpedoType} 鱼雷 → {target.ShipName}：" +
			$"命中检定 1D10={roll} ≤ {threshold}（模式 {torpedo.HitMode}，航程 {distance}），" +
			$"命中 {torpedo.Count} 发中的 {countFactor} 组，造成 {damage} 点损伤";
		return (true, damage, hitDetail);
	}
}

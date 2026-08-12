using Godot;
using System;

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

	public static HexDirection LaunchDirection(ShipComponent ship, int side)
		=> side < 0
			? HexDirectionUtility.TurnLeft(ship.Direction)
			: HexDirectionUtility.TurnRight(ship.Direction);

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

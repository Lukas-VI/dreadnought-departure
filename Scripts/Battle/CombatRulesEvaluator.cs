using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public static class CombatRulesEvaluator
{
	private static readonly Random _rng = new();

	// ── 距离 → 命中率（十面骰命中阈值）──
	public static int HitThreshold(int distanceHex)
	{
		return distanceHex switch
		{
			<= 2 => 8,   // 近距：1D10 ≤ 8 命中
			<= 5 => 6,
			<= 8 => 4,
			<= 12 => 2,
			_ => 1       // 极远：1D10 ≤ 1 命中
		};
	}

	// ── 距离 → 装甲段 ──
	public enum ArmorRange { Close, Medium, Far }
	public static ArmorRange GetArmorRange(int distanceHex)
		=> distanceHex <= 3 ? ArmorRange.Close : distanceHex <= 7 ? ArmorRange.Medium : ArmorRange.Far;

	// ── 口径 → 基础伤害 ──
	public static int BaseDamage(int caliberInches)
	{
		return caliberInches switch
		{
			>= 18 => 12, >= 16 => 10, >= 14 => 8,
			>= 12 => 6, >= 8 => 4, _ => 2
		};
	}

	// ── 完整命中判定流程 ──
	// 返回 (命中, 实际伤害)
	public static (bool hit, int damage) ResolveShot(ShipComponent attacker, ShipComponent defender, int distanceHex)
	{
		int threshold = HitThreshold(distanceHex);
		int roll = _rng.Next(1, 11); // 1D10
		if (roll > threshold) return (false, 0);

		int baseDmg = BaseDamage(attacker.Data?.GunCaliber ?? 14);
		int armor = GetArmorRange(distanceHex) switch
		{
			ArmorRange.Close => defender.Data?.ArmorClose ?? 8,
			ArmorRange.Medium => defender.Data?.ArmorMedium ?? 6,
			_ => defender.Data?.ArmorFar ?? 3
		};
		int final = Math.Max(1, baseDmg - armor + _rng.Next(-2, 3)); // 1D6-3 浮动
		return (true, final);
	}

	// 炮击——伤害进悬空池，阶段末统一结算
	public static void Fire(ShipComponent attacker, ShipComponent defender, int distanceHex)
	{
		var (hit, dmg) = ResolveShot(attacker, defender, distanceHex);
		if (hit)
		{
			defender.PendingDamage += dmg;
			GD.Print($"{attacker.ShipName} 命中 {defender.ShipName}！({dmg} 点悬空)");
		}
	}
}

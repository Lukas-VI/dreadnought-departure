using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 兵棋规则引擎——命中率、装甲分段、基础伤害、完整射击流程。
/// 纯静态方法，无副作用；修改 ShipComponent 的 PendingDamage 由调用方负责。
/// </summary>
public static class CombatRulesEvaluator
{
	private static readonly Random _rng = new();

	/// <summary>一次射击的完整判定记录，供回合结算时打印到 InfoLabel。</summary>
	public struct ShotCheck
	{
		public bool Hit;
		public int HitThreshold;
		public int HitRoll;
		public int Damage;
		public string Detail;
	}

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

	/// <summary>大破/沉没不能射击，完好/小破/中破分别对应火力系数 3/2/1。</summary>
	public static bool CanFire(ShipComponent attacker)
		=> attacker.DamageState is DamageState.Intact or DamageState.Light or DamageState.Moderate;

	/// <summary>目标是否位于主炮或副炮的非零火力射界内；未配置 ShipData 时默认全域可射。</summary>
	public static bool CanFireInArc(ShipComponent attacker, ShipComponent defender)
	{
		if (!CanFire(attacker)) return false;
		if (attacker.Data == null) return true;
		HexDirection targetDir = MoveRulesEvaluator.DirectionTo(attacker.HexCoords, defender.HexCoords);
		return attacker.Data.Firepower.ForArc(attacker.Direction, targetDir) > 0
			|| attacker.Data.SecondaryFirepower.ForArc(attacker.Direction, targetDir) > 0;
	}

	private static int ArcPower(ShipComponent attacker, HexDirection targetDir, bool secondary)
	{
		if (attacker.Data == null) return 6;
		Firepower fp = secondary ? attacker.Data.SecondaryFirepower : attacker.Data.Firepower;
		return fp.ForArc(attacker.Direction, targetDir);
	}

	/// <summary>副炮基础火力：优先用配表值，未配置时按主炮伤害与口径比例折算。</summary>
	private static int WeaponBasePower(ShipComponent attacker, bool secondary)
	{
		if (!secondary || attacker.Data == null) return attacker.AttackPower;
		if (attacker.Data.SecondaryAttackPower > 0) return attacker.Data.SecondaryAttackPower;
		int mainDamage = Math.Max(1, BaseDamage(attacker.Data.GunCaliber));
		int secondaryDamage = Math.Max(1, BaseDamage(attacker.Data.SecondaryGunCaliber));
		return Math.Max(1, attacker.AttackPower * secondaryDamage / mainDamage);
	}

	// ── 口径 → 基础伤害 ──
	public static int BaseDamage(int caliberInches)
	{
		return caliberInches switch
		{
			>= 18 => 12, >= 16 => 10, >= 14 => 8,
			>= 12 => 6, >= 8 => 4, _ => 2
		};
	}

	// ── 完整命中判定流程：命中骰、距离衰减、装甲抵扣、伤害浮动 ──
	public static ShotCheck ResolveShotCheck(ShipComponent attacker, ShipComponent defender, int distanceHex,
		bool radarUsed = false, bool secondary = false)
	{
		string weapon = secondary ? "副炮" : "主炮";
		int threshold = HitThreshold(distanceHex);
		if (radarUsed)
			threshold += RadarRulesEvaluator.GetHitModifier(attacker.Data?.RadarType);
		threshold = Mathf.Clamp(threshold, 1, 10);
		int roll = _rng.Next(1, 11); // 1D10
		int stateCoeff = attacker.DamageState switch
		{
			DamageState.Intact => 3,
			DamageState.Light => 2,
			DamageState.Moderate => 1,
			_ => 0
		};
		if (stateCoeff <= 0)
		{
			return new ShotCheck
			{
				Hit = false,
				HitThreshold = threshold,
				HitRoll = roll,
				Damage = 0,
				Detail = $"{attacker.ShipName} 大破/沉没，无法射击"
			};
		}
		HexDirection targetDir = MoveRulesEvaluator.DirectionTo(attacker.HexCoords, defender.HexCoords);
		int arcPower = ArcPower(attacker, targetDir, secondary);
		if (arcPower <= 0)
		{
			return new ShotCheck
			{
				Hit = false,
				HitThreshold = threshold,
				HitRoll = roll,
				Damage = 0,
				Detail = $"{attacker.ShipName} {weapon} → {defender.ShipName} 目标不在射界内，无法开火"
			};
		}

		if (roll > threshold)
		{
			string miss = $"{attacker.ShipName} {weapon} → {defender.ShipName} 命中检定：1D10={roll} > {threshold}，未命中";
			return new ShotCheck { Hit = false, HitThreshold = threshold, HitRoll = roll, Damage = 0, Detail = miss };
		}

		// 基础伤害来自 AttackPower（副炮按口径比例折算），距离越远衰减越多，装甲按距离分段抵扣。
		int baseDmg = Math.Max(1, WeaponBasePower(attacker, secondary)) * arcPower * stateCoeff / 18;
		int distanceFalloff = distanceHex switch
		{
			<= 3 => 0,
			<= 7 => baseDmg / 4,
			_ => baseDmg / 2
		};
		int armor = GetArmorRange(distanceHex) switch
		{
			ArmorRange.Close => defender.Data?.ArmorClose ?? 8,
			ArmorRange.Medium => defender.Data?.ArmorMedium ?? 6,
			_ => defender.Data?.ArmorFar ?? 3
		};
		int variance = _rng.Next(-3, 4);
		int final = Math.Max(1, baseDmg - distanceFalloff - armor + variance);
		string detail = $"{attacker.ShipName} {weapon} → {defender.ShipName} 命中检定：1D10={roll} ≤ {threshold}，命中；" +
			$"伤害 {baseDmg} - {distanceFalloff}(距离) - {armor}(装甲) + {variance} = {final}";
		return new ShotCheck { Hit = true, HitThreshold = threshold, HitRoll = roll, Damage = final, Detail = detail };
	}

	// 兼容旧调用：只返回 (命中, 实际伤害)
	public static (bool hit, int damage) ResolveShot(ShipComponent attacker, ShipComponent defender, int distanceHex)
	{
		var check = ResolveShotCheck(attacker, defender, distanceHex);
		return (check.Hit, check.Damage);
	}

	// 炮击——主炮与副炮（如配置且目标在副炮射界内）各结算一发，返回合并命中文本供 UI 显示
	public static (bool hit, int damage, string desc) FireEx(ShipComponent attacker, ShipComponent defender, int distanceHex,
		bool radarUsed = false)
	{
		HexDirection targetDir = MoveRulesEvaluator.DirectionTo(attacker.HexCoords, defender.HexCoords);
		bool mainArc = attacker.Data == null
			|| attacker.Data.Firepower.ForArc(attacker.Direction, targetDir) > 0;
		bool secondaryArc = attacker.Data != null
			&& attacker.Data.SecondaryFirepower.ForArc(attacker.Direction, targetDir) > 0;
		if (!mainArc && !secondaryArc)
		{
			defender.PendingShotChecks.Add(
				$"{attacker.ShipName} → {defender.ShipName} 主炮/副炮均不在射界内，无法开火");
			return (false, 0, $"{attacker.ShipName} 目标不在任何炮火射界内！");
		}

		int totalDamage = 0;
		bool mainHit = false, secondaryHit = false;
		if (mainArc)
		{
			var check = ResolveShotCheck(attacker, defender, distanceHex, radarUsed, false);
			defender.PendingShotChecks.Add(check.Detail);
			if (check.Hit)
			{
				mainHit = true;
				defender.PendingDamage += check.Damage;
				totalDamage += check.Damage;
			}
		}
		if (secondaryArc)
		{
			var check = ResolveShotCheck(attacker, defender, distanceHex, radarUsed, true);
			defender.PendingShotChecks.Add(check.Detail);
			if (check.Hit)
			{
				secondaryHit = true;
				defender.PendingDamage += check.Damage;
				totalDamage += check.Damage;
			}
		}

		string desc;
		if (mainHit && secondaryHit)
			desc = $"{attacker.ShipName} 主炮与副炮命中 {defender.ShipName}，共造成 {totalDamage} 点悬空损伤";
		else if (mainHit)
			desc = $"{attacker.ShipName} 主炮命中 {defender.ShipName}，造成 {totalDamage} 点悬空损伤";
		else if (secondaryHit)
			desc = $"{attacker.ShipName} 副炮命中 {defender.ShipName}，造成 {totalDamage} 点悬空损伤";
		else
			desc = $"{attacker.ShipName} 跨射散布，炮弹落水！";
		return (totalDamage > 0, totalDamage, desc);
	}

	// 兼容旧调用
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

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>舰船尺寸，用于指挥值减少与射击 CP 花费。</summary>
public enum ShipTier
{
	Large,
	Medium,
	Small
}

/// <summary>
/// 指挥系统规则求值器。
/// 按规则书 4.3 计算舰船损伤造成的指挥值减少，按表 A4 提供射击 CP 花费。
/// </summary>
public static class CommandRulesEvaluator
{
	public static ShipTier TierOf(ShipComponent ship)
	{
		string cls = ship?.ShipClass ?? ship?.Data?.ShipClass ?? "";
		if (cls.StartsWith("BB", StringComparison.OrdinalIgnoreCase)
			|| cls.StartsWith("BC", StringComparison.OrdinalIgnoreCase))
			return ShipTier.Large;
		if (cls.StartsWith("CA", StringComparison.OrdinalIgnoreCase)
			|| cls.StartsWith("CL", StringComparison.OrdinalIgnoreCase)
			|| cls.StartsWith("CB", StringComparison.OrdinalIgnoreCase))
			return ShipTier.Medium;
		return ShipTier.Small;
	}

	/// <summary>表 A4：大型舰射击 2 CP，中型/小型舰射击 1 CP。</summary>
	public static int FireCPCost(ShipComponent ship) => TierOf(ship) == ShipTier.Large ? 2 : 1;

	/// <summary>
	/// 按规则书 4.3 计算当前指挥值：
	/// 大型舰小破 -1，中破/大破/沉没 -2（不叠加）；
	/// 中型舰中破/大破/沉没 -1；小型舰同类满 3 艘 -1；最低 1。
	/// </summary>
	public static int CommandValue(IEnumerable<ShipComponent> ships, int baseCommand)
	{
		int largeLight = 0;
		int largeSevere = 0;
		int mediumSevere = 0;
		int smallSevere = 0;
		foreach (var ship in ships ?? Enumerable.Empty<ShipComponent>())
		{
			if (!GodotObject.IsInstanceValid(ship) || ship.DamageState == DamageState.Intact) continue;
			switch (TierOf(ship))
			{
				case ShipTier.Large:
					if (ship.DamageState == DamageState.Light) largeLight++;
					else largeSevere++;
					break;
				case ShipTier.Medium:
					if (ship.DamageState != DamageState.Light) mediumSevere++;
					break;
				default:
					if (ship.DamageState != DamageState.Light) smallSevere++;
					break;
			}
		}

		int reduction = largeSevere * 2 + largeLight + mediumSevere
			+ (smallSevere >= 3 ? 1 : 0);
		return Math.Max(1, Math.Max(1, baseCommand) - reduction);
	}
}

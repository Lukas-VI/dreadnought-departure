using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// PV 评分求值器。
/// 简化计分：沉没记全额 PV，大破记一半，中破记四分之一，小破与完好不计分。
/// 具体剧本可能另有倍率与阈值，后续可在关卡 JSON 中扩展。
/// </summary>
public static class VictoryRulesEvaluator
{
	public static int PVScoreForState(int pv, DamageState state) => state switch
	{
		DamageState.Sunk => pv,
		DamageState.Heavy => pv / 2,
		DamageState.Moderate => pv / 4,
		_ => 0
	};

	public static int FleetScore(IEnumerable<ShipComponent> ships)
	{
		int score = 0;
		foreach (var ship in ships ?? Enumerable.Empty<ShipComponent>())
			if (GodotObject.IsInstanceValid(ship))
				score += PVScoreForState(ship.PV, ship.DamageState);
		return score;
	}
}

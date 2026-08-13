using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>结算服务：收集检定日志、整理命中演绎、落实悬空损伤并清空回合缓冲。</summary>
public static class CombatSettlementService
{
	public static List<string> CollectChecks(IEnumerable<ShipComponent> ships)
	{
		var checks = new List<string>();
		foreach (ShipComponent ship in ships)
		{
			if (GodotObject.IsInstanceValid(ship) && ship.PendingShotChecks.Count > 0)
			{
				checks.AddRange(ship.PendingShotChecks);
			}
		}
		return checks;
	}

	public static List<HitFeedbackEvent> CollectHitEvents(IEnumerable<ShipComponent> ships)
		=> ships
			.SelectMany(ship => ship.PendingHitEvents)
			.Where(ev => ev != null
				&& GodotObject.IsInstanceValid(ev.Attacker)
				&& GodotObject.IsInstanceValid(ev.Target))
			.ToList();

	public static List<HitFeedbackEvent> BuildReplayOrder(
		IEnumerable<ShipComponent> ships,
		string initiativeOwner)
	{
		var hitEvents = CollectHitEvents(ships);
		var playerAttacks = hitEvents
			.Where(ev => ev.Attacker.BattleSide == GenerationSide.Player)
			.ToList();
		var enemyAttacks = hitEvents
			.Where(ev => ev.Attacker.BattleSide == GenerationSide.Enemy)
			.ToList();
		bool playerFirst = initiativeOwner != "enemy";
		return playerFirst
			? playerAttacks.Concat(enemyAttacks).ToList()
			: enemyAttacks.Concat(playerAttacks).ToList();
	}

	/// <summary>落实 PendingDamage，并对首次进入沉没的船调用 onSunk 回调。</summary>
	public static void ApplyPendingDamage(
		IEnumerable<ShipComponent> ships,
		HashSet<ShipComponent> countedSunk,
		Action<ShipComponent> onSunk)
	{
		foreach (ShipComponent ship in ships)
		{
			if (GodotObject.IsInstanceValid(ship) && ship.PendingDamage > 0)
			{
				ship.ApplyPendingDamage();
			}
		}
		foreach (ShipComponent ship in ships)
		{
			if (GodotObject.IsInstanceValid(ship)
				&& ship.DamageState == DamageState.Sunk
				&& countedSunk.Add(ship))
			{
				onSunk?.Invoke(ship);
			}
		}
	}

	public static void ClearPending(IEnumerable<ShipComponent> ships)
	{
		foreach (ShipComponent ship in ships)
		{
			if (!GodotObject.IsInstanceValid(ship)) continue;
			ship.PendingShotChecks.Clear();
			ship.PendingHitEvents.Clear();
		}
	}
}

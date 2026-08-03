using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>单机与 PvP 共用的“船 → 指令意图”转换结果。</summary>
public sealed record ShipCommandIntent(string ServerShipId, string Action, object Detail);

/// <summary>
/// 从 ShipComponent 的待命字段（PendingSpeed / PendingDirection / PendingAttackTarget）
/// 构建服务端可识别的逐船指令。单机结算直接读这些字段，PvP 提交也走同一份转换。
/// </summary>
public static class CommandIntentBuilder
{
	public static List<ShipCommandIntent> Build(
		List<ShipComponent> ships,
		BattlePhase phase)
	{
		var list = new List<ShipCommandIntent>();
		foreach (ShipComponent ship in ships)
		{
			if (ship == null || !GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0)
			{
				continue;
			}

			string serverId = ship.GetMeta("serverShipId", "").AsString();
			if (string.IsNullOrEmpty(serverId))
			{
				continue;
			}

			string action = "wait";
			object detail = null;
			if (phase == BattlePhase.SpeedAdjust)
			{
				if (ship.PendingSpeed >= 0 && ship.PendingSpeed != ship.CurrentSpeed)
				{
					action = ship.PendingSpeed > ship.CurrentSpeed ? "accelerate" : "decelerate";
				}
			}
			else if (phase is BattlePhase.MovePhase1 or BattlePhase.MovePhase2
				or BattlePhase.MovePhase3)
			{
				if (ship.PendingDirection.HasValue)
				{
					int delta = ((int)ship.PendingDirection.Value - (int)ship.Direction + 6) % 6;
					if (delta == 1)
					{
						action = "turn_right";
					}
					else if (delta == 5)
					{
						action = "turn_left";
					}
				}
			}
			else if (phase == BattlePhase.Gunfire &&
				ship.PendingAttackTarget != null &&
				GodotObject.IsInstanceValid(ship.PendingAttackTarget))
			{
				string targetId = ship.PendingAttackTarget
					.GetMeta("serverShipId", "")
					.AsString();
				if (!string.IsNullOrEmpty(targetId))
				{
					action = "fire";
					detail = new { targetShipId = targetId };
				}
			}

			list.Add(new ShipCommandIntent(serverId, action, detail));
		}
		return list;
	}
}

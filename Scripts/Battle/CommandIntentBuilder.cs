using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>单机与 PvP 共用的“船 → 指令意图”转换结果。</summary>
public sealed record ShipCommandIntent(
	ShipComponent Ship,
	string ServerShipId,
	string Action,
	int? TargetSpeed,
	HexDirection? TargetDirection,
	ShipComponent Target,
	int TorpedoSide = 0)
{
	/// <summary>转成服务端 battle.command 的 wire 对象；单机结算直接读字段。</summary>
	public object ToWire()
	{
		object detail = null;
		if (Action == "fire")
		{
			string targetId = Target != null
				? Target.GetMeta("serverShipId", "").AsString()
				: "";
			if (!string.IsNullOrEmpty(targetId))
			{
				detail = new
				{
					targetShipId = targetId,
					radarUsed = Ship.PendingRadarUsed,
				};
			}
		}
		else if (Action == "torpedo")
		{
			detail = new
			{
				side = TorpedoSide,
				count = Ship.TorpedoesAvailableOnSide(TorpedoSide),
			};
		}
		return new { id = ServerShipId, action = Action, detail };
	}
}

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

			string action = "wait";
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
				action = "fire";
			}
			else if (phase == BattlePhase.Torpedo && ship.PendingTorpedoSide != 0)
			{
				action = "torpedo";
			}

			list.Add(new ShipCommandIntent(
				ship,
				serverId,
				action,
				ship.PendingSpeed >= 0 ? ship.PendingSpeed : null,
				ship.PendingDirection,
				ship.PendingAttackTarget,
				ship.PendingTorpedoSide));
		}
		return list;
	}
}

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// AI 控制器——实现 IUnitController 接口。
/// 在阶段管线内与玩家同阶段交替行动：速度阶段调整航速，移动阶段转向，
/// 炮击阶段对射程内敌舰开火；机动位移仍由 GameplayDirector 在阶段结束时统一执行。
/// </summary>
public partial class AIController : Node, IUnitController
{
	public async void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
		MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
		BattlePhase phase, Action onComplete)
	{
		var aliveEnemies = enemyUnits
			.Where(e => GodotObject.IsInstanceValid(e) && e.CurrentHp > 0)
			.ToList();
		var data = GetNodeOrNull<LevelDataManager>("../LevelDataManager");
		var director = GetNodeOrNull<GameplayDirector>("../GameplayDirector");

		foreach (var ship in myUnits)
		{
			if (!GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0) continue;
			if (phase == BattlePhase.Torpedo)
			{
				if (ship.Data == null || ship.Data.TotalTorpedoTubes <= 0) continue;
				var torpedoTarget = aliveEnemies
					.Where(e => VisionRulesEvaluator.CanEngage(ship, e, data,
						ship.Data != null
						&& !string.IsNullOrEmpty(ship.Data.RadarType)
						&& ship.DamageState is DamageState.Intact or DamageState.Light))
					.OrderBy(e => BattleRulesEvaluator.GetHexDistance(ship.HexCoords, e.HexCoords))
					.FirstOrDefault();
				if (torpedoTarget == null) continue;
				int side = ChooseTorpedoSide(ship, torpedoTarget);
				if (director != null && director.LaunchTorpedo(ship, side, true))
				{
					await ToSignal(GetTree().CreateTimer(0.35f), "timeout");
				}
				continue;
			}
			if (phase == BattlePhase.Gunfire)
			{
				// AI 在炮击阶段自动启用雷达技能（玩家侧通过技能按钮显式开启）。
				ship.PendingRadarActive = ship.Data != null
					&& !string.IsNullOrEmpty(ship.Data.RadarType)
					&& ship.DamageState is DamageState.Intact or DamageState.Light;
			}

			var target = aliveEnemies
				.Where(e => VisionRulesEvaluator.CanEngage(ship, e, data, ship.PendingRadarActive))
				.OrderBy(e => BattleRulesEvaluator.GetHexDistance(ship.HexCoords, e.HexCoords))
				.FirstOrDefault();
			if (target == null) break;

			int dist = BattleRulesEvaluator.GetHexDistance(ship.HexCoords, target.HexCoords);
			if (map != null)
				EventBus.Instance?.EmitSignal("CameraTopDownRequested",
					map.HexToWorld(ship.HexCoords.X, ship.HexCoords.Y));
			await ToSignal(GetTree().CreateTimer(0.45f), "timeout");

			if (phase == BattlePhase.Gunfire && dist <= ship.AttackRange
				&& ship.MainAmmo > 0 && CombatRulesEvaluator.CanFireInArc(ship, target))
			{
				int cost = CommandRulesEvaluator.FireCPCost(ship);
				if (director == null || director.TryConsumeEnemyCP(cost))
				{
					ship.MainAmmo--;
					bool radarOnly = VisionRulesEvaluator.IsRadarOnly(ship, target, data, ship.PendingRadarActive);
					var (_, _, desc) = CombatRulesEvaluator.FireEx(ship, target, dist, radarOnly);
					Log(hud, desc);
					continue;
				}
			}

			if (phase == BattlePhase.SpeedAdjust)
				AdjustSpeedForTarget(ship, target, dist, director);

			if (phase is BattlePhase.SpeedAdjust or BattlePhase.MovePhase1
				or BattlePhase.MovePhase2 or BattlePhase.MovePhase3 or BattlePhase.Gunfire)
				TurnTowardTarget(ship, target, director);
		}

		onComplete?.Invoke();
	}

	private void AdjustSpeedForTarget(ShipComponent ship, ShipComponent target, int distance,
		GameplayDirector director)
	{
		int max = ship.MaxSpeedForCurrentState;
		int wish = distance > ship.AttackRange
			? ship.CurrentSpeed + 1
			: Math.Max(0, ship.CurrentSpeed - 1);
		if (!SpeedTable.CanAdjustSpeed(ship.CurrentSpeed, wish, max) || wish == ship.CurrentSpeed) return;
		if (director != null && !director.TryConsumeEnemyCP(1)) return;

		int old = ship.CurrentSpeed;
		ship.CurrentSpeed = wish;
		Log(null, $"{ship.ShipName} 航速 {old} → {wish}");
	}

	private static int ChooseTorpedoSide(ShipComponent ship, ShipComponent target)
	{
		HexDirection best = MoveRulesEvaluator.DirectionTo(ship.HexCoords, target.HexCoords);
		int diff = ((int)best - (int)ship.Direction + 6) % 6;
		int side = diff <= 3 ? 1 : -1;
		if (ship.TorpedoesAvailableOnSide(side) <= 0)
		{
			side = -side;
		}
		return side;
	}

	private void TurnTowardTarget(ShipComponent ship, ShipComponent target, GameplayDirector director)
	{
		if (MoveRulesEvaluator.IsInForwardArc(ship.HexCoords, target.HexCoords, ship.Direction)) return;

		HexDirection best = MoveRulesEvaluator.DirectionTo(ship.HexCoords, target.HexCoords);
		int diff = ((int)best - (int)ship.Direction + 6) % 6;
		if (diff == 0) return;
		if (director != null && !director.TryConsumeEnemyCP(1)) return;

		HexDirection next = diff <= 3
			? HexDirectionUtility.TurnRight(ship.Direction)
			: HexDirectionUtility.TurnLeft(ship.Direction);
		ship.AnimateTurnTo(next);
		Log(null, $"{ship.ShipName} 转向 → {ship.Direction}");
	}

	private static void Log(BattleHudBroker hud, string message)
	{
		hud?.DisplayConsoleLog(message);
		EventBus.Instance?.EmitLog(message);
	}
}

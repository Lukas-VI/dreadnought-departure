using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector
{
	private async void DoEndTurnSettlement()
	{
		if (_settling) return;
		_settling = true;
		var bus = GetNode<EventBus>("EventBus");
		try
		{
			bus.EmitSignal("LogMessage", "回合结算：判定检定……");
			var allShips = _playerShips.Concat(_enemyShips).ToList();
			var checks = CombatSettlementService.CollectChecks(allShips);
			if (checks.Count > 0)
			{
				bus.EmitSignal("LogMessage", string.Join("\n", checks));
			}

			var hitEvents = CombatSettlementService.CollectHitEvents(allShips);
			var playerAttacks = hitEvents
				.Where(ev => ev.Attacker.BattleSide == GenerationSide.Player)
				.ToList();
			var enemyAttacks = hitEvents
				.Where(ev => ev.Attacker.BattleSide == GenerationSide.Enemy)
				.ToList();
			GD.Print($"结算演绎：我方 {playerAttacks.Count} 条 / 敌方 {enemyAttacks.Count} 条");
			var replayOrder = CombatSettlementService.BuildReplayOrder(
				allShips, _dataManager?.InitiativeOwner ?? "player");

			CombatSettlementService.ApplyPendingDamage(allShips, _countedSunk, ship =>
			{
				if (ship.BattleSide == GenerationSide.Player)
				{
					_playerSunkCount++;
				}
				else
				{
					_enemySunkCount++;
				}
			});
			_moveSettlement.RefreshStackOffsets(_playerShips, _enemyShips);
			RefreshCommandValues();

			foreach (var ev in replayOrder)
			{
				bus.EmitSignal("CameraTopDownRequested", ev.Attacker.GlobalPosition);
				await ToSignal(GetTree().CreateTimer(0.45f), "timeout");
				bus.EmitSignal("CameraTopDownRequested", ev.Target.GlobalPosition);
				await ToSignal(GetTree().CreateTimer(0.45f), "timeout");
				_feedback.PlayFeedback(ev.Target, ev.Hit, ev.Damage);
				await ToSignal(GetTree().CreateTimer(0.65f), "timeout");
			}

			CombatSettlementService.ClearPending(allShips);

			if (!CheckBattleEnd())
			{
				await ToSignal(GetTree(), "process_frame");
			}
		}
		finally
		{
			if (_battleEnded)
			{
				_settling = false;
			}
			else
			{
				CallDeferred(nameof(ContinueAfterSettlement));
			}
		}
	}

	private void ContinueAfterSettlement()
	{
		_settling = false;
		AdvancePhase();
	}

	private bool CheckBattleEnd()
	{
		VictoryRulesEvaluator.VictoryResult customResult = EvaluateVictoryConditions();
		bool customConfigured = VictoryRulesEvaluator.IsConfigured(_dataManager?.VictoryJson);
		VictoryJudge.Verdict verdict = VictoryJudge.Judge(
			customResult,
			customConfigured,
			_playerShips.Count(IsShipAlive),
			_enemyShips.Count(IsShipAlive),
			_turnNumber,
			_dataManager?.MaxTurns ?? 18,
			PlayerScore,
			EnemyScore,
			_currentPhase == BattlePhase.EndTurn);
		if (verdict.Outcome == VictoryRulesEvaluator.VictoryResult.None)
		{
			return false;
		}

		_battleEnded = true;
		var bus = GetNode<EventBus>("EventBus");
		bus.EmitLog($"🏁 {verdict.Result}：{verdict.Detail}");
		bus.EmitSignal("BattleEnded", verdict.Result, verdict.Detail);
		if (verdict.Outcome == VictoryRulesEvaluator.VictoryResult.PlayerWin)
		{
			NotifyLevelComplete();
		}
		return true;
	}

	private VictoryRulesEvaluator.VictoryResult EvaluateVictoryConditions()
	{
		if (string.IsNullOrEmpty(_dataManager?.VictoryJson))
		{
			return VictoryRulesEvaluator.VictoryResult.None;
		}
		var checkpoints = new HashSet<string>();
		if (StoryDirector.Instance != null)
		{
			foreach (string key in StoryDirector.Instance.GetTrueFlags())
			{
				checkpoints.Add(key);
			}
		}
		var snapshot = new VictoryRulesEvaluator.VictorySnapshot
		{
			Turn = _turnNumber,
			MaxTurns = _dataManager?.MaxTurns ?? 18,
			PlayerAlive = _playerShips.Count(IsShipAlive),
			EnemyAlive = _enemyShips.Count(IsShipAlive),
			PlayerDestroyed = _playerSunkCount,
			EnemyDestroyed = _enemySunkCount,
			PlayerReached = new HashSet<Vector2I>(_playerReachedHexes),
			EnemyReached = new HashSet<Vector2I>(_enemyReachedHexes),
			CompletedCheckpoints = checkpoints,
			ActionCounts = new Dictionary<string, int>(_playerActionCounts),
		};
		return VictoryRulesEvaluator.Evaluate(_dataManager.VictoryJson, snapshot);
	}

	private void NotifyLevelComplete()
	{
		StoryDirector.Instance?.Trigger("level_complete", _dataManager?.CurrentMapName ?? "");
	}

	private void ApplyDeferredSpeedCaps()
		=> _economy.ApplyDeferredSpeedCaps(
			_playerShips.Concat(_enemyShips),
			message => GetNode<EventBus>("EventBus").EmitSignal("LogMessage", message));
}

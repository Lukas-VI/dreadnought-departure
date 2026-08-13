using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector
{
	private async void StartBattle()
	{
		await ToSignal(GetTree(), "process_frame");
		var all = new List<ShipComponent>();
		foreach (Node n in GetTree().GetNodesInGroup("Ships"))
		{
			if (n is ShipComponent ship) all.Add(ship);
		}
		_playerShips = all.Where(s => s.BattleSide == GenerationSide.Player).ToList();
		_enemyShips = all.Where(s => s.BattleSide == GenerationSide.Enemy).ToList();

		_turnNumber = 1;
		PlayerCommandValue = _dataManager.PlayerCommand;
		EnemyCommandValue = _dataManager.EnemyCommand;
		MaxCP = Math.Max(1, PlayerCommandValue * 2);
		CurrentCP = Math.Min(_dataManager.PlayerInitialCP, MaxCP);
		EnemyMaxCP = Math.Max(1, EnemyCommandValue * 2);
		EnemyCurrentCP = Math.Min(_dataManager.EnemyInitialCP, EnemyMaxCP);
		RefreshCommandValues();
		_currentPhase = BattlePhase.SpeedAdjust;
		EmitPhaseChanged();
		EmitCommandStateUpdated();
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
			$"—— 第 {_turnNumber} 回合 —— {BattlePhaseMachine.PhaseLabels[(int)_currentPhase]}");
		StoryDirector.Instance?.SetMapName(_dataManager.CurrentMapName);
		GetNode<EventBus>("EventBus").EmitSignal("BattleStarted");

		if (CheckBattleEnd()) return;
		RefreshDirectionOverlays();
		BeginPlayerPhase();
	}

	private void OnPlayerSideFinished()
	{
		_playerFinishedThisPhase = true;
		CancelPhaseTimer();
		if (_remotePvp)
		{
			_remotePhaseActive = false;
			SendRemoteCommands();
			return;
		}
		if (_battleEnded || _enemyActing) return;
		RunEnemyTurn();
	}

	private void BeginPlayerPhase()
	{
		_playerFinishedThisPhase = false;
		_enemyTurnRunThisPhase = false;
		foreach (ShipComponent ship in _playerShips)
		{
			if (IsShipAlive(ship))
			{
				ship.ClearPendingCommands();
				ship.UpdateUi();
			}
		}
		if (!_remotePvp && _currentPhase == BattlePhase.SpeedAdjust)
		{
			MoveRulesEvaluator.SyncFormationGroups(_playerShips.Where(IsShipAlive).ToList());
		}
		RefreshDirectionOverlays();
		if (!_remotePvp)
		{
			StartPhaseTimer(true);
		}
		_player.BeginPhaseAction(this, _playerShips, _enemyShips, _mapGenerator, _overlay);
	}

	private void RunEnemyTurn()
	{
		_enemyActing = true;
		_enemyTurnRunThisPhase = true;
		var bus = GetNode<EventBus>("EventBus");
		bus.EmitSignal("OverlayClearRequested");

		var aliveEnemies = _enemyShips.Where(IsShipAlive).ToList();
		var alivePlayers = _playerShips.Where(IsShipAlive).ToList();
		if (_currentPhase == BattlePhase.SpeedAdjust)
		{
			MoveRulesEvaluator.SyncFormationGroups(aliveEnemies);
		}
		if (!IsPlayerActionPhase(_currentPhase) || _ai == null
			|| aliveEnemies.Count == 0 || alivePlayers.Count == 0)
		{
			OnEnemySideFinished();
			return;
		}

		bus.EmitLog($"敌方行动：{BattlePhaseMachine.PhaseLabels[(int)_currentPhase]}");
		FocusOnEnemyTurn(aliveEnemies);
		_ai.TakeTurn(aliveEnemies, alivePlayers, _mapGenerator, _overlay, _hud,
			_currentPhase, OnEnemySideFinished);
	}

	private void FocusOnEnemyTurn(List<ShipComponent> enemies)
	{
		if (_mapGenerator == null || enemies.Count == 0) return;
		Vector3 center = Vector3.Zero;
		foreach (ShipComponent enemy in enemies)
		{
			center += _mapGenerator.HexToWorld(enemy.HexCoords.X, enemy.HexCoords.Y);
		}
		center /= enemies.Count;
		GetNode<EventBus>("EventBus").EmitSignal("CameraFocusRequested", center, 22f, 55f);
	}

	private void OnEnemySideFinished()
	{
		_enemyActing = false;
		CancelPhaseTimer();
		if (_battleEnded) return;
		GetNode<EventBus>("EventBus").EmitLog("敌方行动完成，推进阶段");
		CallDeferred(nameof(AdvancePhase));
	}

	public void AdvancePhase()
	{
		if (_advancing || _settling || _battleEnded) return;
		if (_remotePvp)
		{
			CancelPhaseTimer();
			if (_remoteMyTurn && !_remoteCommandsSent)
			{
				_remotePhaseActive = false;
				SendRemoteCommands();
			}
			return;
		}
		if (_enemyActing)
		{
			GetNode<EventBus>("EventBus").EmitLog("敌方行动中，请稍候");
			return;
		}
		CancelPhaseTimer();
		CommitPendingCommands();
		if (!_enemyTurnRunThisPhase)
		{
			_playerFinishedThisPhase = true;
			RunEnemyTurn();
			return;
		}

		int movePhase = CurrentMovePhase;
		if (movePhase is 1 or 2 or 3)
		{
			_advancing = true;
			GetNode<EventBus>("EventBus").EmitSignal("OverlayClearRequested");
			AnimateMovePhaseAndContinue(movePhase);
			return;
		}

		_advancing = true;
		try
		{
			FinishPhaseTransition();
		}
		finally
		{
			_advancing = false;
		}
	}

	private async void AnimateMovePhaseAndContinue(int phase)
	{
		try
		{
			await AnimateMovePhase(phase);
		}
		finally
		{
			_advancing = false;
		}
		FinishPhaseTransition();
	}

	private void FinishPhaseTransition()
	{
		GetNode<EventBus>("EventBus").EmitSignal("OverlayClearRequested");

		BattlePhaseMachine.Transition transition = BattlePhaseMachine.Plan(
			_currentPhase,
			_dataManager?.IsNightBattle ?? false,
			_dataManager.GunfirePhaseEnabled,
			_dataManager.TorpedoPhaseEnabled);
		_currentPhase = transition.Next;

		if (transition.SkipLighting)
		{
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "白天地图，跳过视野/照明阶段");
		}
		if (transition.SkipGunfire)
		{
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "炮击阶段未启用，跳过");
		}
		if (transition.SkipTorpedo)
		{
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "鱼雷阶段未启用，跳过");
		}

		if (_currentPhase == BattlePhase.SpeedAdjust)
		{
			_turnNumber++;
			CurrentCP = Math.Min(CurrentCP + PlayerCommandValue, MaxCP);
			EnemyCurrentCP = Math.Min(EnemyCurrentCP + EnemyCommandValue, EnemyMaxCP);
			ApplyDeferredSpeedCaps();
			foreach (ShipComponent ship in _playerShips.Concat(_enemyShips))
			{
				if (!IsShipAlive(ship)) continue;
				ship.TorpedoFiredLastTurn = ship.TorpedoFiredThisTurn;
				ship.TorpedoFiredThisTurn = false;
				if (ship.CurrentSpeed <= 2
					&& !ship.TurnedThisPhase
					&& !ship.TorpedoFiredLastTurn
					&& ship.CanReloadTorpedoes)
				{
					ship.ReloadTorpedoes();
					GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
						$"♻ {ship.ShipName} 低速未转向且未开火，完成备用鱼雷装填");
				}
				ship.TurnedThisPhase = false;
				ship.UpdateUi();
			}
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
				$"—— 第 {_turnNumber} 回合 ——");
		}

		if (_currentPhase == BattlePhase.EndTurn)
		{
			DoEndTurnSettlement();
			return;
		}

		EmitPhaseChanged();
		EmitCommandStateUpdated();
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
			$"➡ {BattlePhaseMachine.PhaseLabels[(int)_currentPhase]}");

		if (IsPlayerActionPhase(_currentPhase))
		{
			BeginPlayerPhase();
		}
	}
}

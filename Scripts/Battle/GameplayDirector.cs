using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

public enum BattlePhase
{
	SpeedAdjust,   // 0. 速度调整阶段
	MovePhase1,    // 1. 第一移动阶段
	MovePhase2,    // 2. 第二移动阶段
	MovePhase3,    // 3. 第三移动阶段
	ReconLighting, // 4. 视野/照明阶段
	Gunfire,       // 5. 火炮射击阶段
	Torpedo,       // 6. 鱼雷雷击阶段
	EndTurn        // 7. 回合结束结算
}

/// <summary>
/// 战场主控——阶段管线调度器。
/// 持有 LevelDataManager / MapGenerator / UnitSpawner / PlayerController 引用，
/// 在大回合内驱动 8 个阶段的流转（速度调整→三段移动→视野→炮击→雷击→结算），
/// 管理全局 CP 计数，并通过 EventBus 通知各子系统。
/// 白天地图自动跳过视野/照明阶段，仅夜战地图进入 ReconLighting。
/// 三段移动在各自阶段被推进时立即按 SpeedTable 结算，不累积到回合末。
/// </summary>
public partial class GameplayDirector : Node
{
	private LevelDataManager _dataManager;
	private MapGenerator _mapGenerator;
	private UnitSpawner _unitSpawner;
	private GridOverlayController _overlay;
	private PlayerController _player;
	private AIController _ai;
	private BattleHudBroker _hud;

	// —— 阶段管线 ——
	private BattlePhase _currentPhase = BattlePhase.SpeedAdjust;
	private int _turnNumber;

	// —— CP ——
	public int CurrentCP { get; private set; } = 8;
	public int MaxCP { get; private set; } = 12;

	// —— 单位缓存 ——
	private List<ShipComponent> _playerShips = new();
	private List<ShipComponent> _enemyShips = new();
	private bool _advancing;
	private bool _settling;
	private bool _enemyActing;
	private bool _battleEnded;
	private bool _playerFinishedThisPhase;
	private float _phaseTimerRemaining;
	private float _phaseTimerTotal;
	private bool _phaseTimerRunning;
	private bool _timerForPlayer = true;
	private float _timerEmitAccumulator;
	private bool _enemyTurnRunThisPhase;

	private static readonly string[] PhaseLabels =
	{
		"1 速度", "2 ▶ 第一移动", "3 ▶▶ 第二移动", "4 ▶▶▶ 第三移动",
		"5 视野", "6 火炮", "7 鱼雷", "8 结算"
	};

	public override void _Ready()
	{
		_dataManager = GetNode<LevelDataManager>("LevelDataManager");
		_mapGenerator = GetNode<MapGenerator>("MapGenerator");
		_unitSpawner = GetNode<UnitSpawner>("UnitSpawner");
		_overlay = GetNode<GridOverlayController>("GridOverlayController");
		_player = GetNode<PlayerController>("PlayerController");
		_ai = GetNodeOrNull<AIController>("AIController");
		_hud = GetNodeOrNull<BattleHudBroker>("CanvasLayer/BattleUI/InfoLabel");

		var bus = GetNode<EventBus>("EventBus");
		bus.AdvancePhaseClicked += AdvancePhase;
		bus.PlayerSideFinished += OnPlayerSideFinished;
		CallDeferred(MethodName.LaunchBattleField);
	}

	public override void _Process(double delta)
	{
		if (!_phaseTimerRunning) return;
		_phaseTimerRemaining = Mathf.Max(0f, _phaseTimerRemaining - (float)delta);
		_timerEmitAccumulator += (float)delta;
		if (_timerEmitAccumulator >= 0.05f)
		{
			_timerEmitAccumulator = 0f;
			EmitPhaseTimerUpdated();
		}
		if (_phaseTimerRemaining <= 0f)
		{
			_phaseTimerRunning = false;
			OnPhaseTimerExpired();
		}
	}

	public void LaunchBattleField()
	{
		_mapGenerator.BuildMap(_dataManager.TerrainData);
		_unitSpawner.SpawnUnits(_dataManager.UnitData);
		_overlay.InitializeOverlayTargets(_mapGenerator.SpawnedTileMeshes);
		StartBattle();
	}

	private async void StartBattle()
	{
		await ToSignal(GetTree(), "process_frame");
		var all = new List<ShipComponent>();
		foreach (var n in GetTree().GetNodesInGroup("Ships"))
			if (n is ShipComponent s) all.Add(s);
		_playerShips = all.Where(s => s.BattleSide == GenerationSide.Player).ToList();
		_enemyShips = all.Where(s => s.BattleSide == GenerationSide.Enemy).ToList();

		_turnNumber = 1;
		MaxCP = Math.Max(4, _dataManager.PlayerCommand * 2);
		CurrentCP = Math.Min(_dataManager.PlayerInitialCP, MaxCP);
		_currentPhase = BattlePhase.SpeedAdjust;
		EmitPhaseChanged();
		EmitCpUpdated();
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
			$"—— 第 {_turnNumber} 回合 —— {PhaseLabels[(int)_currentPhase]}");

		// 启动首阶段
		if (CheckBattleEnd()) return;
		BeginPlayerPhase();
	}

	private void OnPlayerSideFinished()
	{
		_playerFinishedThisPhase = true;
		CancelPhaseTimer();
		if (_battleEnded || _enemyActing) return;
		RunEnemyTurn();
	}

	/// <summary>启动玩家阶段：重置待命状态、启动玩家倒计时，再进入逐船操作。</summary>
	private void BeginPlayerPhase()
	{
		_playerFinishedThisPhase = false;
		_enemyTurnRunThisPhase = false;
		foreach (var ship in _playerShips)
			if (IsShipAlive(ship))
			{
				ship.ClearPendingCommands();
				ship.UpdateUi();
			}
		StartPhaseTimer(true);
		_player.BeginPhaseAction(this, _playerShips, _enemyShips, _mapGenerator, _overlay);
	}

	/// <summary>玩家侧操作队列清空后，同一阶段内接续敌方 AI 操作。</summary>
	private void RunEnemyTurn()
	{
		_enemyActing = true;
		_enemyTurnRunThisPhase = true;
		var bus = GetNode<EventBus>("EventBus");
		bus.EmitSignal("OverlayClearRequested");

		var aliveEnemies = _enemyShips.Where(IsShipAlive).ToList();
		var alivePlayers = _playerShips.Where(IsShipAlive).ToList();
		if (!IsPlayerActionPhase(_currentPhase) || _ai == null
			|| aliveEnemies.Count == 0 || alivePlayers.Count == 0)
		{
			OnEnemySideFinished();
			return;
		}

		bus.EmitLog($"敌方行动：{PhaseLabels[(int)_currentPhase]}");
		FocusOnEnemyTurn(aliveEnemies);
		_ai.TakeTurn(aliveEnemies, alivePlayers, _mapGenerator, _overlay, _hud,
			_currentPhase, OnEnemySideFinished);
	}

	/// <summary>敌方行动开始时把镜头平滑移到敌方舰队中心。</summary>
	private void FocusOnEnemyTurn(List<ShipComponent> enemies)
	{
		if (_mapGenerator == null || enemies.Count == 0) return;
		Vector3 center = Vector3.Zero;
		foreach (var enemy in enemies)
			center += _mapGenerator.HexToWorld(enemy.HexCoords.X, enemy.HexCoords.Y);
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

	/// <summary>推进到下一个阶段；从移动阶段离开时先执行该阶段的惯性移动。</summary>
	public void AdvancePhase()
	{
		if (_advancing || _settling || _battleEnded) return;
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

	/// <summary>等待移动动画结束后再切换阶段，避免船还没到位就开始下一阶段交互。</summary>
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

		// 照明阶段仅在夜战地图启用；白天从 MovePhase3 直接跳到 Gunfire。
		bool isNight = _dataManager?.IsNightBattle ?? false;
		bool skipLighting = _currentPhase == BattlePhase.MovePhase3 && !isNight;
		bool skipTorpedo = _currentPhase == BattlePhase.Gunfire && !_dataManager.TorpedoPhaseEnabled;

		_currentPhase = _currentPhase switch
		{
			BattlePhase.SpeedAdjust   => BattlePhase.MovePhase1,
			BattlePhase.MovePhase1    => BattlePhase.MovePhase2,
			BattlePhase.MovePhase2    => BattlePhase.MovePhase3,
			BattlePhase.MovePhase3    => skipLighting ? BattlePhase.Gunfire : BattlePhase.ReconLighting,
			BattlePhase.ReconLighting => BattlePhase.Gunfire,
			BattlePhase.Gunfire       => skipTorpedo ? BattlePhase.EndTurn : BattlePhase.Torpedo,
			BattlePhase.Torpedo       => BattlePhase.EndTurn,
			BattlePhase.EndTurn       => BattlePhase.SpeedAdjust,
			_                         => BattlePhase.SpeedAdjust,
		};

		if (skipLighting)
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "白天地图，跳过视野/照明阶段");
		if (skipTorpedo)
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "鱼雷阶段未启用，跳过");

		if (_currentPhase == BattlePhase.SpeedAdjust)
		{
			_turnNumber++;
			CurrentCP = Math.Min(CurrentCP + _dataManager.PlayerCommand, MaxCP);
			ApplyDeferredSpeedCaps();
			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (IsShipAlive(ship))
				{
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
		EmitCpUpdated();
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
			$"➡ {PhaseLabels[(int)_currentPhase]}");

		// 可玩家交互的阶段自动启动
		if (IsPlayerActionPhase(_currentPhase))
			BeginPlayerPhase();
	}

	/// <summary>手动推进时提交所有我方船的待命指令：速度、转向、炮击。</summary>
	private void CommitPendingCommands()
	{
		var bus = GetNode<EventBus>("EventBus");
		foreach (var ship in _playerShips)
		{
			if (!IsShipAlive(ship))
			{
				ship.ClearPendingCommands();
				continue;
			}

			if (ship.PendingSpeed >= 0)
			{
				int old = ship.CurrentSpeed;
				ship.CurrentSpeed = ship.PendingSpeed;
				bus.EmitLog($"{ship.ShipName} 航速待命生效 {old} → {ship.CurrentSpeed}");
			}

			if (ship.PendingDirection.HasValue)
			{
				ship.Direction = ship.PendingDirection.Value;
				ship.TurnedThisPhase = true;
				ship.UpdateUi();
				bus.EmitLog($"{ship.ShipName} 转向待命生效 → {ship.Direction}");
			}

			if (ship.PendingAttackTarget != null && GodotObject.IsInstanceValid(ship.PendingAttackTarget))
			{
				ShipComponent target = ship.PendingAttackTarget;
				int attackCost = ship.ShipClass == "BB" ? 2 : 1;
				if (TryConsumeCP(attackCost) && ship.MainAmmo > 0
					&& CombatRulesEvaluator.CanFireInArc(ship, target))
				{
					ship.MainAmmo--;
					var (_, _, desc) = CombatRulesEvaluator.FireEx(ship, target,
						ship.PendingAttackDistance, ship.PendingRadarUsed);
					bus.EmitLog(desc);
				}
				else
				{
					bus.EmitLog($"{ship.ShipName} 炮击未执行（CP 或条件不足）");
				}
			}

			ship.ClearPendingCommands();
		}
	}

	private void StartPhaseTimer(bool forPlayer)
	{
		CancelPhaseTimer();
		if (_dataManager?.PhaseSecondsPerShip == null) return;
		int phase = (int)_currentPhase;
		if (phase < 0 || phase >= _dataManager.PhaseSecondsPerShip.Length) return;
		int perShip = _dataManager.PhaseSecondsPerShip[phase];
		if (perShip <= 0) return;
		int count = forPlayer ? _playerShips.Count(IsShipAlive) : _enemyShips.Count(IsShipAlive);
		if (count <= 0) return;
		_phaseTimerTotal = Math.Max(1, perShip * count + _dataManager.PhaseExtraSeconds);
		_phaseTimerRemaining = _phaseTimerTotal;
		_phaseTimerRunning = true;
		_timerForPlayer = forPlayer;
		_timerEmitAccumulator = 0f;
		EmitPhaseTimerUpdated();
	}

	private void CancelPhaseTimer()
	{
		if (!_phaseTimerRunning) return;
		_phaseTimerRunning = false;
		EmitPhaseTimerUpdated();
	}

	private void OnPhaseTimerExpired()
	{
		if (_battleEnded || _settling) return;
		if (_enemyActing) return;
		if (!_playerFinishedThisPhase)
		{
			_playerFinishedThisPhase = true;
			CommitPendingCommands();
			RunEnemyTurn();
		}
	}

	private void EmitPhaseTimerUpdated()
	{
		GetNode<EventBus>("EventBus").EmitSignal("PhaseTimerUpdated",
			_phaseTimerRemaining, _phaseTimerTotal);
	}

	private static bool IsPlayerActionPhase(BattlePhase p) => p switch
	{
		BattlePhase.SpeedAdjust => true,
		BattlePhase.MovePhase1 => true,
		BattlePhase.MovePhase2 => true,
		BattlePhase.MovePhase3 => true,
		BattlePhase.Gunfire => true,
		_ => false
	};

	/// <summary>尝试消耗 CP。成功则返回 true 并 Emit CpUpdated 信号。</summary>
	public bool TryConsumeCP(int amount)
	{
		if (CurrentCP < amount) return false;
		CurrentCP -= amount;
		EmitCpUpdated();
		return true;
	}

	/// <summary>增加 CP（不超过上限），Emit CpUpdated 信号。</summary>
	public void AddCP(int amount)
	{
		CurrentCP = Math.Min(CurrentCP + amount, MaxCP);
		EmitCpUpdated();
	}

	private async void DoEndTurnSettlement()
	{
		if (_settling) return;
		_settling = true;
		var bus = GetNode<EventBus>("EventBus");
		try
		{
			bus.EmitSignal("LogMessage", "回合结算：判定检定……");
			var checks = new List<string>();
			foreach (var ship in _playerShips.Concat(_enemyShips))
			{
				if (!GodotObject.IsInstanceValid(ship) || ship.PendingShotChecks.Count == 0) continue;
				checks.AddRange(ship.PendingShotChecks);
			}
			if (checks.Count > 0)
				bus.EmitSignal("LogMessage", string.Join("\n", checks));

			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (GodotObject.IsInstanceValid(ship) && ship.PendingDamage > 0)
					ship.ApplyPendingDamage();

			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (GodotObject.IsInstanceValid(ship))
					ship.PendingShotChecks.Clear();

			if (!CheckBattleEnd())
				await ToSignal(GetTree(), "process_frame");
		}
		finally
		{
			if (_battleEnded)
				_settling = false;
			else
				CallDeferred(nameof(ContinueAfterSettlement));
		}
	}

	private void ContinueAfterSettlement()
	{
		_settling = false;
		AdvancePhase();
	}

	/// <summary>移动阶段自动执行该阶段位移：按 SpeedTable 推算格数，用 Tween 播放位移动画。</summary>
	private async System.Threading.Tasks.Task AnimateMovePhase(int phase)
	{
		float longest = 0f;
		var bus = GetNode<EventBus>("EventBus");
		var occupied = new HashSet<Vector2I>();
		var occupiedShips = new Dictionary<Vector2I, ShipComponent>();
		foreach (var ship in _playerShips.Concat(_enemyShips))
			if (IsShipAlive(ship))
			{
				occupied.Add(ship.HexCoords);
				occupiedShips[ship.HexCoords] = ship;
			}

		foreach (var ship in _playerShips.Concat(_enemyShips))
		{
			if (!GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0) continue;

			int requestedSteps = MoveRulesEvaluator.MovementForPhase(
				ship.CurrentSpeed, phase, _turnNumber % 2 == 1);
			int steps = MoveRulesEvaluator.AdvanceSteps(ship.HexCoords, ship.Direction,
				requestedSteps, hex => (_dataManager?.IsIsland(hex) ?? false)
					|| (occupied.Contains(hex) && hex != ship.HexCoords));

			Vector2I off = HexDirectionUtility.Offset(ship.Direction);
			if (steps < requestedSteps)
			{
				Vector2I blockedHex = ship.HexCoords + off * (steps + 1);
				if (_dataManager?.IsIsland(blockedHex) ?? false)
				{
					bus.EmitLog($"🪨 {ship.ShipName} 撞击岛屿，直接沉没！");
					ship.TakeDamage(ship.CurrentHp);
					continue;
				}
				if (occupiedShips.TryGetValue(blockedHex, out var blocker) && blocker != ship)
				{
					if (CollisionRulesEvaluator.IsCollision())
					{
						int hullSum = ship.MaxHp + blocker.MaxHp;
						var (rollA, dmgA) = CollisionRulesEvaluator.RollDamage(hullSum);
						var (rollB, dmgB) = CollisionRulesEvaluator.RollDamage(hullSum);
						bus.EmitLog($"💥 {ship.ShipName} 与 {blocker.ShipName} 发生冲撞！（{rollA}→{dmgA}，{rollB}→{dmgB}）");
						ship.TakeDamage(dmgA);
						blocker.TakeDamage(dmgB);
					}
					else
					{
						bus.EmitLog($"⚠️ {ship.ShipName} 前方有舰船但未发生冲撞，停在 {steps} 格前");
					}
				}
				else
				{
					bus.EmitLog($"⚠️ {ship.ShipName} 前方受阻，仅推进 {steps} 格");
				}
			}
			if (steps <= 0) continue;

			Vector2I target = ship.HexCoords + off * steps;
			occupied.Remove(ship.HexCoords);
			occupied.Add(target);
			float duration = 0.35f + steps * 0.2f;
			longest = Mathf.Max(longest, duration);
			ship.AnimateMoveTo(_mapGenerator, target, duration);
		}

		if (longest > 0f)
			await ToSignal(GetTree().CreateTimer(longest + 0.1f), "timeout");
	}

	public BattlePhase CurrentPhase => _currentPhase;
	public int TurnNumber => _turnNumber;
	public int CurrentMovePhase => _currentPhase switch
	{
		BattlePhase.MovePhase1 => 1, BattlePhase.MovePhase2 => 2, BattlePhase.MovePhase3 => 3,
		_ => 0
	};
	public IReadOnlyList<ShipComponent> PlayerShips => _playerShips;
	public IReadOnlyList<ShipComponent> EnemyShips => _enemyShips;

	/// <summary>任一方全灭时结束战斗，并通知 UI 显示结果。</summary>
	private bool CheckBattleEnd()
	{
		int playerCount = _playerShips.Count(IsShipAlive);
		int enemyCount = _enemyShips.Count(IsShipAlive);
		if (playerCount > 0 && enemyCount > 0) return false;

		_battleEnded = true;
		bool playerWon = playerCount > 0;
		string result = playerWon ? "胜利" : "失败";
		string detail = playerWon ? "敌方舰队已全灭" : "我方舰队已全灭";
		var bus = GetNode<EventBus>("EventBus");
		bus.EmitLog($"🏁 {result}：{detail}");
		bus.EmitSignal("BattleEnded", result, detail);
		return true;
	}

	private static bool IsShipAlive(ShipComponent ship)
		=> GodotObject.IsInstanceValid(ship) && ship.CurrentHp > 0;

	/// <summary>损伤导致的降速不立即生效，统一在下一回合速度调整阶段强制压速。</summary>
	private void ApplyDeferredSpeedCaps()
	{
		var bus = GetNode<EventBus>("EventBus");
		foreach (var ship in _playerShips.Concat(_enemyShips))
		{
			if (!IsShipAlive(ship)) continue;
			int cap = ship.MaxSpeedForCurrentState;
			if (ship.CurrentSpeed > cap)
			{
				int old = ship.CurrentSpeed;
				ship.CurrentSpeed = cap;
				bus.EmitLog($"{ship.ShipName} 因损伤强制降速 {old} → {cap}");
			}
		}
	}

	private void EmitPhaseChanged() =>
		GetNode<EventBus>("EventBus").EmitSignal("PhaseChanged",
			PhaseLabels[(int)_currentPhase], (int)_currentPhase, _turnNumber);

	private void EmitCpUpdated() =>
		GetNode<EventBus>("EventBus").EmitSignal("CpUpdated", CurrentCP, MaxCP);
}

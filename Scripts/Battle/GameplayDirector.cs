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
/// </summary>
public partial class GameplayDirector : Node
{
	private LevelDataManager _dataManager;
	private MapGenerator _mapGenerator;
	private UnitSpawner _unitSpawner;
	private GridOverlayController _overlay;
	private PlayerController _player;

	// —— 阶段管线 ——
	private BattlePhase _currentPhase = BattlePhase.SpeedAdjust;
	private int _turnNumber;

	// —— CP ——
	public int CurrentCP { get; private set; } = 8;
	public int MaxCP { get; private set; } = 12;

	// —— 单位缓存 ——
	private List<ShipComponent> _playerShips = new();
	private List<ShipComponent> _enemyShips = new();

	private static readonly string[] PhaseLabels =
	{
		"速度", "▶ 第一移动", "▶▶ 第二移动", "▶▶▶ 第三移动",
		"视野", "火炮", "鱼雷", "结算"
	};

	public override void _Ready()
	{
		_dataManager = GetNode<LevelDataManager>("LevelDataManager");
		_mapGenerator = GetNode<MapGenerator>("MapGenerator");
		_unitSpawner = GetNode<UnitSpawner>("UnitSpawner");
		_overlay = GetNode<GridOverlayController>("GridOverlayController");
		_player = GetNode<PlayerController>("PlayerController");

		GetNode<EventBus>("EventBus").AdvancePhaseClicked += AdvancePhase;
		CallDeferred(MethodName.LaunchBattleField);
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
		CurrentCP = MaxCP;
		_currentPhase = BattlePhase.SpeedAdjust;
		EmitPhaseChanged();
		EmitCpUpdated();
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage",
			$"—— 第 {_turnNumber} 回合 —— {PhaseLabels[(int)_currentPhase]}");

		// 启动首阶段
		_player.BeginPhaseAction(this, _playerShips, _enemyShips, _mapGenerator, _overlay);
	}

	/// <summary>推进到下一个阶段。白天地图从第三移动阶段直接进入火炮阶段。</summary>
	public void AdvancePhase()
	{
		GetNode<EventBus>("EventBus").EmitSignal("OverlayClearRequested");

		// 照明阶段仅在夜战地图启用；白天从 MovePhase3 直接跳到 Gunfire。
		bool isNight = _dataManager?.IsNightBattle ?? false;
		bool skipLighting = _currentPhase == BattlePhase.MovePhase3 && !isNight;

		_currentPhase = _currentPhase switch
		{
			BattlePhase.SpeedAdjust   => BattlePhase.MovePhase1,
			BattlePhase.MovePhase1    => BattlePhase.MovePhase2,
			BattlePhase.MovePhase2    => BattlePhase.MovePhase3,
			BattlePhase.MovePhase3    => skipLighting ? BattlePhase.Gunfire : BattlePhase.ReconLighting,
			BattlePhase.ReconLighting => BattlePhase.Gunfire,
			BattlePhase.Gunfire       => BattlePhase.Torpedo,
			BattlePhase.Torpedo       => BattlePhase.EndTurn,
			BattlePhase.EndTurn       => BattlePhase.SpeedAdjust,
			_                         => BattlePhase.SpeedAdjust,
		};

		if (skipLighting)
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "白天地图，跳过视野/照明阶段");

		if (_currentPhase == BattlePhase.SpeedAdjust)
		{
			_turnNumber++;
			CurrentCP = Math.Min(CurrentCP + 3, MaxCP);
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
			_player.BeginPhaseAction(this, _playerShips, _enemyShips, _mapGenerator, _overlay);
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

	private void DoEndTurnSettlement()
	{
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "回合结算中……");
		GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "下一回合……");
		CallDeferred(nameof(AdvancePhase));
	}

	public BattlePhase CurrentPhase => _currentPhase;
	public int TurnNumber => _turnNumber;
	public bool IsOddTurn => _turnNumber % 2 == 1;
	public int CurrentMovePhase => _currentPhase switch
	{
		BattlePhase.MovePhase1 => 1, BattlePhase.MovePhase2 => 2, BattlePhase.MovePhase3 => 3,
		_ => 0
	};
	public IReadOnlyList<ShipComponent> PlayerShips => _playerShips;
	public IReadOnlyList<ShipComponent> EnemyShips => _enemyShips;

	private void EmitPhaseChanged() =>
		GetNode<EventBus>("EventBus").EmitSignal("PhaseChanged",
			PhaseLabels[(int)_currentPhase], (int)_currentPhase);

	private void EmitCpUpdated() =>
		GetNode<EventBus>("EventBus").EmitSignal("CpUpdated", CurrentCP, MaxCP);
}
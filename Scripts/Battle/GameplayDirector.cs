using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DreadnoughtDeparture.Network;
using DreadnoughtDeparture.Story;

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
	private BattleFeedbackController _feedback;
	private TorpedoController _torpedoController;
	private PlayerController _player;
	private AIController _ai;
	private BattleHudBroker _hud;

	// —— 阶段管线 ——
	private BattlePhase _currentPhase = BattlePhase.SpeedAdjust;
	private int _turnNumber;

	// —— CP ——
	private readonly BattleEconomyState _economy = new();
	public int CurrentCP { get => _economy.CurrentCP; set => _economy.CurrentCP = value; }
	public int MaxCP { get => _economy.MaxCP; set => _economy.MaxCP = value; }
	public int EnemyCurrentCP { get => _economy.EnemyCurrentCP; set => _economy.EnemyCurrentCP = value; }
	public int EnemyMaxCP { get => _economy.EnemyMaxCP; set => _economy.EnemyMaxCP = value; }
	public int PlayerCommandValue { get => _economy.PlayerCommandValue; set => _economy.PlayerCommandValue = value; }
	public int EnemyCommandValue { get => _economy.EnemyCommandValue; set => _economy.EnemyCommandValue = value; }
	public int PlayerScore { get => _economy.PlayerScore; set => _economy.PlayerScore = value; }
	public int EnemyScore { get => _economy.EnemyScore; set => _economy.EnemyScore = value; }

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
	private bool _remotePvp;
	private string _lastRemotePhase = "";
	private int _lastRemoteTurn = -1;
	private bool _remoteCommandsSent;
	private bool _remotePhaseActive;
	private bool _remoteMyTurn;
	private bool _remotePaused;
	private bool _storyPlaying;
	private bool _storyPausedTimerForPlayer = true;
	private long _remoteTimerEndAt;
	private int _remoteTimerTotal;
	private Button _advanceButton;
	private readonly Dictionary<string, ShipComponent> _remoteShips = new();
	private readonly Dictionary<string, TorpedoComponent> _remoteTorpedoes = new();
	private readonly Dictionary<string, Tween> _remoteTweens = new();
	private readonly MoveSettlementService _moveSettlement = new();
	private readonly HashSet<Vector2I> _playerReachedHexes = new();
	private readonly HashSet<Vector2I> _enemyReachedHexes = new();
	private readonly Dictionary<string, int> _playerActionCounts = new();
	private readonly HashSet<ShipComponent> _countedSunk = new();
	private int _enemySunkCount;
	private int _playerSunkCount;

	public override void _Ready()
	{
		_dataManager = GetNode<LevelDataManager>("LevelDataManager");
		_mapGenerator = GetNode<MapGenerator>("MapGenerator");
		_unitSpawner = GetNode<UnitSpawner>("UnitSpawner");
		_overlay = GetNode<GridOverlayController>("GridOverlayController");
		_feedback = new BattleFeedbackController();
		AddChild(_feedback);
		_torpedoController = new TorpedoController();
		AddChild(_torpedoController);
		_player = GetNode<PlayerController>("PlayerController");
		_ai = GetNodeOrNull<AIController>("AIController");
		_hud = GetNodeOrNull<BattleHudBroker>("CanvasLayer/BattleUI/InfoLabel");
		_moveSettlement.Map = _mapGenerator;
		_moveSettlement.Data = _dataManager;
		_moveSettlement.IsAlive = IsShipAlive;
		_moveSettlement.RefreshCommandValues = RefreshCommandValues;
		_economy.Changed += EmitCommandStateUpdated;

		var bus = GetNode<EventBus>("EventBus");
		var storyDirector = new StoryDirector();
		AddChild(storyDirector);
		bus.AdvancePhaseClicked += AdvancePhase;
		bus.PlayerSideFinished += OnPlayerSideFinished;
		bus.PlayerActionPerformed += actionId =>
		{
			_playerActionCounts.TryGetValue(actionId, out int count);
			_playerActionCounts[actionId] = count + 1;
		};
		bus.StoryPlaybackStarted += OnStoryPlaybackStarted;
		bus.StoryPlaybackEnded += OnStoryPlaybackEnded;
		if (PvpFlowState.PvpBattle)
		{
			_remotePvp = true;
			ProcessMode = ProcessModeEnum.Always;
			if (_ai != null)
			{
				_ai.ProcessMode = ProcessModeEnum.Disabled;
			}
			NetworkClient.Instance.WsMessageReceived += OnRemotePvpMessage;
			NetworkClient.Instance.ConnectionStateChanged += OnPvpConnectionChanged;
			OnPvpConnectionChanged(NetworkClient.Instance.IsWebSocketConnected);
		}
		CallDeferred(MethodName.LaunchBattleField);
	}

	public override void _ExitTree()
	{
		if (_remotePvp && NetworkClient.Instance != null)
		{
			NetworkClient.Instance.WsMessageReceived -= OnRemotePvpMessage;
			NetworkClient.Instance.ConnectionStateChanged -= OnPvpConnectionChanged;
		}
	}

	private void OnStoryPlaybackStarted()
	{
		_storyPlaying = true;
		_storyPausedTimerForPlayer = _timerForPlayer;
		CancelPhaseTimer();
	}

	private void OnStoryPlaybackEnded()
	{
		_storyPlaying = false;
		if (_remotePvp || _battleEnded || _enemyActing) return;
		if (IsPlayerActionPhase(_currentPhase))
		{
			StartPhaseTimer(_storyPausedTimerForPlayer);
		}
	}

	public override void _Process(double delta)
	{
		if (_remotePvp)
		{
			if (_remoteTimerEndAt > 0)
			{
				long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
					+ NetworkClient.Instance.ServerTimeOffsetMs;
				_phaseTimerRemaining = Math.Max(0f, (_remoteTimerEndAt - nowMs) / 1000f);
				_phaseTimerTotal = _remoteTimerTotal;
				_timerEmitAccumulator += (float)delta;
				if (_timerEmitAccumulator >= 0.1f)
				{
					_timerEmitAccumulator = 0f;
					EmitPhaseTimerUpdated();
				}
				if (_remoteMyTurn && !_remoteCommandsSent &&
					nowMs >= _remoteTimerEndAt - 500)
				{
					SendRemoteCommands();
				}
			}
			return;
		}
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
		_overlay.BuildGlobalGrid();
		_overlay.BuildSpecialCellOverlays();
		if (_remotePvp)
		{
			_overlay.InitializeOverlayTargets(_mapGenerator.SpawnedTileMeshes);
			StartRemotePvp();
			return;
		}
		_unitSpawner.SpawnUnits(_dataManager.UnitData);
		_overlay.InitializeOverlayTargets(_mapGenerator.SpawnedTileMeshes);
		StartBattle();
	}

	private void StartPhaseTimer(bool forPlayer)
	{
		if (_storyPlaying) return;
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
		if (_remotePvp)
		{
			if (_timerForPlayer && !_remoteCommandsSent)
			{
				_remotePhaseActive = false;
				SendRemoteCommands();
			}
			return;
		}
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
		BattlePhase.Torpedo => true,
		_ => false
	};

	/// <summary>尝试消耗我方 CP。成功则返回 true 并刷新 HUD。</summary>
	public bool TryConsumeCP(int amount)
		=> _economy.TryConsumePlayer(amount);

	/// <summary>增加我方 CP（不超过上限）。</summary>
	public void AddCP(int amount)
		=> _economy.AddPlayer(amount);

	/// <summary>尝试消耗敌方 CP。成功则返回 true 并刷新 HUD。</summary>
	public bool TryConsumeEnemyCP(int amount)
		=> _economy.TryConsumeEnemy(amount);

	/// <summary>增加敌方 CP（不超过上限）。</summary>
	public void AddEnemyCP(int amount)
		=> _economy.AddEnemy(amount);

	/// <summary>按舰船损伤重算双方指挥值、CP 上限与 PV 得分。</summary>
	public void RefreshCommandValues()
		=> _economy.Refresh(
			_playerShips,
			_enemyShips,
			_dataManager?.PlayerCommand ?? 5,
			_dataManager?.EnemyCommand ?? 4);

	/// <summary>按对方舰船当前损伤状态计算 PV 得分。</summary>
	public void RefreshScores()
		=> _economy.RefreshScores(_playerShips, _enemyShips);

	public BattlePhase CurrentPhase => _currentPhase;
	public int TurnNumber => _turnNumber;
	public bool IsPlayerSecondTurn => _dataManager?.InitiativeOwner == "enemy";
	public int CurrentMovePhase => _currentPhase switch
	{
		BattlePhase.MovePhase1 => 1, BattlePhase.MovePhase2 => 2, BattlePhase.MovePhase3 => 3,
		_ => 0
	};
	public IReadOnlyList<ShipComponent> PlayerShips => _playerShips;
	public IReadOnlyList<ShipComponent> EnemyShips => _enemyShips;

	/// <summary>任一方全灭时结束战斗，并通知 UI 显示结果。</summary>
	private static bool IsShipAlive(ShipComponent ship)
		=> GodotObject.IsInstanceValid(ship) && ship.CurrentHp > 0;

	private void EmitPhaseChanged() =>
		GetNode<EventBus>("EventBus").EmitSignal("PhaseChanged",
			BattlePhaseMachine.PhaseLabels[(int)_currentPhase], (int)_currentPhase, _turnNumber);

	private void EmitCommandStateUpdated() =>
		GetNode<EventBus>("EventBus").EmitSignal("CommandStateUpdated",
			PlayerCommandValue, CurrentCP, MaxCP,
			EnemyCommandValue, EnemyCurrentCP, EnemyMaxCP,
			PlayerScore, EnemyScore);
}

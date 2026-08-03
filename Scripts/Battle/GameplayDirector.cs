using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DreadnoughtDeparture.Network;

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
	public int EnemyCurrentCP { get; private set; } = 8;
	public int EnemyMaxCP { get; private set; } = 12;
	public int PlayerCommandValue { get; private set; } = 5;
	public int EnemyCommandValue { get; private set; } = 4;
	public int PlayerScore { get; private set; }
	public int EnemyScore { get; private set; }

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
	private long _remoteTimerEndAt;
	private int _remoteTimerTotal;
	private Button _advanceButton;
	private readonly Dictionary<string, ShipComponent> _remoteShips = new();
	private readonly Dictionary<string, Tween> _remoteTweens = new();
	private readonly Dictionary<ShipComponent, FormationTrail> _formationTrails = new();

	/// <summary>单纵阵首舰历史轨迹：Cells[i] 对应到达后的航向 Headings[i]。</summary>
	private sealed class FormationTrail
	{
		public readonly List<Vector2I> Cells = new();
		public readonly List<HexDirection> Headings = new();
		public readonly List<ShipComponent> Members = new();
	}

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
		if (PvpFlowState.PvpBattle)
		{
			_remotePvp = true;
			if (_ai != null)
			{
				_ai.ProcessMode = ProcessModeEnum.Disabled;
			}
			NetworkClient.Instance.WsMessageReceived += OnRemotePvpMessage;
		}
		CallDeferred(MethodName.LaunchBattleField);
	}

	public override void _ExitTree()
	{
		if (_remotePvp && NetworkClient.Instance != null)
		{
			NetworkClient.Instance.WsMessageReceived -= OnRemotePvpMessage;
		}
	}

	public override void _Process(double delta)
	{
		if (_remotePvp)
		{
			if (_remoteTimerEndAt > 0 && _remoteMyTurn && !_remoteCommandsSent &&
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() +
					NetworkClient.Instance.ServerTimeOffsetMs >= _remoteTimerEndAt)
			{
				SendRemoteCommands();
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

	private async void StartRemotePvp()
	{
		var bus = GetNode<EventBus>("EventBus");
		bus.EmitLog("PvP 远程战场已启动，等待服务端状态...");
		try
		{
			await NetworkClient.Instance.FetchServerTimeAsync();
		}
		catch
		{
			// 授时失败时继续，倒计时将退化为本地估算。
		}
		if (string.IsNullOrEmpty(PvpMapState.MapJson) &&
			!string.IsNullOrEmpty(PvpFlowState.PendingRoomId))
		{
			try
			{
				JsonElement mapResult = await NetworkClient.Instance.DownloadMapAsync(
					PvpFlowState.PendingRoomId);
				if (mapResult.TryGetProperty("map", out JsonElement map) &&
					map.ValueKind == JsonValueKind.Object)
				{
					PvpMapState.MapJson = map.GetRawText();
					PvpMapState.MapName = map.TryGetProperty("Name", out JsonElement nameProp)
						? nameProp.GetString() ?? ""
						: "";
				}
			}
			catch (Exception ex)
			{
				bus.EmitLog($"PvP 地图下载失败：{ex.Message}");
			}
		}
		if (!string.IsNullOrEmpty(PvpMapState.MapJson) &&
			_dataManager.TerrainSources.Count == 0)
		{
			if (_dataManager.LoadMapFromJson(PvpMapState.MapJson))
			{
				_mapGenerator.BuildMap(_dataManager.TerrainData);
				FrameRemoteMap(bus);
				bus.EmitLog($"PvP 地图已加载（地形 {_dataManager.TerrainSources.Count}）");
			}
			else
			{
				bus.EmitLog("PvP 地图加载失败");
			}
		}
		else if (_dataManager.TerrainSources.Count == 0)
		{
			bus.EmitLog("PvP 地图为空，未生成地形");
		}
		if (!string.IsNullOrEmpty(PvpFlowState.PendingRoomId))
		{
			NetworkClient.Instance.SendWsJoinRoom(PvpFlowState.PendingRoomId);
		}
		if (!string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
		{
			NetworkClient.Instance.SendWsGetBattleState(PvpFlowState.PendingBattleId);
		}
	}

	private void FrameRemoteMap(EventBus bus)
	{
		if (_dataManager.TerrainSources.Count == 0)
		{
			return;
		}
		Vector2I min = new Vector2I(int.MaxValue, int.MaxValue);
		Vector2I max = new Vector2I(int.MinValue, int.MinValue);
		foreach (Vector2I hex in _dataManager.TerrainSources.Keys)
		{
			min = new Vector2I(Mathf.Min(min.X, hex.X), Mathf.Min(min.Y, hex.Y));
			max = new Vector2I(Mathf.Max(max.X, hex.X), Mathf.Max(max.Y, hex.Y));
		}
		Vector3 center = (_mapGenerator.HexToWorld(min.X, min.Y)
			+ _mapGenerator.HexToWorld(max.X, max.Y)) * 0.5f;
		float span = Mathf.Sqrt((max - min).LengthSquared());
		float distance = Mathf.Clamp(span * 2.2f, 24f, 140f);
		bus.EmitSignal("CameraFocusRequested", center, distance, 55f);
	}

	private void OnRemotePvpMessage(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			string type = root.TryGetProperty("type", out JsonElement typeProp)
				? typeProp.GetString() ?? ""
				: "";
			if (type == "battle.state" && root.TryGetProperty("state", out JsonElement state))
			{
				ApplyRemoteState(state);
			}
			else if (type == "error")
			{
				string code = root.TryGetProperty("code", out JsonElement codeProp)
					? codeProp.GetString() ?? ""
					: "";
				GetNode<EventBus>("EventBus").EmitLog($"PvP 服务端错误：{code}");
			}
		}
		catch
		{
			// 忽略非 JSON 消息。
		}
	}

	private void ApplyRemoteState(JsonElement state)
	{
		var bus = GetNode<EventBus>("EventBus");
		int turn = state.TryGetProperty("turn", out JsonElement turnProp)
			? turnProp.GetInt32()
			: 0;
		string phase = state.TryGetProperty("phase", out JsonElement phaseProp)
			? phaseProp.GetString() ?? ""
			: "";
		if (turn != _lastRemoteTurn || phase != _lastRemotePhase)
		{
			_lastRemoteTurn = turn;
			_lastRemotePhase = phase;
			_turnNumber = turn;
			_currentPhase = RemotePhaseToLocal(phase);
			_remoteCommandsSent = false;
			_remoteTimerEndAt = 0;
			CancelPhaseTimer();
			EmitPhaseChanged();
			bus.EmitLog($"—— PvP 第 {turn} 回合 · {phase} ——");
		}

		if (!state.TryGetProperty("ships", out JsonElement ships))
		{
			return;
		}

		var seen = new HashSet<string>();
		var pending = new List<(string Id, ShipComponent Ship, Vector2I Hex, bool IsNew, int StackIndex, int StackTotal)>();
		int mySide = 0;
		if (state.TryGetProperty("players", out JsonElement players))
		{
			int index = 0;
			foreach (JsonElement player in players.EnumerateArray())
			{
				if (player.GetString() == NetworkClient.Instance.UserId)
				{
					mySide = index;
					break;
				}
				index++;
			}
		}
		foreach (JsonElement ship in ships.EnumerateArray())
		{
			bool isNew = false;
			string id = ship.TryGetProperty("id", out JsonElement idProp)
				? idProp.GetString() ?? ""
				: "";
			if (string.IsNullOrEmpty(id))
			{
				continue;
			}

			JsonElement hex = ship.TryGetProperty("hex", out JsonElement hexProp)
				? hexProp
				: default;
			if (hex.ValueKind != JsonValueKind.Array || hex.GetArrayLength() < 2)
			{
				continue;
			}

			int side = ship.TryGetProperty("side", out JsonElement sideProp)
				? sideProp.GetInt32()
				: 0;
			int facing = ship.TryGetProperty("facing", out JsonElement facingProp)
				? facingProp.GetInt32()
				: 0;
			int speed = ship.TryGetProperty("speed", out JsonElement speedProp)
				? speedProp.GetInt32()
				: 0;
			int hp = ship.TryGetProperty("hp", out JsonElement hpProp)
				? hpProp.GetInt32()
				: 0;
			int maxHp = ship.TryGetProperty("maxHp", out JsonElement maxHpProp)
				? maxHpProp.GetInt32()
				: 0;

			if (!_remoteShips.TryGetValue(id, out ShipComponent component))
			{
				string shipId = ship.TryGetProperty("shipId", out JsonElement shipIdProp)
					? shipIdProp.GetString() ?? ""
					: "";
				PackedScene prefab = ShipCatalog.GetScene(shipId);
				if (prefab == null)
				{
					prefab = ResourceLoader.Load<PackedScene>(
						"res://Ships/BaseShip/ship_3d.tscn");
				}
				if (prefab == null)
				{
					continue;
				}
				component = prefab.Instantiate<ShipComponent>();
				_unitSpawner.AddChild(component);
				component.SetMeta("serverShipId", id);
				_remoteShips[id] = component;
				isNew = true;
			}

			seen.Add(id);
			component.BattleSide = side == mySide
				? GenerationSide.Player
				: GenerationSide.Enemy;
			Vector2I coords = new Vector2I(hex[0].GetInt32(), hex[1].GetInt32());
			component.AnimateTurnTo((HexDirection)(facing % 6));
			component.CurrentSpeed = speed;
			if (maxHp > 0)
			{
				component.MaxHp = maxHp;
				component.CurrentHp = Math.Clamp(hp, 0, maxHp);
			}
			LevelDataManager.BattlefieldUnits[coords] = component;
			pending.Add((id, component, coords, isNew, 0, 1));
		}

		var stale = new List<string>();
		foreach (string key in _remoteShips.Keys)
		{
			if (!seen.Contains(key))
			{
				stale.Add(key);
			}
		}
		foreach (string key in stale)
		{
			ShipComponent dead = _remoteShips[key];
			if (LevelDataManager.BattlefieldUnits.TryGetValue(dead.HexCoords, out ShipComponent current)
				&& current == dead)
			{
				LevelDataManager.BattlefieldUnits.Remove(dead.HexCoords);
			}
			dead.QueueFree();
			_remoteShips.Remove(key);
		}

		var stacks = new Dictionary<(Vector2I Hex, int Side), List<ShipComponent>>();
		foreach (var entry in pending)
		{
			if (!stacks.TryGetValue((entry.Hex, (int)entry.Ship.BattleSide), out var group))
			{
				group = new List<ShipComponent>();
				stacks[(entry.Hex, (int)entry.Ship.BattleSide)] = group;
			}
			group.Add(entry.Ship);
		}

		foreach (var kv in stacks)
		{
			Vector2I hex = kv.Key.Hex;
			List<ShipComponent> group = kv.Value;
			Vector3 center = _mapGenerator.HexToWorld(hex.X, hex.Y);
			Vector2I forwardOff = HexDirectionUtility.Offset(group[0].Direction);
			Vector3 forward = _mapGenerator.HexToWorld(forwardOff.X, forwardOff.Y)
				- _mapGenerator.HexToWorld(0, 0);
			forward.Y = 0f;
			Vector3 lateral = new Vector3(forward.Z, 0f, -forward.X);
			if (lateral.LengthSquared() > 0f)
			{
				lateral = lateral.Normalized();
			}

			for (int i = 0; i < group.Count; i++)
			{
				ShipComponent ship = group[i];
				float zOffset = group.Count <= 1
					? 0f
					: (i - (group.Count - 1) / 2f) * ShipComponent.StackZStep;
				Vector3 to = new Vector3(center.X, ShipComponent.StackBaseY, center.Z)
					+ lateral * zOffset;
				ship.HexCoords = hex;
				string serverId = ship.GetMeta("serverShipId", "").AsString();
				ShipComponent moved = ship;
				bool isNew = pending.Find(entry => entry.Ship == moved).IsNew;
				if (isNew)
				{
					moved.Position = to;
				}
				else
				{
					if (_remoteTweens.TryGetValue(serverId, out Tween oldTween))
					{
						oldTween.Kill();
						_remoteTweens.Remove(serverId);
					}
					Tween tween = moved.CreateTween();
					tween.SetTrans(Tween.TransitionType.Quad);
					tween.SetEase(Tween.EaseType.InOut);
					tween.TweenProperty(moved, "position", to, 0.35f);
					_remoteTweens[serverId] = tween;
					tween.Finished += () =>
					{
						if (_remoteTweens.TryGetValue(serverId, out Tween current)
							&& current == tween)
						{
							_remoteTweens.Remove(serverId);
						}
					};
				}
			}
		}

		_playerShips = _remoteShips.Values
			.Where(ship => ship.BattleSide == GenerationSide.Player)
			.ToList();
		_enemyShips = _remoteShips.Values
			.Where(ship => ship.BattleSide == GenerationSide.Enemy)
			.ToList();
		if (state.TryGetProperty("playerCommand", out JsonElement playerCommandProp))
		{
			PlayerCommandValue = playerCommandProp.GetInt32();
		}
		if (state.TryGetProperty("enemyCommand", out JsonElement enemyCommandProp))
		{
			EnemyCommandValue = enemyCommandProp.GetInt32();
		}
		if (state.TryGetProperty("playerMaxCP", out JsonElement playerMaxProp))
		{
			MaxCP = playerMaxProp.GetInt32();
		}
		if (state.TryGetProperty("enemyMaxCP", out JsonElement enemyMaxProp))
		{
			EnemyMaxCP = enemyMaxProp.GetInt32();
		}
		if (state.TryGetProperty("playerCP", out JsonElement playerCpProp))
		{
			CurrentCP = playerCpProp.GetInt32();
		}
		if (state.TryGetProperty("enemyCP", out JsonElement enemyCpProp))
		{
			EnemyCurrentCP = enemyCpProp.GetInt32();
		}
		if (state.TryGetProperty("playerScore", out JsonElement playerScoreProp))
		{
			PlayerScore = playerScoreProp.GetInt32();
		}
		if (state.TryGetProperty("enemyScore", out JsonElement enemyScoreProp))
		{
			EnemyScore = enemyScoreProp.GetInt32();
		}
		EmitCommandStateUpdated();
		foreach (JsonElement ship in ships.EnumerateArray())
		{
			string id = ship.TryGetProperty("id", out JsonElement idProp)
				? idProp.GetString() ?? ""
				: "";
			if (!_remoteShips.TryGetValue(id, out ShipComponent component))
			{
				continue;
			}
			string leadId = ship.TryGetProperty("formationLeadId", out JsonElement leadProp)
				&& leadProp.ValueKind == JsonValueKind.String
				? leadProp.GetString() ?? ""
				: "";
			int formationIndex = ship.TryGetProperty("formationIndex", out JsonElement indexProp)
				? indexProp.GetInt32()
				: -1;
			component.FormationLead =
				!string.IsNullOrEmpty(leadId) &&
				_remoteShips.TryGetValue(leadId, out ShipComponent lead)
					? lead
					: null;
			component.FormationIndex = formationIndex;
		}

		bool myTurn = state.TryGetProperty("activePlayer", out JsonElement activeProp) &&
			activeProp.GetString() == NetworkClient.Instance.UserId;
		string status = state.TryGetProperty("status", out JsonElement statusProp)
			? statusProp.GetString() ?? ""
			: "";
		if (status == "active" && myTurn && !_remotePhaseActive)
		{
			_remotePhaseActive = true;
			_remoteMyTurn = true;
			CallDeferred(nameof(BeginPlayerPhase));
		}
		else if (!myTurn || status != "active")
		{
			_remotePhaseActive = false;
			_remoteMyTurn = myTurn;
		}
		if (state.TryGetProperty("timerEndAt", out JsonElement timerEndAtProp) &&
			state.TryGetProperty("timerTotal", out JsonElement timerTotalProp))
		{
			_remoteTimerEndAt = timerEndAtProp.GetInt64();
			_remoteTimerTotal = timerTotalProp.GetInt32();
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
				+ NetworkClient.Instance.ServerTimeOffsetMs;
			_phaseTimerRemaining = Math.Max(0f, (_remoteTimerEndAt - nowMs) / 1000f);
			_phaseTimerTotal = _remoteTimerTotal;
			EmitPhaseTimerUpdated();
		}
		RefreshAdvanceButton();
	}

	private void RefreshAdvanceButton()
	{
		if (_advanceButton == null)
		{
			_advanceButton = GetNodeOrNull<Button>(
				"CanvasLayer/BattleUI/PhaseControlMargin/VBoxContainer/BtnPanel/EndTurnBtn");
		}
		if (_advanceButton != null)
		{
			_advanceButton.Disabled = !_remoteMyTurn || _remoteCommandsSent;
		}
	}

	private void SendRemoteCommands()
	{
		var intents = CommandIntentBuilder.Build(
			_playerShips.Where(IsShipAlive).ToList(),
			_currentPhase);
		var ships = new List<object>();
		foreach (ShipCommandIntent intent in intents)
		{
			ships.Add(intent.ToWire());
		}
		_remoteCommandsSent = true;
		NetworkClient.Instance.SendWsBattleShipsCommand(
			PvpFlowState.PendingBattleId,
			ships);
		GetNode<EventBus>("EventBus").EmitLog($"PvP 已提交 {ships.Count} 艘船指令");
	}

	private static BattlePhase RemotePhaseToLocal(string phase)
	{
		return phase switch
		{
			"speed" => BattlePhase.SpeedAdjust,
			"move1" => BattlePhase.MovePhase1,
			"move2" => BattlePhase.MovePhase2,
			"move3" => BattlePhase.MovePhase3,
			"recon" => BattlePhase.ReconLighting,
			"gunnery" => BattlePhase.Gunfire,
			"torpedo" => BattlePhase.Torpedo,
			_ => BattlePhase.SpeedAdjust,
		};
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
			$"—— 第 {_turnNumber} 回合 —— {PhaseLabels[(int)_currentPhase]}");

		// 启动首阶段
		if (CheckBattleEnd()) return;
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
		// 移动阶段保留贪吃蛇编队标记；只在速度调整阶段按几何关系重建。
		if (!_remotePvp && _currentPhase == BattlePhase.SpeedAdjust)
			MoveRulesEvaluator.SyncFormationGroups(_playerShips.Where(IsShipAlive).ToList());
		if (!_remotePvp)
		{
			StartPhaseTimer(true);
		}
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
		if (_currentPhase == BattlePhase.SpeedAdjust)
			MoveRulesEvaluator.SyncFormationGroups(aliveEnemies);
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
			CurrentCP = Math.Min(CurrentCP + PlayerCommandValue, MaxCP);
			EnemyCurrentCP = Math.Min(EnemyCurrentCP + EnemyCommandValue, EnemyMaxCP);
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
		EmitCommandStateUpdated();
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
			}
		}

		foreach (ShipCommandIntent intent in CommandIntentBuilder.Build(
			_playerShips.Where(IsShipAlive).ToList(),
			_currentPhase))
		{
			ShipComponent ship = intent.Ship;
			if (intent.Action == "accelerate" || intent.Action == "decelerate")
			{
				if (intent.TargetSpeed.HasValue && intent.TargetSpeed.Value != ship.CurrentSpeed)
				{
					int old = ship.CurrentSpeed;
					ship.CurrentSpeed = intent.TargetSpeed.Value;
					bus.EmitLog($"{ship.ShipName} 航速待命生效 {old} → {ship.CurrentSpeed}");
				}
			}
			else if (intent.Action == "turn_left" || intent.Action == "turn_right")
			{
				if (intent.TargetDirection.HasValue)
				{
					ship.AnimateTurnTo(intent.TargetDirection.Value);
					bus.EmitLog($"{ship.ShipName} 转向待命生效 → {ship.Direction}");
				}
			}
			else if (intent.Action == "fire" &&
				intent.Target != null &&
				GodotObject.IsInstanceValid(intent.Target))
			{
				ShipComponent target = intent.Target;
				int attackCost = CommandRulesEvaluator.FireCPCost(ship);
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
		}

		foreach (var ship in _playerShips)
		{
			if (IsShipAlive(ship))
			{
				ship.ClearPendingCommands();
			}
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
		_ => false
	};

	/// <summary>尝试消耗我方 CP。成功则返回 true 并刷新 HUD。</summary>
	public bool TryConsumeCP(int amount)
	{
		if (CurrentCP < amount) return false;
		CurrentCP -= amount;
		EmitCommandStateUpdated();
		return true;
	}

	/// <summary>增加我方 CP（不超过上限）。</summary>
	public void AddCP(int amount)
	{
		CurrentCP = Math.Min(CurrentCP + amount, MaxCP);
		EmitCommandStateUpdated();
	}

	/// <summary>尝试消耗敌方 CP。成功则返回 true 并刷新 HUD。</summary>
	public bool TryConsumeEnemyCP(int amount)
	{
		if (EnemyCurrentCP < amount) return false;
		EnemyCurrentCP -= amount;
		EmitCommandStateUpdated();
		return true;
	}

	/// <summary>增加敌方 CP（不超过上限）。</summary>
	public void AddEnemyCP(int amount)
	{
		EnemyCurrentCP = Math.Min(EnemyCurrentCP + amount, EnemyMaxCP);
		EmitCommandStateUpdated();
	}

	/// <summary>按舰船损伤重算双方指挥值、CP 上限与 PV 得分。</summary>
	public void RefreshCommandValues()
	{
		PlayerCommandValue = CommandRulesEvaluator.CommandValue(
			_playerShips, _dataManager?.PlayerCommand ?? 5);
		EnemyCommandValue = CommandRulesEvaluator.CommandValue(
			_enemyShips, _dataManager?.EnemyCommand ?? 4);
		MaxCP = Math.Max(1, PlayerCommandValue * 2);
		EnemyMaxCP = Math.Max(1, EnemyCommandValue * 2);
		CurrentCP = Math.Min(CurrentCP, MaxCP);
		EnemyCurrentCP = Math.Min(EnemyCurrentCP, EnemyMaxCP);
		RefreshScores();
		EmitCommandStateUpdated();
	}

	/// <summary>按对方舰船当前损伤状态计算 PV 得分。</summary>
	public void RefreshScores()
	{
		PlayerScore = VictoryRulesEvaluator.FleetScore(_enemyShips);
		EnemyScore = VictoryRulesEvaluator.FleetScore(_playerShips);
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
			RefreshStackOffsets();
			RefreshCommandValues();

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
		var occupiedShips = new Dictionary<Vector2I, List<ShipComponent>>();
		foreach (var ship in _playerShips.Concat(_enemyShips))
			if (IsShipAlive(ship))
				AddStackOccupant(occupiedShips, ship.HexCoords, ship);

		bool oddTurn = _turnNumber % 2 == 1;
		var ordered = new List<ShipComponent>();
		var chains = new List<List<ShipComponent>>();
		var processed = new HashSet<ShipComponent>();
		foreach (var side in new[] { GenerationSide.Player, GenerationSide.Enemy })
		{
			var sideShips = _playerShips.Concat(_enemyShips)
				.Where(s => IsShipAlive(s) && s.BattleSide == side)
				.ToList();
			foreach (var ship in sideShips)
			{
				if (processed.Contains(ship)) continue;
				if (ship.FormationLead != null && ReferenceEquals(ship.FormationLead, ship))
				{
					var chain = sideShips.Where(s => ReferenceEquals(s.FormationLead, ship))
						.OrderBy(s => s.FormationIndex)
						.ToList();
					if (chain.Count < 2)
					{
						ordered.Add(ship);
						processed.Add(ship);
						continue;
					}
					chains.Add(chain);
					foreach (var s in chain)
					{
						processed.Add(s);
						ordered.Add(s);
					}
				}
				else
				{
					ordered.Add(ship);
					processed.Add(ship);
				}
			}
		}

		for (int i = 0; i < ordered.Count; i++)
		{
			var ship = ordered[i];
			var chain = chains.FirstOrDefault(c => ReferenceEquals(c[0], ship));
			if (chain != null)
			{
				longest = Mathf.Max(longest,
					AnimateFormationChain(chain, phase, oddTurn, bus, occupiedShips));
				i += chain.Count - 1;
				continue;
			}
			longest = Mathf.Max(longest,
				AnimateStraightShip(ship, phase, oddTurn, bus, occupiedShips));
		}

		if (longest > 0f)
			await ToSignal(GetTree().CreateTimer(longest + 0.1f), "timeout");
		RefreshStackOffsets();
	}

	private float AnimateStraightShip(ShipComponent ship, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(ship.CurrentSpeed, phase, oddTurn);
		Vector2I off = HexDirectionUtility.Offset(ship.Direction);
		int steps = ResolveMoveSteps(ship, requestedSteps, off, bus, occupiedShips);
		if (!IsShipAlive(ship) || steps <= 0) return 0f;

		Vector2I target = ship.HexCoords + off * steps;
		RemoveStackOccupant(occupiedShips, ship.HexCoords, ship);
		AddStackOccupant(occupiedShips, target, ship);
		float duration = 0.35f + steps * 0.2f;
		ship.AnimateMoveTo(_mapGenerator, target, duration);
		return duration;
	}

	/// <summary>单纵阵按首舰轨迹推进：后船逐格消费首舰历史轨迹，到达每个转向格时立即转向。</summary>
	private float AnimateFormationChain(List<ShipComponent> chain, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		ShipComponent lead = chain[0];
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(lead.CurrentSpeed, phase, oddTurn);
		Vector2I off = HexDirectionUtility.Offset(lead.Direction);
		int steps = ResolveMoveSteps(lead, requestedSteps, off, bus, occupiedShips);
		if (!IsShipAlive(lead)) return 0f;

		FormationTrail trail = GetOrBuildFormationTrail(lead, chain);
		int leadIndex = trail.Cells.Count - 1;
		// 首舰转向在推进阶段已生效；即使本阶段不移动，也要更新当前格的轨迹航向。
		for (int i = 0; i < trail.Cells.Count; i++)
			if (trail.Cells[i] == lead.HexCoords)
				trail.Headings[i] = lead.Direction;
		if (steps <= 0) return 0f;

		for (int i = 0; i < steps; i++)
		{
			trail.Cells.Add(trail.Cells[^1] + off);
			trail.Headings.Add(lead.Direction);
		}

		var leadPath = trail.Cells.GetRange(leadIndex + 1, steps);
		var leadHeadings = trail.Headings.GetRange(leadIndex + 1, steps);
		RemoveStackOccupant(occupiedShips, lead.HexCoords, lead);
		AddStackOccupant(occupiedShips, trail.Cells[^1], lead);
		float perStep = 0.2f + 0.35f / Math.Max(1, steps);
		lead.AnimateMovePath(_mapGenerator, leadPath, perStep, leadHeadings);

		for (int k = 1; k < chain.Count; k++)
		{
			var follower = chain[k];
			if (!IsShipAlive(follower)) continue;
			int followerIndex = trail.Cells.LastIndexOf(follower.HexCoords);
			if (followerIndex < 0) continue;
			int followerSteps = Math.Min(steps, trail.Cells.Count - 1 - followerIndex);
			if (followerSteps <= 0) continue;
			var path = trail.Cells.GetRange(followerIndex + 1, followerSteps);
			var headings = trail.Headings.GetRange(followerIndex + 1, followerSteps);
			RemoveStackOccupant(occupiedShips, follower.HexCoords, follower);
			AddStackOccupant(occupiedShips, path[followerSteps - 1], follower);
			follower.AnimateMovePath(_mapGenerator, path, perStep, headings);
		}
		return 0.35f + steps * 0.2f;
	}

	private FormationTrail GetOrBuildFormationTrail(ShipComponent lead, List<ShipComponent> chain)
	{
		if (_formationTrails.TryGetValue(lead, out var trail)
			&& trail.Cells.Count > 0
			&& trail.Cells[^1] == lead.HexCoords
			&& trail.Members.SequenceEqual(chain))
			return trail;

		trail = new FormationTrail();
		for (int i = chain.Count - 1; i >= 0; i--)
		{
			var ship = chain[i];
			if (!IsShipAlive(ship)) continue;
			trail.Cells.Add(ship.HexCoords);
			trail.Headings.Add(ship.Direction);
		}
		trail.Members.AddRange(chain);
		_formationTrails[lead] = trail;
		return trail;
	}

	private int ResolveMoveSteps(ShipComponent ship, int requestedSteps, Vector2I off, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		int steps = MoveRulesEvaluator.AdvanceSteps(ship.HexCoords, ship.Direction, requestedSteps,
			hex => (_dataManager?.IsIsland(hex) ?? false)
				|| !CanStackEnter(ship, hex, occupiedShips));
		if (steps >= requestedSteps) return steps;

		Vector2I blockedHex = ship.HexCoords + off * (steps + 1);
		if (_dataManager?.IsIsland(blockedHex) ?? false)
		{
			bus.EmitLog($"🪨 {ship.ShipName} 撞击岛屿，直接沉没！");
			ship.TakeDamage(ship.CurrentHp);
			RefreshCommandValues();
			return 0;
		}
		if (occupiedShips.TryGetValue(blockedHex, out var blockers) && blockers.Count > 0)
		{
			var blocker = blockers[0];
			if (CollisionRulesEvaluator.IsCollision())
			{
				int hullSum = ship.MaxHp + blocker.MaxHp;
				var (rollA, dmgA) = CollisionRulesEvaluator.RollDamage(hullSum);
				var (rollB, dmgB) = CollisionRulesEvaluator.RollDamage(hullSum);
				bus.EmitLog($"💥 {ship.ShipName} 与 {blocker.ShipName} 发生冲撞！（{rollA}→{dmgA}，{rollB}→{dmgB}）");
				ship.TakeDamage(dmgA);
				blocker.TakeDamage(dmgB);
				RefreshCommandValues();
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
		return steps;
	}

	private static void AddStackOccupant(
		Dictionary<Vector2I, List<ShipComponent>> occupants, Vector2I hex, ShipComponent ship)
	{
		if (!occupants.TryGetValue(hex, out var list))
		{
			list = new List<ShipComponent>();
			occupants[hex] = list;
		}
		if (!list.Contains(ship)) list.Add(ship);
	}

	private static void RemoveStackOccupant(
		Dictionary<Vector2I, List<ShipComponent>> occupants, Vector2I hex, ShipComponent ship)
	{
		if (occupants.TryGetValue(hex, out var list))
		{
			list.Remove(ship);
			if (list.Count == 0) occupants.Remove(hex);
		}
	}

	/// <summary>同阵营单格最多 2 艘；敌舰占位或已满 2 艘时不可进入。</summary>
	private static bool CanStackEnter(ShipComponent ship, Vector2I hex,
		Dictionary<Vector2I, List<ShipComponent>> occupants)
	{
		if (!occupants.TryGetValue(hex, out var list) || list.Count == 0) return true;
		if (hex == ship.HexCoords) return true;
		if (list.Any(s => s.BattleSide != ship.BattleSide)) return false;
		return list.Count < 2;
	}

	/// <summary>按同格同阵营堆叠序号自动调整模型 y 高度。</summary>
	public void RefreshStackOffsets()
	{
		var groups = new Dictionary<Vector2I, List<ShipComponent>>();
		foreach (var ship in _playerShips.Concat(_enemyShips))
			if (IsShipAlive(ship))
				AddStackOccupant(groups, ship.HexCoords, ship);

		foreach (var group in groups)
			foreach (var sideGroup in group.Value.GroupBy(s => s.BattleSide))
			{
				var stacked = sideGroup.ToList();
				for (int i = 0; i < stacked.Count; i++)
				{
					var ship = stacked[i];
					Vector3 hexCenter = _mapGenerator.HexToWorld(ship.HexCoords.X, ship.HexCoords.Y);
					ship.ApplyStackOffset(i, stacked.Count, hexCenter, LateralAxisFor(ship));
				}
			}
	}

	private Vector3 LateralAxisFor(ShipComponent ship)
	{
		Vector2I off = HexDirectionUtility.Offset(ship.Direction);
		Vector3 forward = _mapGenerator.HexToWorld(off.X, off.Y)
			- _mapGenerator.HexToWorld(0, 0);
		forward.Y = 0f;
		if (forward.LengthSquared() < 0.0001f) return Vector3.Right;
		forward = forward.Normalized();
		return new Vector3(forward.Z, 0f, -forward.X);
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
		if (playerCount > 0 && enemyCount > 0)
		{
			if (_currentPhase == BattlePhase.EndTurn && _turnNumber >= _dataManager.MaxTurns)
			{
				_battleEnded = true;
				string scoreResult = PlayerScore > EnemyScore ? "胜利"
					: PlayerScore < EnemyScore ? "失败" : "平局";
				string scoreDetail = $"回合数已到，PV 我方 {PlayerScore} / 敌方 {EnemyScore}";
				var scoreBus = GetNode<EventBus>("EventBus");
				scoreBus.EmitLog($"🏁 {scoreResult}：{scoreDetail}");
				scoreBus.EmitSignal("BattleEnded", scoreResult, scoreDetail);
				return true;
			}
			return false;
		}

		_battleEnded = true;
		bool playerWon = playerCount > 0;
		string result = playerWon ? "胜利" : "失败";
		string detail = playerWon
			? $"敌方舰队已全灭（PV 我方 {PlayerScore} / 敌方 {EnemyScore}）"
			: $"我方舰队已全灭（PV 我方 {PlayerScore} / 敌方 {EnemyScore}）";
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

	private void EmitCommandStateUpdated() =>
		GetNode<EventBus>("EventBus").EmitSignal("CommandStateUpdated",
			PlayerCommandValue, CurrentCP, MaxCP,
			EnemyCommandValue, EnemyCurrentCP, EnemyMaxCP,
			PlayerScore, EnemyScore);
}

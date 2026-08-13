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
	private bool _remotePaused;
	private bool _storyPlaying;
	private bool _storyPausedTimerForPlayer = true;
	private long _remoteTimerEndAt;
	private int _remoteTimerTotal;
	private Button _advanceButton;
	private readonly Dictionary<string, ShipComponent> _remoteShips = new();
	private readonly Dictionary<string, TorpedoComponent> _remoteTorpedoes = new();
	private readonly Dictionary<string, Tween> _remoteTweens = new();
	private readonly Dictionary<ShipComponent, FormationTrail> _formationTrails = new();
	private readonly HashSet<Vector2I> _playerReachedHexes = new();
	private readonly HashSet<Vector2I> _enemyReachedHexes = new();
	private readonly Dictionary<string, int> _playerActionCounts = new();
	private readonly HashSet<ShipComponent> _countedSunk = new();
	private int _enemySunkCount;
	private int _playerSunkCount;

	/// <summary>单纵阵首舰历史轨迹：Cells[i] 对应到达后的航向 Headings[i]。</summary>
	private sealed class FormationTrail
	{
		public readonly List<Vector2I> Cells = new();
		public readonly List<HexDirection> Headings = new();
		public readonly List<ShipComponent> Members = new();
	}

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

	private void OnPvpConnectionChanged(bool connected)
	{
		var bus = GetNodeOrNull<EventBus>("EventBus");
		if (bus == null) return;
		if (connected)
		{
			bus.EmitLog("PvP 连接已建立，等待服务端状态...");
		}
		else
		{
			bus.EmitLog("PvP 连接断开，正在自动重连...");
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
				_overlay.BuildGlobalGrid();
				_overlay.BuildSpecialCellOverlays();
				StoryDirector.Instance?.SetMapName(_dataManager.CurrentMapName);
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
				if (code == "not_your_turn" &&
					!string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
				{
					_remoteCommandsSent = false;
					_remoteMyTurn = false;
					_remoteTimerEndAt = 0;
					NetworkClient.Instance.SendWsGetBattleState(PvpFlowState.PendingBattleId);
				}
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
			foreach (ShipComponent ship in _remoteShips.Values)
			{
				ship.TurnedThisPhase = false;
			}
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
			int mainAmmo = ship.TryGetProperty("mainAmmo", out JsonElement mainAmmoProp)
				? mainAmmoProp.GetInt32()
				: -1;
			int torpedoLeft = ship.TryGetProperty("torpedoLeftRemaining", out JsonElement torpedoLeftProp)
				? torpedoLeftProp.GetInt32()
				: -1;
			int torpedoCenter = ship.TryGetProperty("torpedoCenterRemaining", out JsonElement torpedoCenterProp)
				? torpedoCenterProp.GetInt32()
				: -1;
			int torpedoRight = ship.TryGetProperty("torpedoRightRemaining", out JsonElement torpedoRightProp)
				? torpedoRightProp.GetInt32()
				: -1;
			int torpedoReloads = ship.TryGetProperty("torpedoReloadsRemaining", out JsonElement reloadProp)
				? reloadProp.GetInt32()
				: -1;
			int stackIndex = ship.TryGetProperty("stackIndex", out JsonElement stackIndexProp)
				? stackIndexProp.GetInt32()
				: 0;
			int stackTotal = ship.TryGetProperty("stackTotal", out JsonElement stackTotalProp)
				? stackTotalProp.GetInt32()
				: 1;

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
				ShipCatalog.Entry entry = ShipCatalog.Get(shipId);
				if (entry?.Data != null)
				{
					component.ApplyData(entry.Data);
				}
				_remoteShips[id] = component;
				isNew = true;
			}

			seen.Add(id);
			component.BattleSide = side == mySide
				? GenerationSide.Player
				: GenerationSide.Enemy;
			Vector2I coords = new Vector2I(hex[0].GetInt32(), hex[1].GetInt32());
			int previousFacing = (int)component.Direction;
			if (isNew)
			{
				component.Direction = (HexDirection)(facing % 6);
				component.TurnedThisPhase = false;
			}
			else if (previousFacing != facing % 6)
			{
				component.AnimateTurnTo((HexDirection)(facing % 6));
			}
			component.CurrentSpeed = speed;
			if (maxHp > 0)
			{
				component.MaxHp = maxHp;
				component.CurrentHp = Math.Clamp(hp, 0, maxHp);
			}
			if (mainAmmo >= 0)
			{
				component.MainAmmo = mainAmmo;
			}
			if (torpedoLeft >= 0) component.TorpedoLeftRemaining = torpedoLeft;
			if (torpedoCenter >= 0) component.TorpedoCenterRemaining = torpedoCenter;
			if (torpedoRight >= 0) component.TorpedoRightRemaining = torpedoRight;
			if (torpedoReloads >= 0) component.TorpedoReloadsRemaining = torpedoReloads;
			component.TorpedoFiredThisTurn =
				ship.TryGetProperty("torpedoFiredThisTurn", out JsonElement firedProp)
					&& firedProp.ValueKind == JsonValueKind.True;
			component.TorpedoFiredLastTurn =
				ship.TryGetProperty("torpedoFiredLastTurn", out JsonElement firedLastProp)
					&& firedLastProp.ValueKind == JsonValueKind.True;
			LevelDataManager.BattlefieldUnits[coords] = component;
			pending.Add((id, component, coords, isNew, stackIndex, stackTotal));
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
		foreach (var group in stacks.Values)
		{
			group.Sort((a, b) =>
				pending.Find(entry => entry.Ship == a).StackIndex
					.CompareTo(pending.Find(entry => entry.Ship == b).StackIndex));
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
		if (state.TryGetProperty("torpedoes", out JsonElement torpedoes))
		{
			SyncRemoteTorpedoes(torpedoes);
		}
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
		RefreshDirectionOverlays();

		bool myTurn = state.TryGetProperty("activePlayer", out JsonElement activeProp) &&
			activeProp.GetString() == NetworkClient.Instance.UserId;
		string status = state.TryGetProperty("status", out JsonElement statusProp)
			? statusProp.GetString() ?? ""
			: "";
		bool paused = state.TryGetProperty("paused", out JsonElement pausedProp) &&
			pausedProp.ValueKind == JsonValueKind.True;
		if (paused != _remotePaused)
		{
			_remotePaused = paused;
			_remoteTimerEndAt = 0;
			_remoteTimerTotal = 0;
			_remoteMyTurn = false;
			_remotePhaseActive = false;
			bus.EmitLog(paused ? "PvP 对局暂停：对手断线" : "PvP 对局恢复：对手已重连");
			EmitPhaseTimerUpdated();
			RefreshAdvanceButton();
		}
		myTurn = myTurn && !paused;
		if (status == "active" && myTurn && !_remotePhaseActive)
		{
			_remotePhaseActive = true;
			_remoteMyTurn = true;
			if (_currentPhase == BattlePhase.ReconLighting)
			{
				if (!_remoteCommandsSent)
				{
					SendRemoteCommands();
				}
			}
			else
			{
				CallDeferred(nameof(BeginPlayerPhase));
			}
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
		if (status != "active" && !_battleEnded)
		{
			_battleEnded = true;
			string winner = state.TryGetProperty("winner", out JsonElement winnerProp) &&
				winnerProp.ValueKind == JsonValueKind.String
				? winnerProp.GetString() ?? ""
				: "";
			string result = string.IsNullOrEmpty(winner)
				? "平局"
				: winner == NetworkClient.Instance.UserId
					? "胜利"
					: "失败";
			GetNode<EventBus>("EventBus").EmitSignal(
				"BattleEnded",
				result,
				$"回合 {turn} · {phase}");
		}
		RefreshAdvanceButton();
	}

	private void SyncRemoteTorpedoes(JsonElement torpedoes)
	{
		if (_torpedoController == null || _mapGenerator == null) return;
		var seen = new HashSet<string>();
		foreach (JsonElement entry in torpedoes.EnumerateArray())
		{
			string id = entry.TryGetProperty("id", out JsonElement idProp)
				? idProp.GetString() ?? ""
				: "";
			if (string.IsNullOrEmpty(id)) continue;
			JsonElement hex = entry.TryGetProperty("hex", out JsonElement hexProp)
				? hexProp
				: default;
			if (hex.ValueKind != JsonValueKind.Array || hex.GetArrayLength() < 2) continue;
			int side = entry.TryGetProperty("side", out JsonElement sideProp)
				? sideProp.GetInt32()
				: 0;
			int direction = entry.TryGetProperty("direction", out JsonElement dirProp)
				? dirProp.GetInt32()
				: 0;
			int speed = entry.TryGetProperty("speed", out JsonElement speedProp)
				? speedProp.GetInt32()
				: 6;
			int range = entry.TryGetProperty("remainingRange", out JsonElement rangeProp)
				? rangeProp.GetInt32()
				: 4;
			int count = entry.TryGetProperty("count", out JsonElement countProp)
				? countProp.GetInt32()
				: 1;
			int hitMode = entry.TryGetProperty("hitMode", out JsonElement modeProp)
				? modeProp.GetInt32()
				: 7;
			int damage = entry.TryGetProperty("torpedoDamage", out JsonElement damageProp)
				? damageProp.GetInt32()
				: 30;
			string type = entry.TryGetProperty("torpedoType", out JsonElement typeProp)
				? typeProp.GetString() ?? ""
				: "鱼雷";
			int fanSide = entry.TryGetProperty("fanSide", out JsonElement fanSideProp)
				? fanSideProp.GetInt32()
				: -1;
			int fanBranch = entry.TryGetProperty("fanBranch", out JsonElement fanBranchProp)
				? fanBranchProp.GetInt32()
				: 0;
			Vector2I coords = new Vector2I(hex[0].GetInt32(), hex[1].GetInt32());
			seen.Add(id);

			if (!_remoteTorpedoes.TryGetValue(id, out TorpedoComponent torpedo))
			{
				torpedo = _torpedoController.SpawnTorpedo(
					id, side, coords, (HexDirection)(direction % 6),
					speed, range, count, hitMode, damage, type, null,
					fanSide, fanBranch, _mapGenerator);
				if (torpedo == null) continue;
				_remoteTorpedoes[id] = torpedo;
			}
			else
			{
				if (torpedo.Hex != coords)
				{
					torpedo.AnimateMoveTo(_mapGenerator, coords, 0.3f);
				}
				torpedo.Direction = (HexDirection)(direction % 6);
				torpedo.RemainingRange = range;
				torpedo.RangeSpent = entry.TryGetProperty("rangeSpent", out JsonElement spentProp)
					? spentProp.GetInt32()
					: torpedo.RangeSpent;
				torpedo.Count = count;
				torpedo.FanSide = fanSide;
				torpedo.FanBranch = fanBranch;
				torpedo.ApplyVisual();
			}
		}

		var stale = new List<string>();
		foreach (string id in _remoteTorpedoes.Keys)
		{
			if (!seen.Contains(id))
			{
				stale.Add(id);
			}
		}
		foreach (string id in stale)
		{
			if (_remoteTorpedoes.Remove(id, out TorpedoComponent torpedo))
			{
				_torpedoController.RemoveTorpedo(torpedo);
			}
		}
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
			$"—— 第 {_turnNumber} 回合 —— {BattlePhaseMachine.PhaseLabels[(int)_currentPhase]}");
		StoryDirector.Instance?.SetMapName(_dataManager.CurrentMapName);
		GetNode<EventBus>("EventBus").EmitSignal("BattleStarted");

		// 启动首阶段
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
		RefreshDirectionOverlays();
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

		bus.EmitLog($"敌方行动：{BattlePhaseMachine.PhaseLabels[(int)_currentPhase]}");
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

		BattlePhaseMachine.Transition transition = BattlePhaseMachine.Plan(
			_currentPhase,
			_dataManager?.IsNightBattle ?? false,
			_dataManager.GunfirePhaseEnabled,
			_dataManager.TorpedoPhaseEnabled);
		_currentPhase = transition.Next;

		if (transition.SkipLighting)
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "白天地图，跳过视野/照明阶段");
		if (transition.SkipGunfire)
			GetNode<EventBus>("EventBus").EmitSignal("LogMessage", "炮击阶段未启用，跳过");
		if (transition.SkipTorpedo)
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
					if (_currentPhase == BattlePhase.SpeedAdjust)
					{
						ship.AnimateTurnTo(intent.TargetDirection.Value);
						bus.EmitLog($"{ship.ShipName} 转向待命生效 → {ship.Direction}");
					}
					else
					{
						// 移动阶段先沿原航向行进，阶段结束时再执行转向。
						bus.EmitLog($"{ship.ShipName} 转向待命（先行进后转向 → {intent.TargetDirection.Value}）");
					}
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
			else if (intent.Action == "torpedo" && intent.TorpedoSide != 0)
			{
				LaunchTorpedo(ship, intent.TorpedoSide, false, intent.TorpedoBranch);
			}
		}

		bool deferTurn = _currentPhase is BattlePhase.MovePhase1
			or BattlePhase.MovePhase2 or BattlePhase.MovePhase3;
		foreach (var ship in _playerShips)
		{
			if (IsShipAlive(ship))
			{
				ship.ClearPendingCommands(deferTurn);
			}
		}
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

	/// <summary>发射一枚鱼雷齐射：校验 CP/资格、消耗鱼雷并生成占位实体。</summary>
	public bool LaunchTorpedo(ShipComponent ship, int side, bool enemySide = false,
		int branch = 0)
	{
		var bus = GetNode<EventBus>("EventBus");
		if (ship == null || !TorpedoRulesEvaluator.CanLaunch(ship, side))
		{
			bus.EmitLog($"{ship?.ShipName} 雷击未执行（无鱼雷管、状态不允许或本回合已雷击）");
			return false;
		}
		bool second = enemySide ? !IsPlayerSecondTurn : IsPlayerSecondTurn;
		int cost = CommandRulesEvaluator.TorpedoCPCost(ship, second);
		bool consumed = enemySide ? TryConsumeEnemyCP(cost) : TryConsumeCP(cost);
		if (!consumed)
		{
			bus.EmitLog($"{ship.ShipName} 雷击被拒绝（需要 {cost} CP）");
			return false;
		}

		int count = ship.ConsumeTorpedoSalvo(side);
		if (count <= 0)
		{
			bus.EmitLog($"{ship.ShipName} 该侧没有剩余鱼雷");
			return false;
		}
		int hitMode = enemySide
			? _dataManager?.TorpedoModeEnemy ?? 4
			: _dataManager?.TorpedoModePlayer ?? 7;
		TorpedoComponent torpedo = _torpedoController.SpawnTorpedo(
			$"t_{ship.ShipName}_{GD.Randi()}",
			(int)ship.BattleSide,
			ship.HexCoords,
			ship.Direction,
			ship.Data?.TorpedoSpeed ?? 6,
			ship.Data?.TorpedoRange ?? 4,
			count,
			hitMode,
			ship.Data?.TorpedoDamage ?? 30,
			ship.Data?.TorpedoType ?? "",
			ship,
			side,
			branch,
			_mapGenerator);
		if (torpedo != null)
		{
			bus.EmitLog($"💣 {ship.ShipName} 向{(side < 0 ? "左舷" : "右舷")}" +
				$"{(branch == 0 ? "·正" : "·斜")}发射 {count} 发鱼雷（{cost} CP）");
		}
		return torpedo != null;
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

			var hitEvents = _playerShips.Concat(_enemyShips)
				.SelectMany(ship => ship.PendingHitEvents)
				.Where(ev => ev != null
					&& GodotObject.IsInstanceValid(ev.Attacker)
					&& GodotObject.IsInstanceValid(ev.Target))
				.ToList();
			var playerAttacks = hitEvents
				.Where(ev => ev.Attacker.BattleSide == GenerationSide.Player)
				.ToList();
			var enemyAttacks = hitEvents
				.Where(ev => ev.Attacker.BattleSide == GenerationSide.Enemy)
				.ToList();
			GD.Print($"结算演绎：我方 {playerAttacks.Count} 条 / 敌方 {enemyAttacks.Count} 条");
			bool playerFirst = _dataManager?.InitiativeOwner != "enemy";
			var replayOrder = playerFirst
				? playerAttacks.Concat(enemyAttacks)
				: enemyAttacks.Concat(playerAttacks);

			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (GodotObject.IsInstanceValid(ship) && ship.PendingDamage > 0)
					ship.ApplyPendingDamage();
			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (GodotObject.IsInstanceValid(ship)
					&& ship.DamageState == DamageState.Sunk
					&& _countedSunk.Add(ship))
				{
					if (ship.BattleSide == GenerationSide.Player)
					{
						_playerSunkCount++;
					}
					else
					{
						_enemySunkCount++;
					}
				}
			RefreshStackOffsets();
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

			foreach (var ship in _playerShips.Concat(_enemyShips))
				if (GodotObject.IsInstanceValid(ship))
				{
					ship.PendingShotChecks.Clear();
					ship.PendingHitEvents.Clear();
				}

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
		ApplyPendingTurnsAfterMovement();
		RefreshStackOffsets();
		RefreshDirectionOverlays();
		await _torpedoController.MoveTorpedoesAsync(_mapGenerator, _dataManager,
			phase, oddTurn, _playerShips.Concat(_enemyShips).ToList());
		foreach (var ship in _playerShips.Concat(_enemyShips))
			if (IsShipAlive(ship)
				&& _dataManager?.SpecialTiles.TryGetValue(ship.HexCoords, out int specialId) == true)
			{
				if (ship.BattleSide == GenerationSide.Player)
				{
					_playerReachedHexes.Add(ship.HexCoords);
				}
				else
				{
					_enemyReachedHexes.Add(ship.HexCoords);
				}
				bus.EmitSignal("SpecialCellEntered", ship.HexCoords, specialId);
			}
	}

	/// <summary>移动阶段结束后执行待命转向：先沿原航向移动，再转向。</summary>
	private void ApplyPendingTurnsAfterMovement()
	{
		foreach (var ship in _playerShips.Concat(_enemyShips))
		{
			if (IsShipAlive(ship) && ship.PendingDirection.HasValue)
			{
				HexDirection target = ship.PendingDirection.Value;
				ship.AnimateTurnTo(target);
				ship.PendingDirection = null;
			}
		}
	}

	private float AnimateStraightShip(ShipComponent ship, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(ship.CurrentSpeed, phase, oddTurn);
		var path = MoveRulesEvaluator.BuildMovePath(ship.HexCoords, ship.Direction, requestedSteps);
		var moved = ResolveMovePath(ship, path, bus, occupiedShips);
		if (moved.Count <= 0) return 0f;

		Vector2I target = moved[^1].Hex;
		RemoveStackOccupant(occupiedShips, ship.HexCoords, ship);
		AddStackOccupant(occupiedShips, target, ship);
		if (!IsShipAlive(ship))
		{
			ship.HexCoords = target;
			return 0f;
		}

		float perStep = 0.2f + 0.35f / Math.Max(1, moved.Count);
		ship.AnimateMovePath(
			_mapGenerator,
			moved.Select(step => step.Hex).ToList(),
			perStep,
			moved.Select(step => step.Heading).ToList());
		return perStep * moved.Count;
	}

	/// <summary>单纵阵按首舰轨迹推进：后船逐格消费首舰历史轨迹，到达每个转向格时立即转向。</summary>
	private float AnimateFormationChain(List<ShipComponent> chain, int phase, bool oddTurn, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		ShipComponent lead = chain[0];
		int requestedSteps = MoveRulesEvaluator.MovementForPhase(lead.CurrentSpeed, phase, oddTurn);
		var plannedPath = MoveRulesEvaluator.BuildMovePath(lead.HexCoords, lead.Direction, requestedSteps);
		var moved = ResolveMovePath(lead, plannedPath, bus, occupiedShips);
		int steps = moved.Count;

		FormationTrail trail = GetOrBuildFormationTrail(lead, chain);
		int leadIndex = trail.Cells.Count - 1;
		// 先走再转：首舰本阶段先沿原航向移动，结束时转向；轨迹格立即记录转向后的航向。
		HexDirection leadAfterTurn = lead.PendingDirection ?? lead.Direction;
		for (int i = 0; i < trail.Cells.Count; i++)
			if (trail.Cells[i] == lead.HexCoords)
				trail.Headings[i] = leadAfterTurn;
		if (steps <= 0) return 0f;

		for (int i = 0; i < steps; i++)
		{
			trail.Cells.Add(moved[i].Hex);
			trail.Headings.Add(moved[i].Heading);
		}

		var leadPath = trail.Cells.GetRange(leadIndex + 1, steps);
		var leadHeadings = trail.Headings.GetRange(leadIndex + 1, steps);
		RemoveStackOccupant(occupiedShips, lead.HexCoords, lead);
		AddStackOccupant(occupiedShips, trail.Cells[^1], lead);
		float perStep = 0.2f + 0.35f / Math.Max(1, steps);
		if (IsShipAlive(lead))
			lead.AnimateMovePath(_mapGenerator, leadPath, perStep, leadHeadings);
		else
			lead.HexCoords = trail.Cells[^1];

		for (int k = 1; k < chain.Count; k++)
		{
			var follower = chain[k];
			if (!IsShipAlive(follower)) continue;
			int followerIndex = trail.Cells.LastIndexOf(follower.HexCoords);
			if (followerIndex < 0) continue;
			int followerSteps = Math.Min(steps, trail.Cells.Count - 1 - followerIndex);
			if (followerSteps <= 0) continue;
			var followerPath = trail.Cells.GetRange(followerIndex + 1, followerSteps);
			var headings = trail.Headings.GetRange(followerIndex + 1, followerSteps);
			RemoveStackOccupant(occupiedShips, follower.HexCoords, follower);
			AddStackOccupant(occupiedShips, followerPath[followerSteps - 1], follower);
			follower.AnimateMovePath(_mapGenerator, followerPath, perStep, headings);
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

	private List<MoveRulesEvaluator.MovementStep> ResolveMovePath(ShipComponent ship,
		IReadOnlyList<MoveRulesEvaluator.MovementStep> path, EventBus bus,
		Dictionary<Vector2I, List<ShipComponent>> occupiedShips)
	{
		var moved = new List<MoveRulesEvaluator.MovementStep>();
		int index = 0;
		for (; index < path.Count; index++)
		{
			Vector2I next = path[index].Hex;
			if (_dataManager?.IsIsland(next) ?? false)
			{
				bus.EmitLog($"🪨 {ship.ShipName} 撞击岛屿，直接沉没！");
				ship.TakeDamage(ship.CurrentHp);
				RefreshCommandValues();
				return moved;
			}
			if (!CanStackEnter(ship, next, occupiedShips))
			{
				break;
			}
			moved.Add(path[index]);
		}

		if (index >= path.Count) return moved;
		Vector2I blockedHex = path[index].Hex;
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
				bus.EmitLog($"⚠️ {ship.ShipName} 前方有舰船但未发生冲撞，停在 {moved.Count} 格前");
			}
		}
		else
		{
			bus.EmitLog($"⚠️ {ship.ShipName} 前方受阻，仅推进 {moved.Count} 格");
		}
		return moved;
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

	/// <summary>刷新方向标记：只标记单纵阵头与独行舰，跟随舰不显示。</summary>
	private void RefreshDirectionOverlays()
	{
		if (_overlay == null) return;
		var entries = _playerShips.Concat(_enemyShips)
			.Where(IsShipAlive)
			.Where(ship => ship.FormationLead == null || ReferenceEquals(ship.FormationLead, ship))
			.Select(ship => (ship.HexCoords, ship.Direction))
			.ToList();
		_overlay.RefreshDirections(entries);
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
	public bool IsPlayerSecondTurn => _dataManager?.InitiativeOwner == "enemy";
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

	private static bool IsShipAlive(ShipComponent ship)
		=> GodotObject.IsInstanceValid(ship) && ship.CurrentHp > 0;

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
			BattlePhaseMachine.PhaseLabels[(int)_currentPhase], (int)_currentPhase, _turnNumber);

	private void EmitCommandStateUpdated() =>
		GetNode<EventBus>("EventBus").EmitSignal("CommandStateUpdated",
			PlayerCommandValue, CurrentCP, MaxCP,
			EnemyCommandValue, EnemyCurrentCP, EnemyMaxCP,
			PlayerScore, EnemyScore);
}

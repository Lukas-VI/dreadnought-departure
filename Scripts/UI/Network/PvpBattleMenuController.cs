using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using DreadnoughtDeparture.Core;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>PvP 战斗指令面板：展示权威状态，按阶段提交指令并查看广播日志。</summary>
public partial class PvpBattleMenuController : Control
{
	[Export] public string LobbyMenuPath = "res://Scenes/UI/Network/lobby_menu.tscn";
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private Label _battleIdLabel;
	private Label _roomIdLabel;
	private Label _phaseLabel;
	private Label _initiativeLabel;
	private Label _shipsLabel;
	private Label _wsStatusLabel;
	private Label _commandStatusLabel;
	private VBoxContainer _commandBox;
	private RichTextLabel _log;
	private Label _3dHint;
	private SubViewportContainer _viewportContainer;
	private SubViewport _viewport;
	private Node3D _world;
	private LevelDataManager _levelData;
	private MapGenerator _mapGenerator;
	private Node3D _shipsRoot;
	private readonly Dictionary<string, ShipComponent> _ships3D = new();
	private static readonly HexDirection[] ServerFacingToHexDirection =
	{
		HexDirection.SE,
		HexDirection.NE,
		HexDirection.N,
		HexDirection.NW,
		HexDirection.SW,
		HexDirection.S,
	};
	private bool _3dReady;
	private string _lastPhase = "";
	private int _lastTurn = -1;

	public override void _Ready()
	{
		BuildUi();
		Setup3DView();
		NetworkClient.Instance.WsMessageReceived += OnWsMessage;
		NetworkClient.Instance.ConnectionStateChanged += OnConnectionChanged;
		NetworkClient.Instance.WsClosed += OnWsClosed;
		NetworkClient.Instance.ConnectWebSocket();
		OnConnectionChanged(NetworkClient.Instance.IsWebSocketConnected);

		if (!string.IsNullOrEmpty(PvpFlowState.PendingRoomId))
		{
			NetworkClient.Instance.SendWsJoinRoom(PvpFlowState.PendingRoomId);
		}
		if (!string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
		{
			NetworkClient.Instance.SendWsGetBattleState(PvpFlowState.PendingBattleId);
		}

		_battleIdLabel.Text = $"战斗：{PvpFlowState.PendingBattleId}";
		_roomIdLabel.Text = $"房间：{PvpFlowState.PendingRoomId}";
		AppendLog("已进入战斗同步界面，等待权威状态");
	}

	public override void _ExitTree()
	{
		if (NetworkClient.Instance != null)
		{
			NetworkClient.Instance.WsMessageReceived -= OnWsMessage;
			NetworkClient.Instance.ConnectionStateChanged -= OnConnectionChanged;
			NetworkClient.Instance.WsClosed -= OnWsClosed;
		}
	}

	private void BuildUi()
	{
		var backdrop = new ColorRect
		{
			Color = new Color(0.04f, 0.06f, 0.1f, 1f),
			MouseFilter = MouseFilterEnum.Ignore,
		};
		backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(backdrop);

		var topBar = new HBoxContainer();
		topBar.SetAnchorsPreset(LayoutPreset.TopWide);
		topBar.OffsetTop = 18;
		topBar.OffsetBottom = 66;
		topBar.AddThemeConstantOverride("separation", 18);
		AddChild(topBar);

		var title = new Label { Text = "PvP 战斗" };
		title.AddThemeFontSizeOverride("font_size", 26);
		topBar.AddChild(title);

		_wsStatusLabel = new Label { Text = "未连接", Modulate = new Color(1f, 0.8f, 0.3f) };
		topBar.AddChild(_wsStatusLabel);

		var topSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		topBar.AddChild(topSpacer);
		topBar.AddChild(MakeButton("返回大厅", () => GetTree().ChangeSceneToFile(LobbyMenuPath)));
		topBar.AddChild(MakeButton("退出登录", () =>
		{
			NetworkClient.Instance.Logout();
			GetTree().ChangeSceneToFile(MainMenuPath);
		}));

		var body = new HBoxContainer();
		body.SetAnchorsPreset(LayoutPreset.FullRect);
		body.OffsetTop = 80;
		body.OffsetBottom = -20;
		body.AddThemeConstantOverride("separation", 18);
		AddChild(body);

		var infoPanel = new PanelContainer { CustomMinimumSize = new Vector2(520, 0) };
		body.AddChild(infoPanel);
		var infoBox = new VBoxContainer();
		infoBox.AddThemeConstantOverride("separation", 10);
		infoPanel.AddChild(infoBox);

		_battleIdLabel = new Label { Text = "战斗：-" };
		_battleIdLabel.AddThemeFontSizeOverride("font_size", 20);
		infoBox.AddChild(_battleIdLabel);

		_roomIdLabel = new Label { Text = "房间：-" };
		infoBox.AddChild(_roomIdLabel);

		_phaseLabel = new Label { Text = "阶段：- / 回合 -" };
		_phaseLabel.AddThemeFontSizeOverride("font_size", 18);
		infoBox.AddChild(_phaseLabel);

		_initiativeLabel = new Label { Text = "先手：-" };
		infoBox.AddChild(_initiativeLabel);

		_commandStatusLabel = new Label { Text = "指令：未提交" };
		infoBox.AddChild(_commandStatusLabel);

		_shipsLabel = new Label
		{
			Text = "舰船：-",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		infoBox.AddChild(_shipsLabel);

		_commandBox = new VBoxContainer();
		_commandBox.AddThemeConstantOverride("separation", 8);
		infoBox.AddChild(_commandBox);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 8);
		row.AddChild(MakeButton("重连", () => NetworkClient.Instance.ReconnectWebSocket()));
		row.AddChild(MakeButton("调试骰值 3d100", SendDebugRoll));
		infoBox.AddChild(row);

		var rightPanel = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		rightPanel.AddThemeConstantOverride("separation", 8);
		body.AddChild(rightPanel);

		_3dHint = new Label { Text = "3D 战场：等待地图..." };
		rightPanel.AddChild(_3dHint);

		_viewportContainer = new SubViewportContainer
		{
			Stretch = true,
			CustomMinimumSize = new Vector2(640, 360),
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		rightPanel.AddChild(_viewportContainer);

		_viewport = new SubViewport
		{
			Size = new Vector2I(800, 600),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			OwnWorld3D = true,
		};
		_viewportContainer.AddChild(_viewport);

		_world = new Node3D { Name = "PvpWorld" };
		_viewport.AddChild(_world);

		var logPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 220) };
		rightPanel.AddChild(logPanel);
		var logBox = new VBoxContainer();
		logBox.AddThemeConstantOverride("separation", 8);
		logPanel.AddChild(logBox);

		var logTitle = new Label { Text = "同步日志" };
		logTitle.AddThemeFontSizeOverride("font_size", 20);
		logBox.AddChild(logTitle);

		_log = new RichTextLabel
		{
			BbcodeEnabled = false,
			ScrollFollowing = true,
			SizeFlagsVertical = SizeFlags.ExpandFill,
		};
		logBox.AddChild(_log);
	}

	private void SendDebugRoll()
	{
		if (!NetworkClient.Instance.IsWebSocketConnected)
		{
			AppendLog($"未连接，无法投掷（{NetworkClient.Instance.LastWsError}）");
			NetworkClient.Instance.ReconnectWebSocket();
			return;
		}
		if (string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
		{
			AppendLog("没有可用的 battleId");
			return;
		}

		NetworkClient.Instance.SendWsBattleRoll(
			PvpFlowState.PendingBattleId,
			3,
			100,
			"client-test");
	}

	private void OnWsMessage(string json)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			JsonElement root = document.RootElement;
			string type = root.TryGetProperty("type", out JsonElement typeProp)
				? typeProp.GetString() ?? ""
				: "";

			if (type == "room.state" && root.TryGetProperty("room", out JsonElement room))
			{
				string roomId = room.TryGetProperty("id", out JsonElement roomIdProp)
					? roomIdProp.GetString() ?? ""
					: "";
				AppendLog($"[{DateTime.Now:HH:mm:ss}] 已订阅房间 {roomId}");
			}
			else if (type == "battle.state" && root.TryGetProperty("state", out JsonElement state))
			{
				ApplyState(state);
			}
			else if (type == "battle.rolled" && root.TryGetProperty("roll", out JsonElement roll))
			{
				string battleId = root.TryGetProperty("battleId", out JsonElement battleProp)
					? battleProp.GetString() ?? ""
					: "";
				string values = roll.TryGetProperty("values", out JsonElement valuesProp)
					? valuesProp.ToString()
					: "[]";
				string sides = roll.TryGetProperty("sides", out JsonElement sidesProp)
					? sidesProp.ToString()
					: "?";
				AppendLog($"[{DateTime.Now:HH:mm:ss}] battle {battleId} 3d{sides} -> {values}");
			}
			else if (type == "error")
			{
				string code = root.TryGetProperty("code", out JsonElement codeProp)
					? codeProp.GetString() ?? ""
					: "";
				AppendLog($"[{DateTime.Now:HH:mm:ss}] 服务端错误：{code}");
			}
			else if (type == "battle.started" && root.TryGetProperty("battle", out JsonElement battle))
			{
				if (battle.TryGetProperty("id", out JsonElement id))
				{
					PvpFlowState.PendingBattleId = id.GetString() ?? "";
					_battleIdLabel.Text = $"战斗：{PvpFlowState.PendingBattleId}";
					NetworkClient.Instance.SendWsGetBattleState(PvpFlowState.PendingBattleId);
				}
				AppendLog("战斗开始广播已收到");
			}
		}
		catch (Exception ex)
		{
			AppendLog($"解析失败：{ex.Message}");
		}
	}

	private void ApplyState(JsonElement state)
	{
		int turn = state.TryGetProperty("turn", out JsonElement turnProp)
			? turnProp.GetInt32()
			: 0;
		string phase = state.TryGetProperty("phase", out JsonElement phaseProp)
			? phaseProp.GetString() ?? ""
			: "";
		string status = state.TryGetProperty("status", out JsonElement statusProp)
			? statusProp.GetString() ?? ""
			: "";

		_phaseLabel.Text = $"阶段：{phase} / 回合 {turn} / {status}";
		if (state.TryGetProperty("turnOrder", out JsonElement turnOrder) &&
			turnOrder.GetArrayLength() >= 2)
		{
			string firstId = turnOrder[0].GetString() ?? "";
			_initiativeLabel.Text = firstId == NetworkClient.Instance.UserId
				? "先手：我方"
				: "先手：敌方";
		}
		if (turn != _lastTurn || phase != _lastPhase)
		{
			_lastTurn = turn;
			_lastPhase = phase;
			AppendLog($"[{DateTime.Now:HH:mm:ss}] 回合 {turn} · 阶段 {phase}");
		}

		UpdateShips(state);
		UpdateCommandStatus(state);
		UpdateShips3D(state);
		BuildCommandButtons(state, status);
	}

	private void Setup3DView()
	{
		if (_3dReady)
		{
			return;
		}
		if (string.IsNullOrEmpty(PvpMapState.MapJson))
		{
			_3dHint.Text = "3D 战场：等待地图...";
			return;
		}

		_3dHint.Text = "3D 战场：生成中...";
		_levelData = new LevelDataManager
		{
			AutoLoadOnReady = false,
			MapId = "pvp_download",
		};
		_world.AddChild(_levelData);
		if (!_levelData.LoadMapFromJson(PvpMapState.MapJson))
		{
			_3dHint.Text = "3D 战场：地图加载失败";
			return;
		}

		_mapGenerator = new MapGenerator
		{
			DefaultTilePrefab = ResourceLoader.Load<PackedScene>(
				"res://Scenes/Map/Tile/hex_tile_3d.tscn"),
			TilePrefabs = new Godot.Collections.Dictionary<string, PackedScene>
			{
				["ocean"] = ResourceLoader.Load<PackedScene>(
					"res://Scenes/Map/Tile/Prefab/hex_tile_ocean.tscn"),
				["island"] = ResourceLoader.Load<PackedScene>(
					"res://Scenes/Map/Tile/Prefab/hex_tile_island.tscn"),
				["default"] = ResourceLoader.Load<PackedScene>(
					"res://Scenes/Map/Tile/hex_tile_3d.tscn"),
			},
		};
		_mapGenerator.AddChild(new Node3D { Name = "MapContainer" });
		_world.AddChild(_mapGenerator);
		_mapGenerator.BuildMap(_levelData.TerrainData);

		_shipsRoot = new Node3D { Name = "PvpShips" };
		_world.AddChild(_shipsRoot);

		var camera = new Camera3D { Current = true };
		camera.Position = new Vector3(0, 30, 16);
		camera.RotationDegrees = new Vector3(-55, 0, 0);
		_world.AddChild(camera);

		_3dReady = true;
		_3dHint.Text = $"3D 战场：{PvpMapState.MapName}";
	}

	private void UpdateShips3D(JsonElement state)
	{
		if (!_3dReady || _mapGenerator == null)
		{
			return;
		}
		if (!state.TryGetProperty("ships", out JsonElement ships))
		{
			return;
		}

		var seen = new HashSet<string>();
		foreach (JsonElement ship in ships.EnumerateArray())
		{
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

			if (!_ships3D.TryGetValue(id, out ShipComponent component))
			{
				PackedScene prefab = ResourceLoader.Load<PackedScene>(
					"res://Ships/BaseShip/ship_3d.tscn");
				if (prefab == null)
				{
					continue;
				}
				component = prefab.Instantiate<ShipComponent>();
				_shipsRoot.AddChild(component);
				_ships3D[id] = component;
			}

			seen.Add(id);
			component.MoveToHex(
				_mapGenerator,
				new Vector2I(hex[0].GetInt32(), hex[1].GetInt32()));
			component.AnimateTurnTo(ServerFacingToHexDirection[facing % 6]);
			component.CurrentSpeed = speed;
			if (maxHp > 0)
			{
				component.MaxHp = maxHp;
				component.CurrentHp = Math.Clamp(hp, 0, maxHp);
			}
		}

		var stale = new List<string>();
		foreach (string key in _ships3D.Keys)
		{
			if (!seen.Contains(key))
			{
				stale.Add(key);
			}
		}
		foreach (string key in stale)
		{
			_ships3D[key].QueueFree();
			_ships3D.Remove(key);
		}
	}

	private void UpdateShips(JsonElement state)
	{
		if (!state.TryGetProperty("ships", out JsonElement ships))
		{
			_shipsLabel.Text = "舰船：-";
			return;
		}

		int mySide = MySide(state);
		var builder = new StringBuilder();
		builder.AppendLine("舰船：");
		foreach (JsonElement ship in ships.EnumerateArray())
		{
			string sideText = ship.TryGetProperty("side", out JsonElement sideProp)
				? sideProp.GetInt32() == mySide ? "我方" : "敌方"
				: "?";
			string name = ship.TryGetProperty("name", out JsonElement nameProp)
				? nameProp.GetString() ?? "?"
				: "?";
			JsonElement hex = ship.TryGetProperty("hex", out JsonElement hexProp)
				? hexProp
				: default;
			int speed = ship.TryGetProperty("speed", out JsonElement speedProp)
				? speedProp.GetInt32()
				: 0;
			int facing = ship.TryGetProperty("facing", out JsonElement facingProp)
				? facingProp.GetInt32()
				: 0;
			int hp = ship.TryGetProperty("hp", out JsonElement hpProp)
				? hpProp.GetInt32()
				: 0;
			int maxHp = ship.TryGetProperty("maxHp", out JsonElement maxHpProp)
				? maxHpProp.GetInt32()
				: 0;
			string shipStatus = ship.TryGetProperty("status", out JsonElement statusProp)
				? statusProp.GetString() ?? ""
				: "";
			string hexText = hex.ValueKind == JsonValueKind.Array && hex.GetArrayLength() >= 2
				? $"[{hex[0].GetInt32()},{hex[1].GetInt32()}]"
				: "[-]";
			builder.AppendLine($"[{sideText}] {name} {hexText} 速{speed} 向{facing} HP{hp}/{maxHp} {shipStatus}");
		}
		_shipsLabel.Text = builder.ToString().TrimEnd();
	}

	private void UpdateCommandStatus(JsonElement state)
	{
		if (!state.TryGetProperty("players", out JsonElement players) ||
			!state.TryGetProperty("commands", out JsonElement commands))
		{
			_commandStatusLabel.Text = "指令：未知";
			return;
		}

		int mySide = MySide(state);
		int playerIndex = 0;
		string mine = "未提交";
		string theirs = "未提交";
		foreach (JsonElement player in players.EnumerateArray())
		{
			string playerId = player.GetString() ?? "";
			string action = commands.TryGetProperty(playerId, out JsonElement actionProp)
				? actionProp.GetString() ?? ""
				: "";
			if (playerIndex == mySide)
			{
				mine = string.IsNullOrEmpty(action) ? "未提交" : action;
			}
			else
			{
				theirs = string.IsNullOrEmpty(action) ? "未提交" : action;
			}
			playerIndex++;
		}
		_commandStatusLabel.Text = $"指令：我方 {mine} / 敌方 {theirs}";
	}

	private void BuildCommandButtons(JsonElement state, string status)
	{
		foreach (Node child in _commandBox.GetChildren())
		{
			child.Free();
		}

		if (status != "active")
		{
			string winner = state.TryGetProperty("winner", out JsonElement winnerProp)
				? winnerProp.GetString() ?? ""
				: "";
			_commandBox.AddChild(new Label { Text = $"战斗已结束，胜者：{winner}" });
			return;
		}

		string phase = state.TryGetProperty("phase", out JsonElement phaseProp)
			? phaseProp.GetString() ?? ""
			: "";
		bool myTurn = state.TryGetProperty("activePlayer", out JsonElement activeProp) &&
			activeProp.GetString() == NetworkClient.Instance.UserId;
		if (!myTurn)
		{
			_commandBox.AddChild(new Label { Text = "等待对方提交指令..." });
		}
		else
		switch (phase)
		{
			case "speed":
				AddCommandButton("加速", () => SendCommand("accelerate"));
				AddCommandButton("减速", () => SendCommand("decelerate"));
				AddCommandButton("待命", () => SendCommand("wait"));
				break;
			case "move1":
			case "move2":
			case "move3":
				AddCommandButton("左转", () => SendCommand("turn_left"));
				AddCommandButton("右转", () => SendCommand("turn_right"));
				AddCommandButton("待命", () => SendCommand("wait"));
				break;
			case "gunnery":
				if (state.TryGetProperty("ships", out JsonElement ships))
				{
					int mySide = MySide(state);
					foreach (JsonElement ship in ships.EnumerateArray())
					{
						int side = ship.TryGetProperty("side", out JsonElement sideProp)
							? sideProp.GetInt32()
							: -1;
						if (side == mySide)
						{
							continue;
						}
						string id = ship.TryGetProperty("id", out JsonElement idProp)
							? idProp.GetString() ?? ""
							: "";
						string name = ship.TryGetProperty("name", out JsonElement nameProp)
							? nameProp.GetString() ?? "?"
							: "?";
						string targetId = id;
						AddCommandButton($"炮击 {name}", () => SendCommand("fire", targetId));
					}
				}
				AddCommandButton("待命", () => SendCommand("wait"));
				break;
		}

		AddCommandButton("推进结算", () => NetworkClient.Instance.SendWsBattleAdvance(
			PvpFlowState.PendingBattleId));
	}

	private void AddCommandButton(string text, Action action)
	{
		_commandBox.AddChild(MakeButton(text, action));
	}

	private void SendCommand(string action, string targetShipId = null)
	{
		if (string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
		{
			AppendLog("没有可用的 battleId");
			return;
		}
		NetworkClient.Instance.SendWsBattleCommand(
			PvpFlowState.PendingBattleId,
			action,
			targetShipId);
		AppendLog($"[{DateTime.Now:HH:mm:ss}] 已提交指令：{action}");
	}

	private int MySide(JsonElement state)
	{
		if (state.TryGetProperty("players", out JsonElement players))
		{
			int index = 0;
			foreach (JsonElement player in players.EnumerateArray())
			{
				if (player.GetString() == NetworkClient.Instance.UserId)
				{
					return index;
				}
				index++;
			}
		}
		return 0;
	}

	private void OnConnectionChanged(bool connected)
	{
		_wsStatusLabel.Text = connected
			? "已连接"
			: $"未连接 ({NetworkClient.Instance.LastWsError})";
		_wsStatusLabel.Modulate = connected
			? new Color(0.5f, 1f, 0.6f)
			: new Color(1f, 0.8f, 0.3f);
	}

	private void OnWsClosed(int code, string reason)
	{
		_wsStatusLabel.Text = $"已断开 ({code})";
	}

	private void AppendLog(string text)
	{
		if (_log != null)
		{
			_log.AppendText(text + "\n");
		}
	}

	private static Button MakeButton(string text, Action action)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 44),
		};
		button.Pressed += action;
		return button;
	}
}

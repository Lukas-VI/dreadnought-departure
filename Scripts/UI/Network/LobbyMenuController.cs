using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>联机大厅：房间列表、创建/加入房间、准备后开战。</summary>
public partial class LobbyMenuController : Control
{
	[Export] public string LoginMenuPath = "res://Scenes/UI/Network/login_menu.tscn";
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string BattleMenuPath = "res://Scenes/UI/Network/pvp_battle_menu.tscn";

	private sealed record RoomInfo(string Id, string Status, List<string> Players, string OwnerId);

	private ItemList _roomList;
	private VBoxContainer _detailBox;
	private Label _usernameLabel;
	private Label _wsStatusLabel;
	private Label _statusLabel;
	private Button _refreshButton;
	private Button _createButton;
	private Button _joinButton;
	private Button _startButton;
	private Button _leaveButton;
	private string _selectedRoomId = "";
	private string _myUserId = "";
	private List<RoomInfo> _rooms = new();
	private bool _busy;

	public override void _Ready()
	{
		if (NetworkClient.Instance == null || !NetworkClient.Instance.IsLoggedIn)
		{
			GetTree().ChangeSceneToFile(LoginMenuPath);
			return;
		}

		BuildUi();
		_usernameLabel.Text = NetworkClient.Instance.Username;
		NetworkClient.Instance.WsMessageReceived += OnWsMessage;
		NetworkClient.Instance.ConnectionStateChanged += OnConnectionChanged;
		NetworkClient.Instance.WsClosed += OnWsClosed;
		NetworkClient.Instance.ConnectWebSocket();
		_wsStatusLabel.Text = "连接中...";
		_ = LoadMeAsync();
		RefreshRooms();
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
			Color = new Color(0.05f, 0.07f, 0.11f, 1f),
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

		var title = new Label { Text = "联机大厅" };
		title.AddThemeFontSizeOverride("font_size", 26);
		topBar.AddChild(title);

		_usernameLabel = new Label { Text = "" };
		_usernameLabel.AddThemeFontSizeOverride("font_size", 18);
		topBar.AddChild(_usernameLabel);

		_wsStatusLabel = new Label { Text = "未连接", Modulate = new Color(1f, 0.8f, 0.3f) };
		topBar.AddChild(_wsStatusLabel);

		var topSpacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		topBar.AddChild(topSpacer);

		topBar.AddChild(MakeButton("返回主菜单", () => _ = OnBackToMainMenuAsync()));

		var body = new HBoxContainer();
		body.SetAnchorsPreset(LayoutPreset.FullRect);
		body.OffsetTop = 80;
		body.OffsetBottom = -20;
		body.AddThemeConstantOverride("separation", 18);
		AddChild(body);

		var leftPanel = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
		body.AddChild(leftPanel);
		var leftBox = new VBoxContainer();
		leftBox.AddThemeConstantOverride("separation", 10);
		leftPanel.AddChild(leftBox);

		var listHeader = new Label { Text = "房间列表" };
		listHeader.AddThemeFontSizeOverride("font_size", 20);
		leftBox.AddChild(listHeader);

		_roomList = new ItemList { CustomMinimumSize = new Vector2(0, 420) };
		_roomList.ItemSelected += index =>
		{
			int selectedIndex = (int)index;
			if (selectedIndex >= 0 && selectedIndex < _rooms.Count)
			{
				_selectedRoomId = _rooms[selectedIndex].Id;
				ShowDetail(_rooms[selectedIndex]);
			}
		};
		leftBox.AddChild(_roomList);

		var listButtons = new HBoxContainer();
		listButtons.AddThemeConstantOverride("separation", 8);
		_refreshButton = MakeButton("刷新", RefreshRooms);
		_createButton = MakeButton("创建房间", () => _ = OnCreateRoomAsync());
		listButtons.AddChild(_refreshButton);
		listButtons.AddChild(_createButton);
		leftBox.AddChild(listButtons);

		_statusLabel = new Label
		{
			Text = "",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		leftBox.AddChild(_statusLabel);

		var rightPanel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
		body.AddChild(rightPanel);
		_detailBox = new VBoxContainer();
		_detailBox.AddThemeConstantOverride("separation", 12);
		rightPanel.AddChild(_detailBox);

		var placeholder = new Label { Text = "选择房间查看详情" };
		placeholder.AddThemeFontSizeOverride("font_size", 18);
		_detailBox.AddChild(placeholder);
	}

	private async void RefreshRooms()
	{
		if (_busy)
		{
			return;
		}

		_busy = true;
		_refreshButton.Disabled = true;
		try
		{
			JsonElement result = await NetworkClient.Instance.ListRoomsAsync();
			_rooms.Clear();
			if (result.TryGetProperty("rooms", out JsonElement rooms))
			{
				foreach (JsonElement room in rooms.EnumerateArray())
				{
					_rooms.Add(ParseRoom(room));
				}
			}

			_roomList.Clear();
			foreach (RoomInfo room in _rooms)
			{
				_roomList.AddItem($"{room.Id}  |  {room.Status}  |  {room.Players.Count}/2");
			}

			RoomInfo selected = _rooms.Find(room => room.Id == _selectedRoomId);
			if (selected != null)
			{
				ShowDetail(selected);
			}
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"刷新失败：{ex.Message}";
		}
		finally
		{
			_busy = false;
			_refreshButton.Disabled = false;
		}
	}

	private async Task LoadMeAsync()
	{
		try
		{
			JsonElement me = await NetworkClient.Instance.GetMeAsync();
			if (me.TryGetProperty("id", out JsonElement id))
			{
				_myUserId = id.GetString() ?? "";
			}
		}
		catch
		{
			_myUserId = "";
		}
	}

	private async Task OnCreateRoomAsync()
	{
		SetBusy(true);
		try
		{
			JsonElement room = await NetworkClient.Instance.CreateRoomAsync();
			string roomId = room.TryGetProperty("id", out JsonElement id) ? id.GetString() ?? "" : "";
			_selectedRoomId = roomId;
			SubscribeRoom(roomId);
			RefreshRooms();
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"创建失败：{ex.Message}";
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async Task OnJoinRoomAsync()
	{
		if (string.IsNullOrEmpty(_selectedRoomId))
		{
			return;
		}

		SetBusy(true);
		try
		{
			await NetworkClient.Instance.JoinRoomAsync(_selectedRoomId);
			SubscribeRoom(_selectedRoomId);
			RefreshRooms();
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"加入失败：{ex.Message}";
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async Task OnStartBattleAsync()
	{
		if (string.IsNullOrEmpty(_selectedRoomId))
		{
			return;
		}

		SetBusy(true);
		try
		{
			JsonElement battle = await NetworkClient.Instance.StartBattleAsync(_selectedRoomId);
			PvpFlowState.PendingRoomId = _selectedRoomId;
			PvpFlowState.PendingBattleId = battle.TryGetProperty("id", out JsonElement id)
				? id.GetString() ?? ""
				: "";
			GetTree().ChangeSceneToFile(BattleMenuPath);
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"开战失败：{ex.Message}";
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void ShowDetail(RoomInfo room)
	{
		foreach (Node child in _detailBox.GetChildren())
		{
			child.Free();
		}

		var idLabel = new Label { Text = $"房间：{room.Id}" };
		idLabel.AddThemeFontSizeOverride("font_size", 20);
		_detailBox.AddChild(idLabel);

		_detailBox.AddChild(new Label { Text = $"状态：{room.Status}" });
		_detailBox.AddChild(new Label { Text = $"玩家：{room.Players.Count} / 2" });

		foreach (string playerId in room.Players)
		{
			string suffix = playerId == _myUserId ? "（我）" : "";
			_detailBox.AddChild(new Label { Text = $"  {playerId}{suffix}" });
		}

		bool isMember = room.Players.Contains(_myUserId);
		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);

		_joinButton = MakeButton("加入房间", () => _ = OnJoinRoomAsync());
		_joinButton.Disabled = isMember || room.Status != "waiting";
		buttons.AddChild(_joinButton);

		_startButton = MakeButton("开始战斗", () => _ = OnStartBattleAsync());
		_startButton.Disabled = !isMember || room.OwnerId != _myUserId || room.Status != "ready";
		buttons.AddChild(_startButton);

		_leaveButton = MakeButton("离开房间", () => _ = OnLeaveRoomAsync());
		_leaveButton.Disabled = !isMember;
		buttons.AddChild(_leaveButton);
		_detailBox.AddChild(buttons);

		var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
		_detailBox.AddChild(spacer);
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

			if (type == "room.updated")
			{
				RefreshRooms();
			}
			else if (type == "battle.started" &&
				root.TryGetProperty("battle", out JsonElement battle))
			{
				PvpFlowState.PendingRoomId = battle.TryGetProperty("roomId", out JsonElement roomId)
					? roomId.GetString() ?? _selectedRoomId
					: _selectedRoomId;
				PvpFlowState.PendingBattleId = battle.TryGetProperty("id", out JsonElement id)
					? id.GetString() ?? ""
					: "";
				GetTree().ChangeSceneToFile(BattleMenuPath);
			}
			else if (type == "room.removed")
			{
				if (root.TryGetProperty("roomId", out JsonElement removedRoom) &&
					removedRoom.GetString() == _selectedRoomId)
				{
					_selectedRoomId = "";
				}
				RefreshRooms();
			}
		}
		catch
		{
			// 忽略非 JSON 或未知消息。
		}
	}

	private void OnConnectionChanged(bool connected)
	{
		_wsStatusLabel.Text = connected ? "已连接" : "未连接";
		_wsStatusLabel.Modulate = connected
			? new Color(0.5f, 1f, 0.6f)
			: new Color(1f, 0.8f, 0.3f);
	}

	private void OnWsClosed(int code, string reason)
	{
		_wsStatusLabel.Text = $"已断开 ({code})";
	}

	private void SetBusy(bool busy)
	{
		_createButton.Disabled = busy;
		_refreshButton.Disabled = busy;
		if (_joinButton != null)
		{
			_joinButton.Disabled = busy;
		}
		if (_startButton != null)
		{
			_startButton.Disabled = busy;
		}
		if (_leaveButton != null)
		{
			_leaveButton.Disabled = busy;
		}
	}

	private async Task OnLeaveRoomAsync()
	{
		if (string.IsNullOrEmpty(_selectedRoomId))
		{
			return;
		}

		string roomId = _selectedRoomId;
		SetBusy(true);
		try
		{
			await NetworkClient.Instance.LeaveRoomAsync(roomId);
			_selectedRoomId = "";
			NetworkClient.Instance.SendWebSocket(
				$"{{\"type\":\"lobby.leave\",\"roomId\":\"{roomId}\"}}");
			RefreshRooms();
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"离开失败：{ex.Message}";
		}
		finally
		{
			SetBusy(false);
		}
	}

	private async Task OnBackToMainMenuAsync()
	{
		try
		{
			if (!string.IsNullOrEmpty(_selectedRoomId))
			{
				await NetworkClient.Instance.LeaveRoomAsync(_selectedRoomId);
			}
		}
		catch
		{
			// 断线清理也会兜底，忽略离开失败。
		}
		NetworkClient.Instance.Logout();
		GetTree().ChangeSceneToFile(MainMenuPath);
	}

	private void SubscribeRoom(string roomId)
	{
		NetworkClient.Instance.SendWebSocket(
			$"{{\"type\":\"lobby.join\",\"roomId\":\"{roomId}\"}}");
	}

	private static RoomInfo ParseRoom(JsonElement element)
	{
		string id = element.TryGetProperty("id", out JsonElement idProp)
			? idProp.GetString() ?? ""
			: "";
		string status = element.TryGetProperty("status", out JsonElement statusProp)
			? statusProp.GetString() ?? ""
			: "";
		string ownerId = element.TryGetProperty("ownerId", out JsonElement ownerProp)
			? ownerProp.GetString() ?? ""
			: "";
		var players = new List<string>();
		if (element.TryGetProperty("players", out JsonElement playersProp))
		{
			foreach (JsonElement player in playersProp.EnumerateArray())
			{
				players.Add(player.GetString() ?? "");
			}
		}

		return new RoomInfo(id, status, players, ownerId);
	}

	private static Button MakeButton(string text, Action action)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 48),
		};
		button.Pressed += action;
		return button;
	}
}

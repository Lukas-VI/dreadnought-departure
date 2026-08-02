using Godot;
using System;
using System.Text;
using System.Text.Json;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>PvP 战斗同步占位：展示房间/战斗状态，验证 WebSocket 权威骰值广播。</summary>
public partial class PvpBattleMenuController : Control
{
	[Export] public string LobbyMenuPath = "res://Scenes/UI/Network/lobby_menu.tscn";
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private Label _battleIdLabel;
	private Label _roomIdLabel;
	private Label _playersLabel;
	private Label _phaseLabel;
	private Label _wsStatusLabel;
	private RichTextLabel _log;

	public override void _Ready()
	{
		BuildUi();
		NetworkClient.Instance.WsMessageReceived += OnWsMessage;
		NetworkClient.Instance.ConnectionStateChanged += OnConnectionChanged;
		NetworkClient.Instance.WsClosed += OnWsClosed;
		NetworkClient.Instance.ConnectWebSocket();
		OnConnectionChanged(NetworkClient.Instance.IsWebSocketConnected);

		if (!string.IsNullOrEmpty(PvpFlowState.PendingRoomId))
		{
			NetworkClient.Instance.SendWebSocket(
				$"{{\"type\":\"lobby.join\",\"roomId\":\"{PvpFlowState.PendingRoomId}\"}}");
		}

		_battleIdLabel.Text = $"战斗：{PvpFlowState.PendingBattleId}";
		_roomIdLabel.Text = $"房间：{PvpFlowState.PendingRoomId}";
		AppendLog($"已进入战斗同步界面，等待房间广播");
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

		var infoPanel = new PanelContainer { CustomMinimumSize = new Vector2(420, 0) };
		body.AddChild(infoPanel);
		var infoBox = new VBoxContainer();
		infoBox.AddThemeConstantOverride("separation", 12);
		infoPanel.AddChild(infoBox);

		_battleIdLabel = new Label { Text = "战斗：-" };
		_battleIdLabel.AddThemeFontSizeOverride("font_size", 20);
		infoBox.AddChild(_battleIdLabel);

		_roomIdLabel = new Label { Text = "房间：-" };
		infoBox.AddChild(_roomIdLabel);

		_playersLabel = new Label { Text = "玩家：-" };
		infoBox.AddChild(_playersLabel);

		_phaseLabel = new Label { Text = "阶段：setup / 回合 0" };
		infoBox.AddChild(_phaseLabel);

		infoBox.AddChild(MakeButton("骰值测试 3d100", () => SendTestRoll()));
		infoBox.AddChild(MakeButton("重连", () => NetworkClient.Instance.ReconnectWebSocket()));

		var logPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		body.AddChild(logPanel);
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
			CustomMinimumSize = new Vector2(0, 0),
		};
		logBox.AddChild(_log);
	}

	private void SendTestRoll()
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

		NetworkClient.Instance.SendWebSocket(
			$"{{\"type\":\"battle.roll\",\"battleId\":\"{PvpFlowState.PendingBattleId}\",\"count\":3,\"sides\":100,\"reason\":\"client-test\"}}");
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

			if (type == "battle.rolled" && root.TryGetProperty("roll", out JsonElement roll))
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
				}
				AppendLog("战斗开始广播已收到");
			}
		}
		catch (Exception ex)
		{
			AppendLog($"解析失败：{ex.Message}");
		}
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
			CustomMinimumSize = new Vector2(0, 48),
		};
		button.Pressed += action;
		return button;
	}
}

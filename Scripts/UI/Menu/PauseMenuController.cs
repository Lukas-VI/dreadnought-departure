using Godot;
using System;
using System.Text.Json;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// ESC 暂停菜单。PvP 模式额外显示远程状态与同步日志。
/// </summary>
public partial class PauseMenuController : Control
{
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string CurrentBattleScenePath = "res://Scenes/Battle/battle_scene.tscn";
	[Export] public string PvpLobbyScenePath = "res://Scenes/UI/Network/lobby_menu.tscn";

	private bool _isPaused;
	private bool _pvp;
	private Label _pvpTurnLabel;
	private Label _pvpStateLabel;
	private RichTextLabel _pvpLog;
	private string _lastPvpPhase = "";
	private int _lastPvpTurn = -1;

	public override void _Ready()
	{
		Visible = false;
		if (PvpFlowState.PvpBattle)
		{
			_pvp = true;
			BuildPvpPanel();
			NetworkClient.Instance.WsMessageReceived += OnPvpMessage;
			NetworkClient.Instance.ConnectionStateChanged += OnPvpConnectionChanged;
		}
	}

	public override void _ExitTree()
	{
		if (_pvp && NetworkClient.Instance != null)
		{
			NetworkClient.Instance.WsMessageReceived -= OnPvpMessage;
			NetworkClient.Instance.ConnectionStateChanged -= OnPvpConnectionChanged;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			if (_isPaused) Resume();
			else Pause();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Pause()
	{
		_isPaused = true;
		Visible = true;
		GetTree().Paused = true;
		if (_pvp && !string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
		{
			NetworkClient.Instance.SendWsGetBattleState(PvpFlowState.PendingBattleId);
		}
	}

	private void Resume()
	{
		_isPaused = false;
		Visible = false;
		GetTree().Paused = false;
	}

	private void BuildPvpPanel()
	{
		var box = GetNode<VBoxContainer>("CenterContainer/VBoxContainer");
		box.AddThemeConstantOverride("separation", 10);

		var title = new Label { Text = "PvP 远程调试", HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 22);
		box.AddChild(title);

		_pvpTurnLabel = new Label { Text = "回合：- / 阶段：-", HorizontalAlignment = HorizontalAlignment.Center };
		box.AddChild(_pvpTurnLabel);

		_pvpStateLabel = new Label
		{
			Text = "等待服务端状态...",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		box.AddChild(_pvpStateLabel);

		_pvpLog = new RichTextLabel
		{
			BbcodeEnabled = false,
			ScrollFollowing = true,
			CustomMinimumSize = new Vector2(520, 150),
		};
		box.AddChild(_pvpLog);

		box.AddChild(MakeButton("返回大厅", () =>
		{
			PvpFlowState.PvpBattle = false;
			GetTree().Paused = false;
			GetTree().ChangeSceneToFile(PvpLobbyScenePath);
		}));
	}

	private void OnPvpMessage(string json)
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
				ApplyPvpState(state);
			}
			else if (type == "battle.rolled" && root.TryGetProperty("roll", out JsonElement roll))
			{
				string values = roll.TryGetProperty("values", out JsonElement valuesProp)
					? valuesProp.ToString()
					: "[]";
				AppendPvpLog($"骰值广播：{values}");
			}
			else if (type == "error")
			{
				string code = root.TryGetProperty("code", out JsonElement codeProp)
					? codeProp.GetString() ?? ""
					: "";
				AppendPvpLog($"服务端错误：{code}");
			}
		}
		catch
		{
			// 忽略非 JSON 消息。
		}
	}

	private void ApplyPvpState(JsonElement state)
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
		bool paused = state.TryGetProperty("paused", out JsonElement pausedProp) &&
			pausedProp.ValueKind == JsonValueKind.True;
		_pvpTurnLabel.Text = $"回合：{turn} / 阶段：{phase} / {status}";

		if (turn != _lastPvpTurn || phase != _lastPvpPhase)
		{
			_lastPvpTurn = turn;
			_lastPvpPhase = phase;
			AppendPvpLog($"回合 {turn} · 阶段 {phase}");
		}

		string myUserId = NetworkClient.Instance.UserId;
		bool myTurn = state.TryGetProperty("activePlayer", out JsonElement activeProp) &&
			activeProp.GetString() == myUserId;
		string initiative = "先手：?";
		if (state.TryGetProperty("turnOrder", out JsonElement turnOrder) &&
			turnOrder.GetArrayLength() >= 2)
		{
			initiative = turnOrder[0].GetString() == myUserId ? "先手：我方" : "先手：敌方";
		}
		_pvpStateLabel.Text = paused
			? "对局暂停：对手断线"
			: $"{initiative} / {(myTurn ? "轮到我提交" : "等待对方提交")}";
	}

	private void AppendPvpLog(string text)
	{
		if (_pvpLog != null)
		{
			_pvpLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}\n");
		}
	}

	private void OnPvpConnectionChanged(bool connected)
	{
		AppendPvpLog(connected ? "WebSocket 已连接" : "WebSocket 已断开");
	}

	public void _OnResumePressed() => Resume();

	public void _OnRetryPressed()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(CurrentBattleScenePath);
	}

	public void _OnMainMenuPressed()
	{
		PvpFlowState.PvpBattle = false;
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}

	private static Button MakeButton(string text, Action pressed)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 40),
		};
		button.Pressed += pressed;
		return button;
	}
}

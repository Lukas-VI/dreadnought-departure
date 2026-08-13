using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DreadnoughtDeparture.Network;
using DreadnoughtDeparture.Story;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector
{
	private void OnPvpConnectionChanged(bool connected)
	{
		var bus = GetNodeOrNull<EventBus>("EventBus");
		if (bus == null) return;
		bus.EmitLog(connected
			? "PvP 连接已建立，等待服务端状态..."
			: "PvP 连接断开，正在自动重连...");
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
			_currentPhase = PvpSyncService.RemotePhaseToLocal(phase);
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

		var syncContext = new PvpSyncService.RemoteShipSyncContext
		{
			RemoteShips = _remoteShips,
			RemoteTweens = _remoteTweens,
			UnitSpawner = _unitSpawner,
			MapGenerator = _mapGenerator,
			MyUserId = NetworkClient.Instance.UserId,
		};
		(_playerShips, _enemyShips) = PvpSyncService.ApplyShips(state, syncContext);
		if (state.TryGetProperty("torpedoes", out JsonElement torpedoes))
		{
			PvpSyncService.SyncRemoteTorpedoes(
				torpedoes, _torpedoController, _mapGenerator, _remoteTorpedoes);
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
}

using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DreadnoughtDeparture.Network;

/// <summary>跨场景保留的登录状态与 HTTP / WebSocket 客户端。</summary>
public partial class NetworkClient : Node
{
	public static NetworkClient Instance { get; private set; }

	public const string DefaultHttpBaseUrl = "http://192.168.31.135:3000";
	private const string AuthFilePath = "user://auth.json";

	public string HttpBaseUrl { get; set; } = DefaultHttpBaseUrl;
	public string Token { get; private set; } = "";
	public string Username { get; private set; } = "";
	public string Email { get; private set; } = "";
	public string UserId { get; private set; } = "";
	public bool IsLoggedIn => !string.IsNullOrEmpty(Token);

	public string WsUrl => HttpBaseUrl
		.Replace("http://", "ws://")
		.Replace("https://", "wss://") + "/ws";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private WebSocketPeer _ws;
	private bool _wsConnected;
	private bool _wsShouldConnect;
	private bool _wsAuthenticated;
	private bool _wsRejoinSent;
	private double _reconnectTimer;
	private readonly Queue<string> _pendingWsMessages = new();

	private const double ReconnectIntervalSeconds = 2.0;

	public bool IsWebSocketConnected => _wsConnected;
	public string LastWsError { get; private set; } = "";
	public long ServerTimeOffsetMs { get; private set; }

	[Signal] public delegate void AuthChangedEventHandler(bool loggedIn, string username);
	[Signal] public delegate void ConnectionStateChangedEventHandler(bool connected);
	[Signal] public delegate void WsMessageReceivedEventHandler(string json);
	[Signal] public delegate void WsClosedEventHandler(int code, string reason);

	public override void _Ready()
	{
		Instance = this;
		ProcessMode = ProcessModeEnum.Always;
		LoadPersistedSession();
	}

	public override void _Process(double delta)
	{
		try
		{
			if (_ws != null && (_wsShouldConnect || _wsConnected))
			{
				_ws.Poll();
				var state = _ws.GetReadyState();
				if (state == WebSocketPeer.State.Open)
				{
					if (!_wsConnected)
					{
						_wsConnected = true;
						_wsAuthenticated = false;
						_wsRejoinSent = false;
						_reconnectTimer = 0;
						LastWsError = "";
						EmitSignal(SignalName.ConnectionStateChanged, true);
					}

					if (!_wsAuthenticated && IsLoggedIn)
					{
						_wsAuthenticated = true;
						_ws.SendText($"{{\"type\":\"auth\",\"token\":\"{Token}\"}}");
					}

					if (_wsAuthenticated && PvpFlowState.PvpBattle && !_wsRejoinSent)
					{
						_wsRejoinSent = true;
						if (!string.IsNullOrEmpty(PvpFlowState.PendingRoomId))
						{
							_ws.SendText(JsonSerializer.Serialize(
								new { type = "lobby.join", roomId = PvpFlowState.PendingRoomId },
								JsonOptions));
						}
						if (!string.IsNullOrEmpty(PvpFlowState.PendingBattleId))
						{
							_ws.SendText(JsonSerializer.Serialize(
								new { type = "battle.state.get", battleId = PvpFlowState.PendingBattleId },
								JsonOptions));
						}
					}

					while (_pendingWsMessages.Count > 0)
					{
						_ws.SendText(_pendingWsMessages.Dequeue());
					}

					while (_ws.GetAvailablePacketCount() > 0)
					{
						byte[] packet = _ws.GetPacket();
						string text = Encoding.UTF8.GetString(packet);
						EmitSignal(SignalName.WsMessageReceived, text);
					}
				}
				else if (state == WebSocketPeer.State.Closed && (_wsConnected || _wsShouldConnect))
				{
					int code = _ws.GetCloseCode();
					string reason = _ws.GetCloseReason();
					_wsConnected = false;
					_wsShouldConnect = false;
					_wsAuthenticated = false;
					_wsRejoinSent = false;
					LastWsError = $"closed_{code}_{reason}";
					EmitSignal(SignalName.WsClosed, code, reason);
					EmitSignal(SignalName.ConnectionStateChanged, false);
				}
			}
		}
		catch
		{
			_wsConnected = false;
			_wsShouldConnect = false;
			_wsRejoinSent = false;
			_ws = null;
			LastWsError = "poll_error";
			EmitSignal(SignalName.ConnectionStateChanged, false);
		}

		if (IsLoggedIn && !_wsConnected && !_wsShouldConnect)
		{
			_reconnectTimer += delta;
			if (_reconnectTimer >= ReconnectIntervalSeconds)
			{
				_reconnectTimer = 0;
				ConnectWebSocket();
			}
		}
	}

	public void ConnectWebSocket()
	{
		if (!IsLoggedIn || _wsConnected || _wsShouldConnect)
		{
			return;
		}

		_ws = new WebSocketPeer();
		Error err = _ws.ConnectToUrl(WsUrl);
		LastWsError = err.ToString();
		if (err != Error.Ok)
		{
			_ws = null;
			_wsShouldConnect = false;
			EmitSignal(SignalName.ConnectionStateChanged, false);
			return;
		}
		_wsShouldConnect = true;
		_wsAuthenticated = false;
	}

	public void ReconnectWebSocket()
	{
		if (_ws != null)
		{
			try
			{
				_ws.Close();
			}
			catch
			{
				// 忽略未连接 peer 的关闭异常。
			}
		}
		_ws = null;
		_wsConnected = false;
		_wsShouldConnect = false;
		_wsAuthenticated = false;
		ConnectWebSocket();
	}

	public void SendWebSocket(string json)
	{
		if (_wsConnected && _ws != null)
		{
			if (_wsAuthenticated)
			{
				_ws.SendText(json);
			}
			else if (_pendingWsMessages.Count < 64)
			{
				_pendingWsMessages.Enqueue(json);
			}
		}
		else if (_pendingWsMessages.Count < 64)
		{
			_pendingWsMessages.Enqueue(json);
		}
	}

	public void SendWsJoinRoom(string roomId)
	{
		SendWebSocket(JsonSerializer.Serialize(new { type = "lobby.join", roomId }, JsonOptions));
	}

	public void SendWsLeaveRoom(string roomId)
	{
		SendWebSocket(JsonSerializer.Serialize(new { type = "lobby.leave", roomId }, JsonOptions));
	}

	public void SendWsGetBattleState(string battleId)
	{
		SendWebSocket(JsonSerializer.Serialize(new { type = "battle.state.get", battleId }, JsonOptions));
	}

	public void SendWsBattleCommand(string battleId, string action, string targetShipId = null)
	{
		object detail = targetShipId == null ? null : new { targetShipId };
		SendWebSocket(JsonSerializer.Serialize(
			new { type = "battle.command", battleId, action, detail },
			JsonOptions));
	}

	public void SendWsBattleShipsCommand(string battleId, IList<object> ships)
	{
		SendWebSocket(JsonSerializer.Serialize(
			new { type = "battle.command", battleId, ships },
			JsonOptions));
	}

	public void SendWsBattleAdvance(string battleId)
	{
		SendWebSocket(JsonSerializer.Serialize(new { type = "battle.advance", battleId }, JsonOptions));
	}

	public void SendWsBattleRoll(string battleId, int count, int sides, string reason)
	{
		SendWebSocket(JsonSerializer.Serialize(
			new { type = "battle.roll", battleId, count, sides, reason },
			JsonOptions));
	}

	public void DisconnectWebSocket()
	{
		if (_ws != null && _wsConnected)
		{
			_ws.Close();
		}
	}

	public void Logout()
	{
		Token = "";
		Username = "";
		Email = "";
		UserId = "";
		_wsConnected = false;
		_wsShouldConnect = false;
		_wsAuthenticated = false;
		_wsRejoinSent = false;
		_reconnectTimer = 0;
		_pendingWsMessages.Clear();
		PvpMapState.MapJson = "";
		PvpMapState.MapName = "";
		ClearPersistedSession();
		if (_ws != null)
		{
			_ws.Close();
			_ws = null;
		}
		EmitSignal(SignalName.AuthChanged, false, "");
	}

	public async Task<JsonElement> RegisterAsync(string email, string username, string password)
	{
		JsonElement result = await RequestAsync(
			"/api/auth/register",
			"POST",
			new { email, username, password },
			false);
		ApplySession(result);
		return result;
	}

	public async Task<JsonElement> LoginAsync(string email, string username, string password)
	{
		JsonElement result = await RequestAsync(
			"/api/auth/login",
			"POST",
			new { email, username, password },
			false);
		ApplySession(result);
		return result;
	}

	public async Task<JsonElement> GetMeAsync()
	{
		return await RequestAsync("/api/me", "GET");
	}

	public async Task FetchServerTimeAsync()
	{
		JsonElement result = await RequestAsync("/api/time", "GET", null, false);
		if (result.TryGetProperty("serverTime", out JsonElement serverTime))
		{
			ServerTimeOffsetMs =
				serverTime.GetInt64() - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}
	}

	public async Task<JsonElement> ListRoomsAsync()
	{
		return await RequestAsync("/api/lobby/rooms", "GET");
	}

	public async Task<JsonElement> CreateRoomAsync()
	{
		return await RequestAsync("/api/lobby/create", "POST");
	}

	public async Task<JsonElement> JoinRoomAsync(string roomId)
	{
		return await RequestAsync("/api/lobby/join", "POST", new { roomId });
	}

	public async Task<JsonElement> LeaveRoomAsync(string roomId)
	{
		return await RequestAsync("/api/lobby/leave", "POST", new { roomId });
	}

	public async Task<JsonElement> UploadMapAsync(string roomId, JsonElement map)
	{
		return await RequestAsync($"/api/lobby/rooms/{roomId}/map", "PUT", map);
	}

	public async Task<JsonElement> DownloadMapAsync(string roomId)
	{
		return await RequestAsync($"/api/lobby/rooms/{roomId}/map", "GET");
	}

	public async Task<JsonElement> UploadShipDataAsync(string roomId, JsonElement ships)
	{
		return await RequestAsync(
			$"/api/lobby/rooms/{roomId}/shipdata",
			"PUT",
			new { ships });
	}

	public async Task<JsonElement> StartBattleAsync(string roomId)
	{
		return await RequestAsync("/api/battle/start", "POST", new { roomId });
	}

	public async Task<JsonElement> PullGachaAsync(string pool, int count, string idempotencyKey)
	{
		return await RequestAsync(
			"/api/gacha/pull",
			"POST",
			new { pool, count, idempotencyKey });
	}

	public async Task<JsonElement> GetGachaPoolsAsync()
	{
		return await RequestAsync("/api/gacha/pools", "GET");
	}

	public async Task<JsonElement> UpdateProfileAsync(string nickname, string avatar = "")
	{
		return await RequestAsync(
			"/api/me/profile",
			"PATCH",
			new { nickname, avatar });
	}

	public async Task<JsonElement> GetBackpackAsync()
	{
		return await RequestAsync("/api/backpack", "GET");
	}

	public async Task<JsonElement> GetShopCatalogAsync()
	{
		return await RequestAsync("/api/shop", "GET");
	}

	public async Task<JsonElement> BuyShopItemAsync(string itemId)
	{
		return await RequestAsync("/api/shop/buy", "POST", new { itemId });
	}

	public async Task<JsonElement> GetMailAsync()
	{
		return await RequestAsync("/api/mail", "GET");
	}

	public async Task<JsonElement> ReadMailAsync(string mailId)
	{
		return await RequestAsync($"/api/mail/{mailId}/read", "POST");
	}

	public async Task<JsonElement> ClaimMailAsync(string mailId)
	{
		return await RequestAsync($"/api/mail/{mailId}/claim", "POST");
	}

	private async Task<JsonElement> RequestAsync(
		string path,
		string method,
		object body = null,
		bool auth = true)
	{
		var request = new HttpRequest { Timeout = 10 };
		AddChild(request);

		try
		{
			var headers = new List<string> { "Content-Type: application/json" };
			if (auth && IsLoggedIn)
			{
				headers.Add($"Authorization: Bearer {Token}");
			}

			string payload = body == null ? "" : JsonSerializer.Serialize(body, JsonOptions);
			Error err = request.Request(
				$"{HttpBaseUrl}{path}",
				headers.ToArray(),
				ParseMethod(method),
				payload);
			if (err != Error.Ok)
			{
				throw new NetworkException($"request_failed_{err}");
			}

			Variant[] result = await ToSignal(request, HttpRequest.SignalName.RequestCompleted);
			long resultCode = result[0].AsInt64();
			long responseCode = result[1].AsInt64();
			byte[] rawBody = result[3].AsByteArray();
			if (resultCode != (long)Error.Ok)
			{
				throw new NetworkException($"network_error_{resultCode}");
			}

			string text = Encoding.UTF8.GetString(rawBody);
			if (responseCode >= 400)
			{
				string code = ExtractErrorCode(text);
				throw new NetworkException(string.IsNullOrEmpty(code) ? $"http_{responseCode}" : code);
			}

			using var document = JsonDocument.Parse(string.IsNullOrEmpty(text) ? "{}" : text);
			return document.RootElement.Clone();
		}
		finally
		{
			request.QueueFree();
		}
	}

	private void ApplySession(JsonElement result)
	{
		if (result.TryGetProperty("token", out JsonElement token))
		{
			Token = token.GetString() ?? "";
		}

		if (result.TryGetProperty("user", out JsonElement user) &&
			user.TryGetProperty("username", out JsonElement username))
		{
			Username = username.GetString() ?? "";
		}
		if (result.TryGetProperty("user", out JsonElement userWithEmail) &&
			userWithEmail.TryGetProperty("email", out JsonElement email))
		{
			Email = email.GetString() ?? "";
		}
		if (result.TryGetProperty("user", out JsonElement userObject) &&
			userObject.TryGetProperty("id", out JsonElement userId))
		{
			UserId = userId.GetString() ?? "";
		}

		SavePersistedSession();
		EmitSignal(SignalName.AuthChanged, IsLoggedIn, Username);
	}

	private void SavePersistedSession()
	{
		if (!IsLoggedIn)
		{
			return;
		}
		using var file = FileAccess.Open(AuthFilePath, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			return;
		}
		file.StoreString(JsonSerializer.Serialize(new
		{
			token = Token,
			username = Username,
			email = Email,
			userId = UserId,
			httpBaseUrl = HttpBaseUrl,
		}));
	}

	private void LoadPersistedSession()
	{
		if (!FileAccess.FileExists(AuthFilePath))
		{
			return;
		}
		using var file = FileAccess.Open(AuthFilePath, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			return;
		}
		string text = file.GetAsText();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			using var document = JsonDocument.Parse(text);
			JsonElement root = document.RootElement;
			Token = root.TryGetProperty("token", out JsonElement token) ? token.GetString() ?? "" : "";
			Username = root.TryGetProperty("username", out JsonElement username)
				? username.GetString() ?? ""
				: "";
			Email = root.TryGetProperty("email", out JsonElement email) ? email.GetString() ?? "" : "";
			UserId = root.TryGetProperty("userId", out JsonElement userId) ? userId.GetString() ?? "" : "";
			HttpBaseUrl = root.TryGetProperty("httpBaseUrl", out JsonElement baseUrl)
				&& !string.IsNullOrEmpty(baseUrl.GetString())
				? baseUrl.GetString() ?? DefaultHttpBaseUrl
				: DefaultHttpBaseUrl;
		}
		catch
		{
			ClearPersistedSession();
		}
	}

	private void ClearPersistedSession()
	{
		if (FileAccess.FileExists(AuthFilePath))
		{
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(AuthFilePath));
		}
	}

	private static string ExtractErrorCode(string text)
	{
		try
		{
			using var document = JsonDocument.Parse(text);
			return document.RootElement.TryGetProperty("error", out JsonElement error)
				? error.GetString() ?? ""
				: "";
		}
		catch
		{
			return "";
		}
	}

	private static HttpClient.Method ParseMethod(string method)
	{
		return method.ToUpperInvariant() switch
		{
			"PATCH" => HttpClient.Method.Patch,
			"POST" => HttpClient.Method.Post,
			"PUT" => HttpClient.Method.Put,
			"DELETE" => HttpClient.Method.Delete,
			_ => HttpClient.Method.Get,
		};
	}
}

/// <summary>服务端返回的业务错误或网络错误。</summary>
public class NetworkException : Exception
{
	public NetworkException(string message)
		: base(message)
	{
	}
}

/// <summary>PvP 流程场景间传递的临时状态。</summary>
public static class PvpFlowState
{
	public static string PendingRoomId = "";
	public static string PendingBattleId = "";
	public static bool PvpBattle = false;
	/// <summary>登录成功后跳转的路径；留空表示进入主菜单。</summary>
	public static string LoginReturnPath = "";
}

/// <summary>PvP 房间缓存的地图 JSON，双方进入战斗前共用。</summary>
public static class PvpMapState
{
	public static string MapJson = "";
	public static string MapName = "";
	public static string PendingUploadFileName = "";
	public static string PendingUploadRoomId = "";
}

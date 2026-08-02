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

	public string HttpBaseUrl { get; set; } = DefaultHttpBaseUrl;
	public string Token { get; private set; } = "";
	public string Username { get; private set; } = "";
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
	private double _reconnectTimer;

	private const double ReconnectIntervalSeconds = 2.0;

	public bool IsWebSocketConnected => _wsConnected;

	[Signal] public delegate void AuthChangedEventHandler(bool loggedIn, string username);
	[Signal] public delegate void ConnectionStateChangedEventHandler(bool connected);
	[Signal] public delegate void WsMessageReceivedEventHandler(string json);
	[Signal] public delegate void WsClosedEventHandler(int code, string reason);

	public override void _Ready()
	{
		Instance = this;
		_ws = new WebSocketPeer();
	}

	public override void _Process(double delta)
	{
		if (_ws == null)
		{
			return;
		}

		try
		{
			_ws.Poll();
			var state = _ws.GetReadyState();
			if (state == WebSocketPeer.State.Open)
			{
				if (!_wsConnected)
				{
					_wsConnected = true;
					_wsAuthenticated = false;
					_reconnectTimer = 0;
					EmitSignal(SignalName.ConnectionStateChanged, true);
				}

				if (!_wsAuthenticated && IsLoggedIn)
				{
					_wsAuthenticated = true;
					_ws.SendText($"{{\"type\":\"auth\",\"token\":\"{Token}\"}}");
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
				EmitSignal(SignalName.WsClosed, code, reason);
				EmitSignal(SignalName.ConnectionStateChanged, false);
			}
		}
		catch
		{
			_wsConnected = false;
			_wsShouldConnect = false;
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
		_ws.ConnectToUrl(WsUrl);
		_wsShouldConnect = true;
		_wsAuthenticated = false;
	}

	public void SendWebSocket(string json)
	{
		if (_wsConnected && _ws != null)
		{
			_ws.SendText(json);
		}
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
		_wsConnected = false;
		_wsShouldConnect = false;
		_wsAuthenticated = false;
		_reconnectTimer = 0;
		if (_ws != null)
		{
			_ws.Close();
		}
		EmitSignal(SignalName.AuthChanged, false, "");
	}

	public async Task<JsonElement> RegisterAsync(string username, string password)
	{
		JsonElement result = await RequestAsync(
			"/api/auth/register",
			"POST",
			new { username, password },
			false);
		ApplySession(result);
		return result;
	}

	public async Task<JsonElement> LoginAsync(string username, string password)
	{
		JsonElement result = await RequestAsync(
			"/api/auth/login",
			"POST",
			new { username, password },
			false);
		ApplySession(result);
		return result;
	}

	public async Task<JsonElement> GetMeAsync()
	{
		return await RequestAsync("/api/me", "GET");
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

		EmitSignal(SignalName.AuthChanged, IsLoggedIn, Username);
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
}

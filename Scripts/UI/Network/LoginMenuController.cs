using Godot;
using System;
using System.Threading.Tasks;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>全局登录 / 注册界面：可编辑服务器地址；本地 token 有效时自动进入主菜单或 PvP 大厅。</summary>
public partial class LoginMenuController : Control
{
	[Export] public string LobbyMenuPath = "res://Scenes/UI/Network/lobby_menu.tscn";
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private LineEdit _serverEdit;
	private LineEdit _emailEdit;
	private LineEdit _usernameEdit;
	private LineEdit _passwordEdit;
	private Label _statusLabel;
	private Button _loginButton;
	private Button _registerButton;

	public override void _Ready()
	{
		BuildUi();
		_serverEdit.Text = NetworkClient.Instance.HttpBaseUrl;
		_ = TryAutoLoginAsync();
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

		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(480, 0) };
		center.AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 14);
		panel.AddChild(box);

		var title = new Label
		{
			Text = "Dreadnought Departure 登录",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(title);

		_serverEdit = MakeLineEdit("服务器地址", NetworkClient.DefaultHttpBaseUrl);
		box.AddChild(_serverEdit);

		_emailEdit = MakeLineEdit("邮箱", "");
		box.AddChild(_emailEdit);

		_usernameEdit = MakeLineEdit("用户名", "admiral");
		box.AddChild(_usernameEdit);

		_passwordEdit = MakeLineEdit("密码", "secret1");
		_passwordEdit.Secret = true;
		box.AddChild(_passwordEdit);

		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 10);
		_loginButton = MakeButton("登录", () => _ = OnLoginPressed());
		_registerButton = MakeButton("注册", () => _ = OnRegisterPressed());
		buttons.AddChild(_loginButton);
		buttons.AddChild(_registerButton);
		box.AddChild(buttons);

		if (!string.IsNullOrEmpty(PvpFlowState.LoginReturnPath))
		{
			box.AddChild(MakeButton("返回主菜单", () =>
			{
				PvpFlowState.LoginReturnPath = "";
				GetTree().ChangeSceneToFile(MainMenuPath);
			}));
		}

		_statusLabel = new Label
		{
			Text = "登录或注册后进入游戏；本地 token 有效时自动恢复",
			HorizontalAlignment = HorizontalAlignment.Center,
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		_statusLabel.AddThemeFontSizeOverride("font_size", 14);
		box.AddChild(_statusLabel);
	}

	private async Task OnLoginPressed()
	{
		await AuthenticateAsync(false);
	}

	private async Task OnRegisterPressed()
	{
		await AuthenticateAsync(true);
	}

	private async Task AuthenticateAsync(bool register)
	{
		SetBusy(true);
		try
		{
			NetworkClient client = NetworkClient.Instance;
			client.HttpBaseUrl = _serverEdit.Text.Trim().TrimEnd('/');
			if (register)
			{
				await client.RegisterAsync(
					_emailEdit.Text.Trim(),
					_usernameEdit.Text.Trim(),
					_passwordEdit.Text);
			}
			else
			{
				await client.LoginAsync(
					_emailEdit.Text.Trim(),
					_usernameEdit.Text.Trim(),
					_passwordEdit.Text);
			}

			_statusLabel.Text = register ? "注册成功，进入游戏..." : "登录成功，进入游戏...";
			GetTree().ChangeSceneToFile(NextSceneAfterLogin());
		}
		catch (Exception ex)
		{
			_statusLabel.Text = ex is NetworkException ? $"失败：{ex.Message}" : $"失败：{ex.Message}";
		}
		finally
		{
			SetBusy(false);
		}
	}

	private void SetBusy(bool busy)
	{
		_loginButton.Disabled = busy;
		_registerButton.Disabled = busy;
		_serverEdit.Editable = !busy;
		_emailEdit.Editable = !busy;
		_usernameEdit.Editable = !busy;
		_passwordEdit.Editable = !busy;
	}

	private async Task TryAutoLoginAsync()
	{
		NetworkClient client = NetworkClient.Instance;
		if (!client.IsLoggedIn)
		{
			return;
		}
		try
		{
			await client.GetMeAsync();
			_statusLabel.Text = "已恢复登录，自动进入游戏...";
			GetTree().ChangeSceneToFile(NextSceneAfterLogin());
		}
		catch
		{
			client.Logout();
			_statusLabel.Text = "本地登录已失效，请重新登录";
		}
	}

	private static string NextSceneAfterLogin()
	{
		string target = string.IsNullOrEmpty(PvpFlowState.LoginReturnPath)
			? ""
			: PvpFlowState.LoginReturnPath;
		PvpFlowState.LoginReturnPath = "";
		return string.IsNullOrEmpty(target)
			? "res://Scenes/UI/Menu/MainMenu/main_menu.tscn"
			: target;
	}

	private static LineEdit MakeLineEdit(string placeholder, string value)
	{
		return new LineEdit
		{
			PlaceholderText = placeholder,
			Text = value,
			CustomMinimumSize = new Vector2(0, 44),
		};
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

using Godot;
using System;
using System.Threading.Tasks;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>PvP 登录 / 注册界面：可编辑服务器地址，成功后进入大厅。</summary>
public partial class LoginMenuController : Control
{
	[Export] public string LobbyMenuPath = "res://Scenes/UI/Network/lobby_menu.tscn";
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private LineEdit _serverEdit;
	private LineEdit _usernameEdit;
	private LineEdit _passwordEdit;
	private Label _statusLabel;
	private Button _loginButton;
	private Button _registerButton;

	public override void _Ready()
	{
		BuildUi();
		_serverEdit.Text = NetworkClient.Instance.HttpBaseUrl;
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
			Text = "PvP 登录 / 注册",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		box.AddChild(title);

		_serverEdit = MakeLineEdit("服务器地址", NetworkClient.DefaultHttpBaseUrl);
		box.AddChild(_serverEdit);

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

		box.AddChild(MakeButton("返回主菜单", () => GetTree().ChangeSceneToFile(MainMenuPath)));

		_statusLabel = new Label
		{
			Text = "连接本机虚拟机服务器，或改为自建服务器地址",
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
				await client.RegisterAsync(_usernameEdit.Text.Trim(), _passwordEdit.Text);
			}
			else
			{
				await client.LoginAsync(_usernameEdit.Text.Trim(), _passwordEdit.Text);
			}

			_statusLabel.Text = register ? "注册成功，进入大厅..." : "登录成功，进入大厅...";
			GetTree().ChangeSceneToFile(LobbyMenuPath);
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
		_usernameEdit.Editable = !busy;
		_passwordEdit.Editable = !busy;
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

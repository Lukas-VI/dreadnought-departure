using Godot;
using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.UI.Network;

/// <summary>网游中心：个人资料、背包、抽卡、商店、邮件。</summary>
public partial class NetworkCenterController : Control
{
	[Export] public string MainMenuPath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string PvpLoginMenuPath = "res://Scenes/UI/Network/login_menu.tscn";

	private Label _statusLabel;
	private Label _titleLabel;
	private VBoxContainer _content;
	private string _currentTab = "profile";

	public override void _Ready()
	{
		BuildUi();
		_ = LoadTabAsync("profile");
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

		var top = new HBoxContainer();
		top.SetAnchorsPreset(LayoutPreset.TopWide);
		top.OffsetTop = 16;
		top.OffsetBottom = 64;
		top.AddThemeConstantOverride("separation", 16);
		AddChild(top);

		_titleLabel = new Label { Text = "网游中心" };
		_titleLabel.AddThemeFontSizeOverride("font_size", 26);
		top.AddChild(_titleLabel);

		_statusLabel = new Label { Text = "", Modulate = new Color(0.8f, 0.9f, 1f) };
		top.AddChild(_statusLabel);

		var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		top.AddChild(spacer);
		top.AddChild(MakeButton("返回主菜单", () => GetTree().ChangeSceneToFile(MainMenuPath)));
		top.AddChild(MakeButton("退出登录", () =>
		{
			NetworkClient.Instance.Logout();
			GetTree().ChangeSceneToFile(PvpLoginMenuPath);
		}));

		var body = new HBoxContainer();
		body.SetAnchorsPreset(LayoutPreset.FullRect);
		body.OffsetTop = 78;
		body.OffsetBottom = -12;
		body.AddThemeConstantOverride("separation", 14);
		AddChild(body);

		var sidebar = new VBoxContainer { CustomMinimumSize = new Vector2(180, 0) };
		sidebar.AddThemeConstantOverride("separation", 8);
		body.AddChild(sidebar);
		AddTabButton(sidebar, "个人中心", "profile");
		AddTabButton(sidebar, "背包", "backpack");
		AddTabButton(sidebar, "抽卡", "gacha");
		AddTabButton(sidebar, "商店", "shop");
		AddTabButton(sidebar, "邮件", "mail");

		var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		body.AddChild(panel);
		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 18);
		margin.AddThemeConstantOverride("margin_right", 18);
		margin.AddThemeConstantOverride("margin_top", 14);
		margin.AddThemeConstantOverride("margin_bottom", 14);
		panel.AddChild(margin);
		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		margin.AddChild(scroll);
		_content = new VBoxContainer();
		_content.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		_content.AddThemeConstantOverride("separation", 10);
		scroll.AddChild(_content);
	}

	private void AddTabButton(VBoxContainer sidebar, string text, string tab)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0, 44),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
		};
		button.Pressed += () => _ = LoadTabAsync(tab);
		sidebar.AddChild(button);
	}

	private async Task LoadTabAsync(string tab)
	{
		_currentTab = tab;
		ClearContent();
		_statusLabel.Text = "加载中...";
		try
		{
			switch (tab)
			{
				case "profile": await LoadProfileAsync(); break;
				case "backpack": await LoadBackpackAsync(); break;
				case "gacha": await LoadGachaAsync(); break;
				case "shop": await LoadShopAsync(); break;
				case "mail": await LoadMailAsync(); break;
			}
			_statusLabel.Text = "";
		}
		catch (Exception ex)
		{
			AddLabel($"加载失败：{ex.Message}");
			_statusLabel.Text = "加载失败";
		}
	}

	private void ClearContent()
	{
		foreach (Node child in _content.GetChildren())
		{
			_content.RemoveChild(child);
			child.QueueFree();
		}
	}

	private async Task LoadProfileAsync()
	{
		JsonElement me = await NetworkClient.Instance.GetMeAsync();
		AddHeader("个人中心");
		AddLabel($"昵称：{Str(me, "nickname")}");
		AddLabel($"用户名：{Str(me, "username")}");
		AddLabel($"邮箱：{Str(me, "email")}");
		AddLabel($"点数：{me.GetProperty("credits").GetInt32()}");
		AddLabel($"背包物品：{me.GetProperty("itemCount").GetInt32()} 种");
		AddLabel($"未读邮件：{me.GetProperty("unreadMail").GetInt32()}");

		var edit = new LineEdit { Text = Str(me, "nickname"), CustomMinimumSize = new Vector2(0, 40) };
		_content.AddChild(edit);
		var save = MakeButton("保存昵称", () => _ = SaveNicknameAsync(edit.Text.Trim()));
		_content.AddChild(save);
	}

	private async Task SaveNicknameAsync(string nickname)
	{
		try
		{
			await NetworkClient.Instance.UpdateProfileAsync(nickname);
			_statusLabel.Text = "昵称已保存";
			await LoadTabAsync("profile");
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"保存失败：{ex.Message}";
		}
	}

	private async Task LoadBackpackAsync()
	{
		JsonElement data = await NetworkClient.Instance.GetBackpackAsync();
		AddHeader("背包");
		foreach (JsonElement item in data.GetProperty("items").EnumerateArray())
		{
			AddLabel($"{Str(item, "item_type")} / {Str(item, "item_id")} × {item.GetProperty("quantity").GetInt32()}");
		}
	}

	private async Task LoadGachaAsync()
	{
		JsonElement data = await NetworkClient.Instance.GetGachaPoolsAsync();
		AddHeader("抽卡");
		foreach (JsonElement pool in data.GetProperty("pools").EnumerateArray())
		{
			string poolId = Str(pool, "id");
			string poolName = Str(pool, "name");
			int cost = pool.GetProperty("costPerPull").GetInt32();
			AddHeader($"{poolName}（{cost} 点/抽）");
			var one = MakeButton("单抽", () => _ = PullAsync(poolId, 1));
			var ten = MakeButton("十连", () => _ = PullAsync(poolId, 10));
			var row = new HBoxContainer();
			row.AddChild(one);
			row.AddChild(ten);
			_content.AddChild(row);
		}
	}

	private async Task PullAsync(string poolId, int count)
	{
		try
		{
			var pull = await NetworkClient.Instance.PullGachaAsync(
				poolId,
				count,
				Guid.NewGuid().ToString("N"));
			var result = new RichTextLabel
			{
				BbcodeEnabled = false,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				CustomMinimumSize = new Vector2(0, 120),
			};
			foreach (JsonElement item in pull.GetProperty("items").EnumerateArray())
			{
				result.Text += $"{Str(item, "name")} [{Str(item, "rarity")}]\n";
			}
			_content.AddChild(result);
			_statusLabel.Text = $"抽卡完成，剩余点数 {pull.GetProperty("creditsLeft").GetInt32()}";
			await LoadTabAsync("backpack");
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"抽卡失败：{ex.Message}";
		}
	}

	private async Task LoadShopAsync()
	{
		JsonElement data = await NetworkClient.Instance.GetShopCatalogAsync();
		AddHeader("商店");
		foreach (JsonElement item in data.GetProperty("items").EnumerateArray())
		{
			string itemId = Str(item, "id");
			string itemName = Str(item, "name");
			int cost = item.GetProperty("cost").GetInt32();
			AddLabel($"{itemName}（{cost} 点） - {Str(item, "description")}");
			_content.AddChild(MakeButton($"购买 {itemName}", () => _ = BuyAsync(itemId)));
		}
	}

	private async Task BuyAsync(string itemId)
	{
		try
		{
			var result = await NetworkClient.Instance.BuyShopItemAsync(itemId);
			_statusLabel.Text = $"购买成功，剩余点数 {result.GetProperty("credits").GetInt32()}";
			await LoadTabAsync("shop");
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"购买失败：{ex.Message}";
		}
	}

	private async Task LoadMailAsync()
	{
		JsonElement data = await NetworkClient.Instance.GetMailAsync();
		AddHeader("邮件");
		foreach (JsonElement mail in data.GetProperty("mails").EnumerateArray())
		{
			string mailId = Str(mail, "id");
			AddHeader($"[{(mail.GetProperty("isRead").GetBoolean() ? "已读" : "未读")}] {Str(mail, "title")}");
			AddLabel(Str(mail, "body"));
			var row = new HBoxContainer();
			row.AddChild(MakeButton("标记已读", () => _ = ReadMailAsync(mailId)));
			if (!mail.GetProperty("claimed").GetBoolean())
			{
				row.AddChild(MakeButton("领取附件", () => _ = ClaimMailAsync(mailId)));
			}
			_content.AddChild(row);
		}
	}

	private async Task ReadMailAsync(string mailId)
	{
		try
		{
			await NetworkClient.Instance.ReadMailAsync(mailId);
			await LoadTabAsync("mail");
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"操作失败：{ex.Message}";
		}
	}

	private async Task ClaimMailAsync(string mailId)
	{
		try
		{
			await NetworkClient.Instance.ClaimMailAsync(mailId);
			_statusLabel.Text = "附件已领取";
			await LoadTabAsync("mail");
		}
		catch (Exception ex)
		{
			_statusLabel.Text = $"领取失败：{ex.Message}";
		}
	}

	private void AddHeader(string text)
	{
		var label = new Label { Text = text };
		label.AddThemeFontSizeOverride("font_size", 20);
		_content.AddChild(label);
	}

	private void AddLabel(string text)
	{
		_content.AddChild(new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart });
	}

	private static Button MakeButton(string text, Action pressed)
	{
		var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
		button.Pressed += pressed;
		return button;
	}

	private static string Str(JsonElement element, string property)
		=> element.TryGetProperty(property, out JsonElement value) ? value.GetString() ?? "" : "";
}

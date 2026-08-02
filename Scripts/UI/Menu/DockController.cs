using Godot;
using System;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 船坞控制器：列出 ShipCatalog 中所有 ShipData，左右按钮切换角色并展示数据卡。
/// </summary>
public partial class DockController : Control
{
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private ShipCatalog.Entry[] _entries;
	private int _index;
	private Label _titleLabel;
	private Label _infoLabel;
	private TextureRect _portrait;

	public override void _Ready()
	{
		_entries = ShipCatalog.Entries.ToArray();
		BuildUi();
		Refresh();
	}

	private void BuildUi()
	{
		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(860, 660) };
		center.AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 12);
		panel.AddChild(box);

		_titleLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
		_titleLabel.AddThemeFontSizeOverride("font_size", 30);
		box.AddChild(_titleLabel);

		var nav = new HBoxContainer();
		nav.Alignment = BoxContainer.AlignmentMode.Center;
		nav.AddThemeConstantOverride("separation", 16);
		nav.AddChild(MakeButton("◀ 上一个", ShowPrev));
		nav.AddChild(MakeButton("下一个 ▶", ShowNext));
		box.AddChild(nav);

		var content = new HBoxContainer();
		content.AddThemeConstantOverride("separation", 20);
		_portrait = new TextureRect
		{
			CustomMinimumSize = new Vector2(220, 280),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		};
		content.AddChild(_portrait);

		var scroll = new ScrollContainer
		{
			CustomMinimumSize = new Vector2(540, 420),
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		_infoLabel = new Label
		{
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(520, 0)
		};
		_infoLabel.AddThemeFontSizeOverride("font_size", 17);
		scroll.AddChild(_infoLabel);
		content.AddChild(scroll);
		box.AddChild(content);

		box.AddChild(MakeButton("返回主菜单", () => GetTree().ChangeSceneToFile(MainMenuScenePath)));
	}

	private void Refresh()
	{
		if (_entries == null || _entries.Length == 0)
		{
			_titleLabel.Text = "船坞";
			_infoLabel.Text = "暂无角色数据";
			return;
		}

		_index = (_index + _entries.Length) % _entries.Length;
		var entry = _entries[_index];
		var data = entry.Data;
		_titleLabel.Text = $"船坞 {_index + 1}/{_entries.Length} · {entry.DisplayName}";
		_portrait.Texture = data?.Portrait;
		_portrait.Visible = data?.Portrait != null;
		if (data == null)
		{
			_infoLabel.Text = "缺少 ShipData 资源";
			return;
		}

		_infoLabel.Text =
			$"舰种：{data.ShipClass}    ID：{entry.ShipId}\n" +
			$"阵营：{data.Camp}    解锁：{(data.IsUnlocked ? "已解锁" : "未解锁")}    成本：{data.Cost}\n\n" +
			$"PV：{data.PV}    最大HP：{data.MaxHp}    最大航速：{data.MaxSpeed}\n" +
			$"主炮：{data.ForwardFire} / {data.SideFire} / {data.BackwardFire}（前/侧/后）\n" +
			$"副炮：{data.SecondaryForwardFire} / {data.SecondarySideFire} / {data.SecondaryBackwardFire}（前/侧/后）\n" +
			$"鱼雷：左 {data.TorpedoLeftTubes} / 中 {data.TorpedoCenterTubes} / 右 {data.TorpedoRightTubes}" +
			$"{(data.HasSpareTorpedoes ? "（备用鱼雷）" : "")}\n" +
			$"装甲：近 {data.ArmorClose} / 中 {data.ArmorMedium} / 远 {data.ArmorFar}\n" +
			$"雷达：{(string.IsNullOrEmpty(data.RadarType) ? "无" : data.RadarType)}" +
			$"    技能：{(data.SkillIds == null || data.SkillIds.Length == 0 ? "无" : string.Join("、", data.SkillIds))}\n\n" +
			$"背景故事：\n{data.Background}";
	}

	private void ShowPrev() { _index--; Refresh(); }
	private void ShowNext() { _index++; Refresh(); }

	private Button MakeButton(string text, Action action)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(150, 46)
		};
		button.AddThemeFontSizeOverride("font_size", 18);
		button.Pressed += action;
		return button;
	}
}

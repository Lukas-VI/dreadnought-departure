using Godot;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 船坞控制器：使用场景节点组织布局。
/// 左侧信息面板、右侧立绘区、顶部栏与底部占位栏，立绘两侧按钮切换角色。
/// </summary>
public partial class DockController : Control
{
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";

	private ShipCatalog.Entry[] _entries;
	private int _index;
	private Label _titleLabel;
	private Label _nameLabel;
	private Label _infoLabel;
	private Label _statusLabel;
	private TextureRect _portrait;
	private Label _placeholderLabel;
	private ColorRect _appreciationOverlay;
	private TextureRect _appreciationPortrait;
	private Label _appreciationPlaceholder;
	private CardController _card;
	private bool _appreciationMode;

	public override void _Ready()
	{
		CallDeferred(nameof(Initialize));
	}

	private void Initialize()
	{
		_titleLabel = GetNode<Label>("TopBar/Margin/HBox/TitleLabel");
		_nameLabel = GetNode<Label>("Content/HBox/LeftPanel/Margin/VBox/NameLabel");
		_infoLabel = GetNode<Label>("Content/HBox/LeftPanel/Margin/VBox/InfoScroll/InfoLabel");
		_statusLabel = GetNode<Label>("BottomBar/Margin/HBox/StatusLabel");
		_portrait = GetNode<TextureRect>("Content/HBox/PortraitArea/HBox/PortraitCenter/PortraitBox/PortraitTexture");
		_placeholderLabel = GetNode<Label>("Content/HBox/PortraitArea/HBox/PortraitCenter/PortraitBox/PlaceholderLabel");
		_appreciationOverlay = GetNode<ColorRect>("AppreciationOverlay");
		_appreciationPortrait = GetNode<TextureRect>("AppreciationOverlay/Center/PortraitBox/PortraitTexture");
		_appreciationPlaceholder = GetNode<Label>("AppreciationOverlay/Center/PortraitBox/PlaceholderLabel");
		_card = GetNode<CardController>("Content/HBox/LeftPanel/Margin/VBox/Card");

		_entries = ShipCatalog.Entries.ToArray();
		Refresh();
	}

	private void Refresh()
	{
		if (_entries == null || _entries.Length == 0)
		{
			_nameLabel.Text = "船坞";
			_infoLabel.Text = "暂无角色数据";
			return;
		}

		_index = (_index + _entries.Length) % _entries.Length;
		var entry = _entries[_index];
		var data = entry.Data;
		_titleLabel.Text = $"船坞 {_index + 1}/{_entries.Length} ·";
		_nameLabel.Text = $"{entry.DisplayName}";
		_portrait.Texture = data?.Portrait;
		_portrait.Visible = data?.Portrait != null;
		_placeholderLabel.Visible = data?.Portrait == null;
		if (_appreciationPortrait != null)
		{
			_appreciationPortrait.Texture = data?.Portrait;
			_appreciationPlaceholder.Visible = data?.Portrait == null;
		}
		if (data == null)
		{
			_infoLabel.Text = "缺少 ShipData 资源";
			return;
		}

		_infoLabel.Text =
			$"舰种：{data.ShipClass}    \nID：{entry.ShipId}\n稀有度：{data.Rarity}\n" +
			$"阵营：{data.Camp}    \n解锁：{(data.IsUnlocked ? "已解锁" : "未解锁")}    \n成本：{data.Cost}\n\n" +
			$"PV：{data.PV}    \n最大HP：{data.MaxHp}    \n最大航速：{data.MaxSpeed}\n" +
			$"主炮：{data.ForwardFire} / {data.SideFire} / {data.BackwardFire}（前/侧/后）\n" +
			$"副炮：{data.SecondaryForwardFire} / {data.SecondarySideFire} / {data.SecondaryBackwardFire}（前/侧/后）\n" +
			$"鱼雷：左 {data.TorpedoLeftTubes} / 中 {data.TorpedoCenterTubes} / 右 {data.TorpedoRightTubes}" +
			$"{(data.HasSpareTorpedoes ? "（备用鱼雷）" : "")}\n" +
			$"装甲：近 {data.ArmorClose} / 中 {data.ArmorMedium} / 远 {data.ArmorFar}\n" +
			$"雷达：{(string.IsNullOrEmpty(data.RadarType) ? "无" : data.RadarType)}\n" +
			$"技能：{(data.SkillIds == null || data.SkillIds.Length == 0 ? "无" : string.Join("、", data.SkillIds))}\n\n" +
			$"背景故事：\n{data.Background}";

		_card.SetName(entry.DisplayName);
		_card.SetType(data.ShipClass);
		_card.SetAttr($"{data.ArmorClose}-{data.ArmorMedium}-{data.ArmorFar}");
		
	}

	private void ShowPrev() { _index--; Refresh(); }
	private void ShowNext() { _index++; Refresh(); }

	public void _OnBackPressed() => GetTree().ChangeSceneToFile(MainMenuScenePath);
	public void _OnPrevPressed() => ShowPrev();
	public void _OnNextPressed() => ShowNext();
	public void _OnCodexPressed() => SetStatus("图鉴（占位）");
	public void _OnCampPressed() => SetStatus("阵营筛选（占位）");
	public void _OnAppreciationPressed() => EnterAppreciation();
	public void _OnAppreciationReturnPressed() => ExitAppreciation();

	private void EnterAppreciation()
	{
		if (_appreciationMode) return;
		_appreciationMode = true;
		_appreciationOverlay.Visible = true;
		_appreciationOverlay.Modulate = new Color(1f, 1f, 1f, 0f);
		_appreciationPortrait.Scale = Vector2.One * 0.72f;

		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.Out);
		tween.Parallel().TweenProperty(_appreciationOverlay, "modulate:a", 1f, 0.3f);
		tween.Parallel().TweenProperty(_appreciationPortrait, "scale", Vector2.One, 0.45f);
	}

	private void ExitAppreciation()
	{
		if (!_appreciationMode) return;
		var tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Cubic);
		tween.SetEase(Tween.EaseType.In);
		tween.TweenProperty(_appreciationOverlay, "modulate:a", 0f, 0.25f);
		tween.TweenCallback(Callable.From(() =>
		{
			_appreciationOverlay.Visible = false;
			_appreciationMode = false;
		}));
	}

	private void SetStatus(string text)
	{
		if (_statusLabel != null)
			_statusLabel.Text = text;
	}
}

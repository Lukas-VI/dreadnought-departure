using Godot;
using System;
using System.Collections.Generic;
using DreadnoughtDeparture.Network;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 画布选择菜单（Control）。
/// 主菜单的“地图编辑器”与“战役”都先进入本页，从 export/maps 选择画布 JSON；
/// 编辑器模式进入 editor_scene，战役模式进入 battle_scene，并把选中画布名交给 LevelDataManager。
/// </summary>
public partial class MapSelectMenuController : Control
{
	/// <summary>主菜单写入的模式：editor / campaign；进入本场景后消费并清空。</summary>
	public static string PendingMode = "editor";

	[Export] public string EditorScenePath = "res://Scenes/UI/Editor/editor_scene.tscn";
	[Export] public string BattleScenePath = "res://Scenes/Battle/battle_scene.tscn";
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string PvpLobbyMenuPath = "res://Scenes/UI/Network/lobby_menu.tscn";

	private string _mode = "editor";
	private ItemList _list;
	private Label _titleLabel;
	private Button _editorButton;
	private Button _campaignButton;
	private AcceptDialog _newDialog;
	private LineEdit _newNameEdit;
	private ConfirmationDialog _deleteDialog;
	private FileDialog _importDialog;
	private string _pendingDelete;
	private OptionButton _newOrientation;
	private SpinBox _playerCommandSpin;
	private SpinBox _enemyCommandSpin;
	private SpinBox _playerCPSpin;
	private SpinBox _enemyCPSpin;
	private SpinBox _initiativeSpin;
	private SpinBox _visionSpin;
	private SpinBox _maxTurnsSpin;
	private OptionButton _mapTypeOption;
	private OptionButton _initiativeOwnerOption;
	private SpinBox _torpedoModePlayerSpin;
	private SpinBox _torpedoModeEnemySpin;
	private SpinBox[] _phaseSecondsSpins;
	private SpinBox _phaseExtraSpin;
	private CheckBox _torpedoEnabledCheck;
	private CheckBox _gunfireEnabledCheck;

	public override void _Ready()
	{
		_mode = string.IsNullOrEmpty(PendingMode) ? "editor" : PendingMode;
		PendingMode = null;
		// 清掉上一次遗留的画布请求，避免未选择就切走时污染下一个场景
		LevelDataManager.RuntimeMapRequest = null;
		BuildUi();
		BuildDialogs();
		RefreshList();
	}

	/// <summary>构建居中面板：标题、画布列表、模式相关按钮与通用管理按钮。</summary>
	private void BuildUi()
	{
		var center = new CenterContainer();
		center.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(center);

		var panel = new PanelContainer { CustomMinimumSize = new Vector2(680, 600) };
		center.AddChild(panel);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 14);
		panel.AddChild(box);

		_titleLabel = new Label
		{
			Text = _mode == "campaign"
				? "战役加载 - 选择画布"
				: _mode == "pvp"
					? "PvP 房主 - 选择地图"
					: "画布列表 - 选择画布",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		_titleLabel.AddThemeFontSizeOverride("font_size", 26);
		box.AddChild(_titleLabel);

		var hint = new Label { Text = "画布保存于 export/maps/*.json" };
		hint.AddThemeFontSizeOverride("font_size", 13);
		box.AddChild(hint);

		_list = new ItemList { CustomMinimumSize = new Vector2(0, 340) };
		_list.ItemActivated += _ => OpenSelected();
		box.AddChild(_list);

		var buttons = new HBoxContainer();
		buttons.AddThemeConstantOverride("separation", 8);

		_editorButton = MakeButton("编辑画布", OpenSelected);
		_campaignButton = MakeButton("开始战役", OpenSelected);
		buttons.AddChild(_editorButton);
		buttons.AddChild(_campaignButton);
		buttons.AddChild(MakeButton("新建", ShowNewDialog));
		buttons.AddChild(MakeButton("导入", () => _importDialog?.PopupCentered()));
		buttons.AddChild(MakeButton("删除", OnDeletePressed));
		box.AddChild(buttons);

		box.AddChild(MakeButton("返回主菜单", () => GetTree().ChangeSceneToFile(MainMenuScenePath)));

		_editorButton.Visible = _mode != "campaign";
		_campaignButton.Visible = _mode == "campaign";
		if (_mode == "pvp")
		{
			_editorButton.Visible = false;
			_campaignButton.Visible = true;
			_campaignButton.Text = "选择此地图";
		}
	}

	/// <summary>构建新建、删除、导入三个弹窗。</summary>
	private void BuildDialogs()
	{
		_newDialog = new AcceptDialog { Title = "新建画布" };
		_newNameEdit = new LineEdit
		{
			PlaceholderText = "画布名（如 map_02）",
			CustomMinimumSize = new Vector2(320, 0)
		};
		_newOrientation = new OptionButton();
		_newOrientation.AddItem("E/W 水平向（当前）", 0);
		_newOrientation.AddItem("N/S 竖直向", 1);
		_newOrientation.Selected = 0;
		var content = new VBoxContainer { CustomMinimumSize = new Vector2(360, 0) };
		content.AddChild(_newNameEdit);
		content.AddChild(_newOrientation);
		content.AddChild(new Label { Text = "关卡初设（占位）" });
		_playerCommandSpin = MakeSpinBox(" 玩家指挥值", 1, 20, 5);
		_enemyCommandSpin = MakeSpinBox(" 敌方指挥值", 1, 20, 4);
		_playerCPSpin = MakeSpinBox(" 玩家初设CP", 0, 99, 8);
		_enemyCPSpin = MakeSpinBox(" 敌方初设CP", 0, 99, 8);
		_initiativeSpin = MakeSpinBox(" 主动权值", 1, 10, 5);
		_visionSpin = MakeSpinBox(" 基本视野", 1, 24, 6);
		_maxTurnsSpin = MakeSpinBox(" 回合数", 1, 99, 18);
		content.AddChild(_playerCommandSpin);
		content.AddChild(_enemyCommandSpin);
		content.AddChild(_playerCPSpin);
		content.AddChild(_enemyCPSpin);
		content.AddChild(_initiativeSpin);
		content.AddChild(_visionSpin);
		content.AddChild(_maxTurnsSpin);

		var phaseHeader = new Label { Text = "阶段限时（每船秒数）" };
		content.AddChild(phaseHeader);
		var phaseScroll = new ScrollContainer { CustomMinimumSize = new Vector2(360, 230) };
		var phaseBox = new VBoxContainer { CustomMinimumSize = new Vector2(340, 0) };
		phaseScroll.AddChild(phaseBox);
		int[] defaults = { 5, 5, 5, 5, 5, 10, 10, 0 };
		string[] phaseNames =
		{
			"速度调整", "第一移动", "第二移动", "第三移动", "视野", "炮击", "鱼雷", "结算"
		};
		_phaseSecondsSpins = new SpinBox[phaseNames.Length];
		for (int i = 0; i < phaseNames.Length; i++)
		{
			var row = new HBoxContainer();
			row.AddChild(new Label { Text = phaseNames[i], CustomMinimumSize = new Vector2(96, 0) });
			_phaseSecondsSpins[i] = MakeSpinBox(" 秒/船", 0, 60, defaults[i]);
			row.AddChild(_phaseSecondsSpins[i]);
			phaseBox.AddChild(row);
		}
		_phaseExtraSpin = MakeSpinBox(" 阶段额外秒数", 0, 60, 5);
		phaseBox.AddChild(_phaseExtraSpin);
		_torpedoEnabledCheck = new CheckBox { Text = "启用鱼雷阶段" };
		phaseBox.AddChild(_torpedoEnabledCheck);
		_gunfireEnabledCheck = new CheckBox { Text = "启用炮击阶段", ButtonPressed = true };
		phaseBox.AddChild(_gunfireEnabledCheck);
		_mapTypeOption = new OptionButton();
		_mapTypeOption.AddItem("地图类型：昼战", 0);
		_mapTypeOption.AddItem("地图类型：夜战", 1);
		_mapTypeOption.Selected = 0;
		phaseBox.AddChild(_mapTypeOption);
		_initiativeOwnerOption = new OptionButton();
		_initiativeOwnerOption.AddItem("主动权：玩家", 0);
		_initiativeOwnerOption.AddItem("主动权：敌方", 1);
		_initiativeOwnerOption.Selected = 0;
		phaseBox.AddChild(_initiativeOwnerOption);
		_torpedoModePlayerSpin = MakeSpinBox(" 鱼雷模式(玩家)", 0, 10, 7);
		_torpedoModeEnemySpin = MakeSpinBox(" 鱼雷模式(敌方)", 0, 10, 4);
		phaseBox.AddChild(_torpedoModePlayerSpin);
		phaseBox.AddChild(_torpedoModeEnemySpin);
		content.AddChild(phaseScroll);
		_newDialog.AddChild(content);
		_newDialog.Confirmed += ConfirmNewCanvas;
		AddChild(_newDialog);

		_deleteDialog = new ConfirmationDialog { DialogText = "确定删除画布？" };
		_deleteDialog.Confirmed += () =>
		{
			if (string.IsNullOrEmpty(_pendingDelete)) return;
			DirAccess.RemoveAbsolute($"{LevelDataManager.DefaultExportFolder}/{_pendingDelete}");
			RefreshList();
		};
		AddChild(_deleteDialog);

		_importDialog = new FileDialog
		{
			Access = FileDialog.AccessEnum.Filesystem,
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Title = "导入画布 JSON"
		};
		_importDialog.AddFilter("*.json", "地图 JSON");
		_importDialog.FileSelected += ImportCanvas;
		AddChild(_importDialog);
	}

	/// <summary>刷新导出目录里的画布列表。</summary>
	private void RefreshList()
	{
		if (_list == null) return;
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		DirAccess dir = DirAccess.Open(LevelDataManager.DefaultExportFolder);
		_list.Clear();
		if (dir == null) return;

		var names = new List<string>();
		foreach (string file in dir.GetFiles())
			if (file.EndsWith(".json")) names.Add(file);
		names.Sort();
		foreach (string name in names) _list.AddItem(name);
	}

	/// <summary>把选中画布交给 LevelDataManager 并按模式切换场景。</summary>
	private void OpenSelected()
	{
		if (_list.GetSelectedItems().Length == 0) return;
		string fileName = _list.GetItemText(_list.GetSelectedItems()[0]);
		if (_mode == "pvp")
		{
			PvpMapState.PendingUploadFileName = fileName;
			GetTree().ChangeSceneToFile(PvpLobbyMenuPath);
			return;
		}
		LevelDataManager.RuntimeMapRequest = fileName;
		if (_mode == "campaign")
		{
			LevelDataManager.ActiveCampaignMap = fileName;
			GetTree().ChangeSceneToFile(BattleScenePath);
		}
		else
		{
			GetTree().ChangeSceneToFile(EditorScenePath);
		}
	}

	/// <summary>显示新建画布输入框。</summary>
	private void ShowNewDialog()
	{
		_newNameEdit.Text = "";
		_newDialog.PopupCentered();
		_newNameEdit.GrabFocus();
	}

	/// <summary>创建空白 v3 画布 JSON，并刷新列表。</summary>
	private void ConfirmNewCanvas()
	{
		string name = _newNameEdit.Text.Trim();
		if (string.IsNullOrEmpty(name)) return;
		string safe = name.Replace("\"", "").Replace("/", "_").Replace("\\", "_").Replace(":", "_");

		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		string path = $"{LevelDataManager.DefaultExportFolder}/{safe}.json";
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null) return;
		string orientation = _newOrientation.Selected == 1 ? "ns" : "ew";
		int pc = (int)_playerCommandSpin.Value, ec = (int)_enemyCommandSpin.Value;
		int pcp = (int)_playerCPSpin.Value, ecp = (int)_enemyCPSpin.Value;
		int ini = (int)_initiativeSpin.Value, vis = (int)_visionSpin.Value, turns = (int)_maxTurnsSpin.Value;
		int[] phaseSeconds = new int[_phaseSecondsSpins.Length];
		for (int i = 0; i < phaseSeconds.Length; i++) phaseSeconds[i] = (int)_phaseSecondsSpins[i].Value;
		int phaseExtra = (int)_phaseExtraSpin.Value;
		bool torpedoEnabled = _torpedoEnabledCheck.ButtonPressed;
		bool gunfireEnabled = _gunfireEnabledCheck.ButtonPressed;
		string mapType = _mapTypeOption.Selected == 1 ? "night" : "day";
		string initiativeOwner = _initiativeOwnerOption.Selected == 1 ? "enemy" : "player";
		int torpedoModePlayer = (int)_torpedoModePlayerSpin.Value;
		int torpedoModeEnemy = (int)_torpedoModeEnemySpin.Value;
		file.StoreString($"{{\"Name\":\"{safe}\",\"Version\":3,\"Orientation\":\"{orientation}\"," +
			$"\"MapType\":\"{mapType}\",\"PlayerCommand\":{pc},\"EnemyCommand\":{ec}," +
			$"\"PlayerInitialCP\":{pcp},\"EnemyInitialCP\":{ecp}," +
			$"\"InitiativeValue\":{ini},\"InitiativeOwner\":\"{initiativeOwner}\",\"BasicVision\":{vis}," +
			$"\"TorpedoModePlayer\":{torpedoModePlayer},\"TorpedoModeEnemy\":{torpedoModeEnemy},\"MaxTurns\":{turns}," +
			$"\"TorpedoPhaseEnabled\":{(torpedoEnabled ? "true" : "false")}," +
			$"\"GunfirePhaseEnabled\":{(gunfireEnabled ? "true" : "false")}," +
			$"\"PhaseSecondsPerShip\":[{string.Join(",", phaseSeconds)}],\"PhaseExtraSeconds\":{phaseExtra}," +
			$"\"Terrain\":{{}},\"Generation\":{{}},\"Special\":{{}},\"Ships\":{{}}}}");
		RefreshList();
	}

	/// <summary>把外部 JSON 复制到导出目录并刷新列表。</summary>
	private void ImportCanvas(string sourcePath)
	{
		string fileName = System.IO.Path.GetFileName(sourcePath);
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		if (DirAccess.CopyAbsolute(sourcePath, $"{LevelDataManager.DefaultExportFolder}/{fileName}") == Error.Ok)
			RefreshList();
	}

	/// <summary>弹出删除确认框。</summary>
	private void OnDeletePressed()
	{
		if (_list.GetSelectedItems().Length == 0) return;
		_pendingDelete = _list.GetItemText(_list.GetSelectedItems()[0]);
		_deleteDialog.DialogText = $"确定删除画布 {_pendingDelete} 吗？";
		_deleteDialog.PopupCentered();
	}

	/// <summary>创建带点击回调的 Button。</summary>
	private static Button MakeButton(string text, Action pressed)
	{
		var btn = new Button { Text = text };
		btn.Pressed += pressed;
		return btn;
	}

	private static SpinBox MakeSpinBox(string suffix, float min, float max, float value)
	{
		return new SpinBox
		{
			MinValue = min,
			MaxValue = max,
			Value = value,
			Suffix = suffix,
			CustomMinimumSize = new Vector2(360, 0)
		};
	}
}

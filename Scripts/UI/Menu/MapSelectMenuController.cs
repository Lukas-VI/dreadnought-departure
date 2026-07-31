using Godot;
using System;
using System.Collections.Generic;

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

	[Export] public string EditorScenePath = "res://Scenes/Editor/editor_scene.tscn";
	[Export] public string BattleScenePath = "res://Scenes/Battle/battle_scene.tscn";
	[Export] public string MainMenuScenePath = "res://Scenes/MainMenu/main_menu.tscn";

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
			Text = _mode == "campaign" ? "战役加载 - 选择画布" : "画布列表 - 选择画布",
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
		_newDialog.AddChild(_newNameEdit);
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
		file.StoreString($"{{\"Name\":\"{safe}\",\"Version\":3,\"Terrain\":{{}},\"Generation\":{{}},\"Special\":{{}},\"Ships\":{{}}}}");
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
}

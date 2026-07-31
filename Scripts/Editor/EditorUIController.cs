using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 编辑器 UI 控制器（CanvasLayer/EditorUI）。
/// 静态布局全部由 Scenes/Editor/editor_ui.tscn 组织，本类只负责节点绑定、
/// 数据填充与信号转发；画布数据读写委托给 LevelDataManager，
/// 图块操作委托给 MapCanvasController。
/// </summary>
public partial class EditorUIController : Control
{
	[Export] public EditorTileCatalog TileCatalog;
	[Export] public PackedScene PaletteSwatchScene;
	[Export] public PackedScene ShipRowScene;
	[Export] public string MainMenuScenePath = "res://Scenes/MainMenu/main_menu.tscn";

	private MapCanvasController _canvas;
	private LevelDataManager _data;

	private Label _canvasNameLabel;
	private Label _statusLabel;
	private Timer _statusTimer;
	private VBoxContainer _terrainEntries;
	private VBoxContainer _generationEntries;
	private VBoxContainer _specialEntries;
	private PanelContainer _rightInspector;
	private Label _inspectorTitle;
	private Label _inspectorSideLabel;
	private VBoxContainer _shipRowsBox;
	private Button _addShipButton;
	private Button _gridToggleButton;

	private ColorRect _libraryOverlay;
	private ItemList _libraryList;
	private CenterContainer _newDialog;
	private LineEdit _newNameEdit;
	private ConfirmationDialog _deleteDialog;
	private FileDialog _importDialog;
	private string _pendingDelete;

	private readonly Dictionary<MapEditorTool, Button> _toolButtons = new();
	private readonly List<PaletteSwatchButton> _paletteButtons = new();
	private readonly List<ShipInspectorRow> _shipRows = new();
	private Vector2I _inspectorHex;

	public override void _Ready()
	{
		// 根 Control 必须忽略鼠标事件，否则会吞掉地图点击；子面板在场景中各自保持 Stop
		MouseFilter = MouseFilterEnum.Ignore;
		_canvas = GetNodeOrNull<MapCanvasController>("../../MapCanvas");
		_data = GetNodeOrNull<LevelDataManager>("../../LevelDataManager");

		BindNodes();
		_libraryList.ItemActivated += _ => OnOpenCanvasPressed();
		BuildPalette();
		SetActiveTool(MapEditorTool.Pencil);
		RefreshCanvasList();

		bool autoOpened = _data?.MapAutoOpened ?? false;
		_canvasNameLabel.Text = _data?.CurrentMapName ?? "未打开画布";
		// 从画布菜单直接进入时，数据已由 LevelDataManager 加载，这里把数据刷进 TileMapLayer。
		if (autoOpened) _canvas?.ApplyDataToLayers();
		ShowCanvasLibrary(!autoOpened);
	}

	// ── 节点绑定 ──

	private void BindNodes()
	{
		_canvasNameLabel = GetNode<Label>("TopBar/TopBarBox/CanvasNameLabel");
		_statusLabel = GetNode<Label>("StatusLabel");
		_statusTimer = GetNode<Timer>("StatusTimer");
		_terrainEntries = GetNode<VBoxContainer>("PalettePanel/PaletteScroll/PaletteBox/TerrainEntries");
		_generationEntries = GetNode<VBoxContainer>("PalettePanel/PaletteScroll/PaletteBox/GenerationEntries");
		_specialEntries = GetNode<VBoxContainer>("PalettePanel/PaletteScroll/PaletteBox/SpecialEntries");
		_rightInspector = GetNode<PanelContainer>("RightInspector");
		_inspectorTitle = GetNode<Label>("RightInspector/InspectorScroll/InspectorBox/InspectorTitle");
		_inspectorSideLabel = GetNode<Label>("RightInspector/InspectorScroll/InspectorBox/InspectorSideLabel");
		_shipRowsBox = GetNode<VBoxContainer>("RightInspector/InspectorScroll/InspectorBox/ShipRows");
		_addShipButton = GetNode<Button>("RightInspector/InspectorScroll/InspectorBox/InspectorButton/AddShipButton");
		_gridToggleButton = GetNode<Button>("ToolBar/ToolBox/GridToggleButton");
		_libraryOverlay = GetNode<ColorRect>("LibraryOverlay");
		_libraryList = GetNode<ItemList>("LibraryOverlay/LibraryCenter/LibraryPanel/LibraryBox/CanvasList");
		_newDialog = GetNode<CenterContainer>("NewDialog");
		_newNameEdit = GetNode<LineEdit>("NewDialog/NewPanel/NewBox/NewNameEdit");
		_deleteDialog = GetNode<ConfirmationDialog>("DeleteDialog");
		_importDialog = GetNode<FileDialog>("ImportDialog");

		_toolButtons[MapEditorTool.Select] = GetNode<Button>("ToolBar/ToolBox/SelectToolButton");
		_toolButtons[MapEditorTool.Pencil] = GetNode<Button>("ToolBar/ToolBox/PencilToolButton");
		_toolButtons[MapEditorTool.Fill] = GetNode<Button>("ToolBar/ToolBox/FillToolButton");
		_toolButtons[MapEditorTool.Eraser] = GetNode<Button>("ToolBar/ToolBox/EraserToolButton");
	}

	// ── 工具按钮 ──

	public void _OnSelectToolPressed() => SetActiveTool(MapEditorTool.Select);
	public void _OnPencilToolPressed() => SetActiveTool(MapEditorTool.Pencil);
	public void _OnFillToolPressed() => SetActiveTool(MapEditorTool.Fill);
	public void _OnEraserToolPressed() => SetActiveTool(MapEditorTool.Eraser);
	public void _OnGridTogglePressed() => _canvas?.SetGridVisible(_gridToggleButton.ButtonPressed);

	private void SetActiveTool(MapEditorTool tool)
	{
		_canvas?.SetTool(tool);
		foreach (var kv in _toolButtons) kv.Value.ButtonPressed = kv.Key == tool;
	}

	// ── 调色板 ──

	private void BuildPalette()
	{
		_terrainEntries.QueueFreeChildren();
		_generationEntries.QueueFreeChildren();
		_specialEntries.QueueFreeChildren();
		_paletteButtons.Clear();

		if (TileCatalog == null || PaletteSwatchScene == null)
		{
			_terrainEntries.AddChild(new Label { Text = "缺少 editor_tile_catalog.tres / palette_swatch.tscn" });
			return;
		}

		foreach (var entry in TileCatalog.Entries)
		{
			if (entry == null) continue;
			VBoxContainer box = BoxForCategory(entry.Category);
			if (box == null) continue;
			var swatch = PaletteSwatchScene.Instantiate<PaletteSwatchButton>();
			swatch.Entry = entry;
			swatch.Pressed += () => SelectPalette(entry, swatch);
			box.AddChild(swatch);
			_paletteButtons.Add(swatch);
		}
	}

	private VBoxContainer BoxForCategory(string category) => category switch
	{
		"Generation" => _generationEntries,
		"Special" => _specialEntries,
		_ => _terrainEntries
	};

	private void SelectPalette(EditorTileEntry entry, PaletteSwatchButton swatch)
	{
		_canvas?.SetPalette(entry.Category, entry.SourceId);
		foreach (var b in _paletteButtons) b.ButtonPressed = ReferenceEquals(b, swatch);
		ShowStatus($"已选择 {entry.DisplayName}");
	}

	// ── 画布库 ──

	public void _OnSavePressed() => OnSavePressed();
	public void _OnCanvasListPressed()
	{
		RefreshCanvasList();
		ShowCanvasLibrary(true);
	}
	public void _OnBackPressed() => GetTree().ChangeSceneToFile(MainMenuScenePath);
	public void _OnOpenCanvasPressed() => OnOpenCanvasPressed();
	public void _ShowNewDialog() => ShowNewDialog();
	public void _OnImportPressed() => _importDialog?.PopupCentered();
	public void _OnDeleteCanvasPressed() => OnDeleteCanvasPressed();
	public void _OnConfirmNewCanvas() => ConfirmNewCanvas();
	public void _OnCancelNewDialog() => _newDialog.Visible = false;

	public void _OnDeleteConfirmed()
	{
		if (string.IsNullOrEmpty(_pendingDelete)) return;
		_data.DeleteMap(_pendingDelete);
		RefreshCanvasList();
		ShowStatus($"已删除 {_pendingDelete}");
	}

	public void _OnImportFileSelected(string path) => ImportCanvas(path);
	public void _OnStatusTimeout() => _statusLabel.Text = "";

	private void RefreshCanvasList()
	{
		if (_libraryList == null || _data == null) return;
		_libraryList.Clear();
		foreach (string name in _data.ListMaps()) _libraryList.AddItem(name);
	}

	private void ShowCanvasLibrary(bool visible)
	{
		if (_libraryOverlay != null) _libraryOverlay.Visible = visible;
	}

	private void OnOpenCanvasPressed()
	{
		if (_libraryList.GetSelectedItems().Length == 0) return;
		string fileName = _libraryList.GetItemText(_libraryList.GetSelectedItems()[0]);
		if (!_data.LoadMap(fileName)) { ShowStatus("打开失败"); return; }
		_canvas.ApplyDataToLayers();
		_canvasNameLabel.Text = _data.CurrentMapName;
		ShowCanvasLibrary(false);
		ShowStatus($"已打开 {fileName}");
	}

	private void OnSavePressed()
	{
		if (_data == null) return;
		if (_data.SaveCurrentMap()) ShowStatus($"已保存 {_data.CurrentMapName}");
		else ShowStatus("保存失败：请先打开或新建画布");
	}

	private void OnDeleteCanvasPressed()
	{
		if (_libraryList.GetSelectedItems().Length == 0) return;
		_pendingDelete = _libraryList.GetItemText(_libraryList.GetSelectedItems()[0]);
		_deleteDialog.DialogText = $"确定删除画布 {_pendingDelete} 吗？";
		_deleteDialog.PopupCentered();
	}

	private void ShowNewDialog()
	{
		_newNameEdit.Text = "";
		_newDialog.Visible = true;
		_newNameEdit.GrabFocus();
	}

	private void ConfirmNewCanvas()
	{
		string name = _newNameEdit.Text.Trim();
		if (string.IsNullOrEmpty(name)) { ShowStatus("画布名不能为空"); return; }
		_data.NewMap(name);
		_canvas.ClearCanvas();
		_canvasNameLabel.Text = name;
		_newDialog.Visible = false;
		ShowCanvasLibrary(false);
		ShowStatus($"已新建 {name}");
	}

	private void ImportCanvas(string sourcePath)
	{
		string fileName = System.IO.Path.GetFileName(sourcePath);
		DirAccess.MakeDirRecursiveAbsolute(LevelDataManager.DefaultExportFolder);
		string destPath = $"{LevelDataManager.DefaultExportFolder}/{fileName}";
		if (DirAccess.CopyAbsolute(sourcePath, destPath) == Error.Ok)
		{
			RefreshCanvasList();
			ShowStatus($"已导入 {fileName}");
		}
		else
		{
			ShowStatus("导入失败");
		}
	}

	// ── 生成点检查器 ──

	/// <summary>由 MapCanvasController 在右键生成点时调用。</summary>
	public void OpenGenerationInspector(Vector2I hex)
	{
		_inspectorHex = hex;
		RebuildInspector();
		_rightInspector.Visible = true;
	}

	/// <summary>隐藏右侧生成点检查器。</summary>
	public void HideInspector() => _rightInspector.Visible = false;

	private void RebuildInspector()
	{
		_shipRowsBox.QueueFreeChildren();
		_shipRows.Clear();

		var gen = _data.GetGenerationAt(_inspectorHex);
		if (gen == null) { HideInspector(); return; }

		_inspectorTitle.Text = $"生成点 ({_inspectorHex.X}, {_inspectorHex.Y})";
		_inspectorSideLabel.Text = gen.Side == GenerationSide.Enemy ? "阵营：敌方" : "阵营：玩家";

		var ships = _data.GetShipsAt(_inspectorHex);
		for (int i = 0; i < ships.Count; i++) AddShipRow(ships[i]);
		_addShipButton.Disabled = _shipRows.Count >= LevelDataManager.MaxShipsPerTile;
		_rightInspector.Visible = true;
	}

	public void _OnAddShipPressed()
	{
		if (_shipRows.Count >= LevelDataManager.MaxShipsPerTile) return;
		AddShipRow(null);
	}

	private void AddShipRow(ShipSpawnData ship)
	{
		if (ShipRowScene == null) return;
		var row = ShipRowScene.Instantiate<ShipInspectorRow>();
		row.Setup(ship);
		row.Removed += OnShipRowRemoved;
		_shipRows.Add(row);
		_shipRowsBox.AddChild(row);
		_addShipButton.Disabled = _shipRows.Count >= LevelDataManager.MaxShipsPerTile;
	}

	private void OnShipRowRemoved(ShipInspectorRow row)
	{
		_shipRows.Remove(row);
		_shipRowsBox.RemoveChild(row);
		row.QueueFree();
		_addShipButton.Disabled = _shipRows.Count >= LevelDataManager.MaxShipsPerTile;
	}

	public void _OnConfirmInspector() => ConfirmInspector();

	private void ConfirmInspector()
	{
		for (int i = 0; i < _shipRows.Count; i++)
		{
			var ship = _shipRows[i].ReadShip();
			if (i < _data.GetShipsAt(_inspectorHex).Count)
				_data.SetShip(_inspectorHex, i, ship);
			else
				_data.AddShip(_inspectorHex, ship);
		}
		while (_data.GetShipsAt(_inspectorHex).Count > _shipRows.Count)
			_data.RemoveShipAt(_inspectorHex, _data.GetShipsAt(_inspectorHex).Count - 1);

		_canvas.RefreshOverlay();
		ShowStatus("船初设已更新");
	}

	public void _OnDeleteGenerationPressed()
	{
		_data.EraseGeneration(_inspectorHex);
		_canvas.RefreshOverlay();
		SyncGenerationLayerFromData();
		HideInspector();
		ShowStatus("已删除生成点");
	}

	/// <summary>删除生成点后，把内存中剩余生成点刷回 GenerationLayer。</summary>
	private void SyncGenerationLayerFromData()
	{
		var layer = _canvas?.LayerForCategory("Generation");
		if (layer == null) return;
		layer.Clear();
		foreach (var kv in _data.GenerationPoints)
			layer.SetCell(new Vector2I(kv.Key.X + (kv.Key.Y >> 1), kv.Key.Y), kv.Value.SourceId, Vector2I.Zero);
	}

	/// <summary>在底部状态栏提示信息，4 秒后自动清空。</summary>
	private void ShowStatus(string message)
	{
		_statusLabel.Text = message;
		_statusTimer.Start();
	}
}

internal static class EditorUiExtensions
{
	/// <summary>释放一个容器下的全部子节点（配合 QueueFree 延迟回收）。</summary>
	public static void QueueFreeChildren(this Node parent)
	{
		foreach (Node child in parent.GetChildren()) child.QueueFree();
	}
}

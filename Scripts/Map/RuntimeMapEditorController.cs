using Godot;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// Runtime hex-level unit editor panel. Attaches to editor_scene.
/// Listens for HexClicked (from MapCanvasController or BattleInputDetector),
/// shows a property panel to edit TileId / Direction / Speed per stacked unit.
/// </summary>
/// <summary>
/// 运行时编辑器面板（Control）。
/// EditorEnabled 为 true 时启用：监听 HexClicked 事件，显示初设属性面板，
/// 支持查看/编辑/添加/删除指定六角格上的堆叠单位初设（TileId/航向/初速），
/// 并可将当前初设保存为 JSON。
/// </summary>
public partial class RuntimeMapEditorController : Control
{
	[Export] public bool EditorEnabled;
	[Export] public NodePath LevelDataManagerPath = "../../LevelDataManager";
	[Export] public NodePath EventBusPath = "../../EventBus";
	[Export] public int DefaultTileId = 6;

	private LevelDataManager _dataManager;
	private EventBus _bus;
	private Vector2I _selectedHex;
	private int _selectedStackIndex;

	private Label _titleLabel;
	private OptionButton _stackSelector;
	private SpinBox _tileIdSpin;
	private OptionButton _directionSelector;
	private SpinBox _speedSpin;
	private Button _applyButton;
	private Button _addButton;
	private Button _removeButton;
	private Button _saveButton;

	public override void _Ready()
	{
		if (!EditorEnabled)
		{
			Visible = false;
			SetProcessUnhandledInput(false);
			return;
		}

		_dataManager = GetNodeOrNull<LevelDataManager>(LevelDataManagerPath);
		_bus = GetNodeOrNull<EventBus>(EventBusPath);
		if (_bus != null) _bus.HexClicked += OnHexClicked;

		BuildUi();
		RefreshPanel();
	}

/// <summary>构建编辑器面板的 UI 控件：标题、堆叠选择器、TileId/航向/初速编辑、按钮组。</summary>
	private void BuildUi()
	{
		Visible = false;
		AnchorLeft = 1f;
		AnchorRight = 1f;
		AnchorTop = 0f;
		AnchorBottom = 0f;
		OffsetLeft = -260f;
		OffsetRight = -12f;
		OffsetTop = 72f;
		OffsetBottom = 320f;

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(LayoutPreset.FullRect);
		AddChild(panel);

		var margin = new MarginContainer();
		margin.AddThemeConstantOverride("margin_left", 10);
		margin.AddThemeConstantOverride("margin_right", 10);
		margin.AddThemeConstantOverride("margin_top", 10);
		margin.AddThemeConstantOverride("margin_bottom", 10);
		panel.AddChild(margin);

		var box = new VBoxContainer();
		box.AddThemeConstantOverride("separation", 8);
		margin.AddChild(box);

		_titleLabel = new Label();
		box.AddChild(_titleLabel);

		_stackSelector = new OptionButton();
		_stackSelector.ItemSelected += index =>
		{
			_selectedStackIndex = (int)index;
			LoadSelectedUnitIntoControls();
		};
		box.AddChild(_stackSelector);

		_tileIdSpin = CreateSpinBox(0, 999, DefaultTileId);
		box.AddChild(WrapField("型号 TileId", _tileIdSpin));

		_directionSelector = new OptionButton();
		foreach (HexDirection direction in Enum.GetValues<HexDirection>())
			_directionSelector.AddItem(direction.ToString(), (int)direction);
		box.AddChild(WrapField("航向", _directionSelector));

		_speedSpin = CreateSpinBox(0, 12, 0);
		box.AddChild(WrapField("初速", _speedSpin));

		var buttons = new HBoxContainer();
		box.AddChild(buttons);

		_applyButton = CreateButton("应用", ApplySelectedUnit);
		_addButton = CreateButton("新增", AddUnit);
		_removeButton = CreateButton("删除", RemoveSelectedUnit);
		buttons.AddChild(_applyButton);
		buttons.AddChild(_addButton);
		buttons.AddChild(_removeButton);

		_saveButton = CreateButton("保存 JSON", SaveMap);
		box.AddChild(_saveButton);
	}

/// <summary>创建带步进值的 SpinBox 控件。</summary>
	private static SpinBox CreateSpinBox(double min, double max, double value)
	{
		var spin = new SpinBox
		{
			MinValue = min,
			MaxValue = max,
			Value = value,
			Step = 1
		};
		return spin;
	}

/// <summary>创建带点击回调的 Button 控件。</summary>
	private static Button CreateButton(string text, Action pressed)
	{
		var button = new Button { Text = text };
		button.Pressed += pressed;
		return button;
	}

/// <summary>将 Label 与编辑器控件包装为 HBoxContainer 以保持布局对齐。</summary>
	private static Control WrapField(string label, Control field)
	{
		var box = new HBoxContainer();
		var text = new Label
		{
			Text = label,
			CustomMinimumSize = new Vector2(72, 0)
		};
		box.AddChild(text);
		box.AddChild(field);
		return box;
	}

/// <summary>接收 HexClicked 事件，选中对应六角格并显示编辑面板。</summary>
	private void OnHexClicked(Vector2I hex)
	{
		_selectedHex = hex;
		_selectedStackIndex = 0;
		Visible = true;
		RefreshPanel();
	}

/// <summary>刷新面板：更新堆叠选择器下拉列表与当前选中单位的属性控件值。</summary>
	private void RefreshPanel()
	{
		if (_titleLabel == null) return;

		_titleLabel.Text = $"初设编辑: {_selectedHex}";
		_stackSelector.Clear();

		var units = _dataManager?.GetUnitsAt(_selectedHex);
		int count = units?.Count ?? 0;
		for (int i = 0; i < count; i++)
			_stackSelector.AddItem($"#{i + 1}  Tile {units![i].TileId}", i);

		bool hasUnit = count > 0;
		_stackSelector.Disabled = !hasUnit;
		_applyButton.Disabled = !hasUnit;
		_removeButton.Disabled = !hasUnit;

		if (!hasUnit)
		{
			_tileIdSpin.Value = DefaultTileId;
			_speedSpin.Value = 0;
			_directionSelector.Select((int)HexDirection.N);
			return;
		}

		_selectedStackIndex = Mathf.Clamp(_selectedStackIndex, 0, count - 1);
		_stackSelector.Select(_selectedStackIndex);
		LoadSelectedUnitIntoControls();
	}

/// <summary>将当前堆叠索引对应的单位初设加载到 UI 控件中。</summary>
	private void LoadSelectedUnitIntoControls()
	{
		var units = _dataManager?.GetUnitsAt(_selectedHex);
		if (units == null || _selectedStackIndex < 0 || _selectedStackIndex >= units.Count) return;

		UnitSpawnData unit = units[_selectedStackIndex];
		_tileIdSpin.Value = unit.TileId;
		_speedSpin.Value = unit.Speed;
		_directionSelector.Select((int)unit.Direction);
	}

/// <summary>将当前 UI 值写入 LevelDataManager 的初设数据。</summary>
	private void ApplySelectedUnit()
	{
		if (_dataManager == null) return;

		_dataManager.SetUnitInitialState(
			_selectedHex,
			_selectedStackIndex,
			(int)_tileIdSpin.Value,
			(HexDirection)_directionSelector.GetSelectedId(),
			(int)_speedSpin.Value);
		RefreshPanel();
		_bus?.EmitSignal("LogMessage", $"已更新 {_selectedHex} #{_selectedStackIndex + 1} 初设");
	}

/// <summary>向当前六角格添加一个新的初设单位。</summary>
	private void AddUnit()
	{
		if (_dataManager == null) return;

		_dataManager.AddUnit(
			_selectedHex,
			(int)_tileIdSpin.Value,
			(HexDirection)_directionSelector.GetSelectedId(),
			(int)_speedSpin.Value);
		RefreshPanel();
		_bus?.EmitSignal("LogMessage", $"已向 {_selectedHex} 添加初设单位");
	}

/// <summary>删除当前堆叠索引对应的初设单位。</summary>
	private void RemoveSelectedUnit()
	{
		if (_dataManager == null) return;

		_dataManager.RemoveUnit(_selectedHex, _selectedStackIndex);
		_selectedStackIndex = 0;
		RefreshPanel();
		_bus?.EmitSignal("LogMessage", $"已删除 {_selectedHex} 的一个初设单位");
	}

/// <summary>调用 LevelDataManager.SaveCurrentMap() 将当前初设持久化为 JSON。</summary>
	private void SaveMap()
	{
		_dataManager?.SaveCurrentMap();
		_bus?.EmitSignal("LogMessage", "地图初设已保存到 JSON");
	}
}

using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 生成点检查器里的一行舰船初设（Scenes/Editor/ship_inspector_row.tscn）。
/// 船型下拉框从 ShipCatalog 填充；方向与初速由 SpinBox 编辑；删除按钮触发 Removed 事件。
/// </summary>
public partial class ShipInspectorRow : HBoxContainer
{
	/// <summary>点击删除按钮时触发。</summary>
	public event System.Action<ShipInspectorRow> Removed;

	private OptionButton _shipSelect;
	private SpinBox _directionSpin;
	private SpinBox _speedSpin;
	private ShipSpawnData _pending;

	public override void _Ready()
	{
		_shipSelect = GetNode<OptionButton>("ShipSelect");
		_directionSpin = GetNode<SpinBox>("DirectionSpin");
		_speedSpin = GetNode<SpinBox>("SpeedSpin");
		GetNode<Button>("RemoveButton").Pressed += () => Removed?.Invoke(this);

		foreach (var entry in ShipCatalog.Entries)
		{
			int idx = _shipSelect.ItemCount;
			_shipSelect.AddItem(entry.DisplayName, idx);
			_shipSelect.SetItemMetadata(idx, entry.ShipId);
		}
		if (_pending != null) Apply(_pending);
	}

	/// <summary>在 AddChild 前调用：用已有初设填充本行，新增行传 null。</summary>
	public void Setup(ShipSpawnData ship) => _pending = ship;

	/// <summary>读取当前控件值，组装成 ShipSpawnData。</summary>
	public ShipSpawnData ReadShip()
	{
		return new ShipSpawnData
		{
			ShipId = (string)_shipSelect.GetSelectedMetadata(),
			Direction = (HexDirection)(int)_directionSpin.Value,
			Speed = (int)_speedSpin.Value
		};
	}

	private void Apply(ShipSpawnData ship)
	{
		int index = -1;
		for (int i = 0; i < _shipSelect.ItemCount; i++)
		{
			if ((string)_shipSelect.GetItemMetadata(i) == ship.ShipId)
			{
				index = i;
				break;
			}
		}
		if (index >= 0) _shipSelect.Select(index);
		_directionSpin.Value = (int)ship.Direction;
		_speedSpin.Value = ship.Speed;
	}
}

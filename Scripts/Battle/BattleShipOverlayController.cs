using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战场船体覆盖层：Label3D 与 TurnFlag 不再属于船预制体，
/// 由本控制器动态创建并跟随舰船位置刷新。
/// </summary>
public partial class BattleShipOverlayController : Node3D
{
	[Export] public Texture2D TurnFlagTexture;
	[Export] public float LabelHeight = 5.2f;
	[Export] public float FlagHeight = 2.0f;
	[Export] public float RefreshInterval = 0.05f;

	private readonly Dictionary<ShipComponent, Label3D> _labels = new();
	private readonly Dictionary<ShipComponent, Sprite3D> _flags = new();
	private float _timer;

	public override void _Ready()
	{
		var bus = GetNode<EventBus>("../EventBus");
		bus.ShipStatusChanged += OnShipStatusChanged;
		bus.ShipSelectionChanged += OnShipSelectionChanged;
		bus.PhaseChanged += OnPhaseChanged;
		CallDeferred(MethodName.RefreshAll);
	}

	public override void _Process(double delta)
	{
		_timer += (float)delta;
		if (_timer < RefreshInterval) return;
		_timer = 0f;
		RefreshStatuses();
		RefreshPositions();
	}

	private void OnPhaseChanged(string phaseName, int phaseIndex, int turnNumber) => RefreshAll();

	private void OnShipStatusChanged(ShipComponent ship)
	{
		if (!GodotObject.IsInstanceValid(ship)) return;
		Label3D label = GetOrCreateLabel(ship);
		label.Text = ship.StatusText;
		label.Visible = ship.ShouldShowStatus;
	}

	private void OnShipSelectionChanged(ShipComponent ship, bool selected)
	{
		if (!GodotObject.IsInstanceValid(ship)) return;
		if (selected)
		{
			foreach (var (other, otherFlag) in _flags)
				if (!ReferenceEquals(other, ship))
					otherFlag.Visible = false;
		}
		Sprite3D flag = GetOrCreateFlag(ship);
		flag.Visible = selected;
		flag.Scale = selected
			? new Vector3(3.2f, 3.2f, 1f)
			: new Vector3(2.5f, 2.5f, 1f);
	}

	private void RefreshAll()
	{
		foreach (var (_, label) in _labels)
			label.QueueFree();
		foreach (var (_, flag) in _flags)
			flag.QueueFree();
		_labels.Clear();
		_flags.Clear();

		foreach (Node node in GetTree().GetNodesInGroup("Ships"))
		{
			if (node is not ShipComponent ship || !GodotObject.IsInstanceValid(ship)) continue;
			OnShipStatusChanged(ship);
			Sprite3D flag = GetOrCreateFlag(ship);
			flag.Visible = false;
		}
		RefreshPositions();
	}

	private void RefreshPositions()
	{
		foreach (var (ship, label) in _labels)
		{
			if (!GodotObject.IsInstanceValid(ship))
			{
				label.QueueFree();
				continue;
			}
			label.Position = new Vector3(0f, LabelHeight, 0f);
		}
		foreach (var (ship, flag) in _flags)
		{
			if (!GodotObject.IsInstanceValid(ship))
			{
				flag.QueueFree();
				continue;
			}
			flag.Position = new Vector3(0f, FlagHeight, 0f);
		}
	}

	private void RefreshStatuses()
	{
		foreach (var (ship, label) in _labels)
		{
			if (!GodotObject.IsInstanceValid(ship))
			{
				label.QueueFree();
				continue;
			}
			label.Text = ship.StatusText;
			label.Visible = ship.ShouldShowStatus;
		}
	}

	private Label3D GetOrCreateLabel(ShipComponent ship)
	{
		if (_labels.TryGetValue(ship, out var label) && GodotObject.IsInstanceValid(label))
			return label;
		label = new Label3D
		{
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			FixedSize = true,
			PixelSize = 0.001f,
			FontSize = 48,
			OutlineSize = 8,
			Modulate = Colors.White
		};
		ship.AddChild(label);
		_labels[ship] = label;
		return label;
	}

	private Sprite3D GetOrCreateFlag(ShipComponent ship)
	{
		if (_flags.TryGetValue(ship, out var flag) && GodotObject.IsInstanceValid(flag))
			return flag;
		flag = new Sprite3D
		{
			Texture = TurnFlagTexture,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Scale = new Vector3(2.5f, 2.5f, 1f)
		};
		ship.AddChild(flag);
		_flags[ship] = flag;
		return flag;
	}
}

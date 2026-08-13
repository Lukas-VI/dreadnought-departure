using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>鱼雷占位实体：带网格标记的 Node3D，负责显示、移动与命中回调。</summary>
public partial class TorpedoComponent : Node3D
{
	public string TorpedoId = "";
	public int Side;
	public Vector2I Hex;
	public HexDirection Direction;
	public int Speed = 6;
	public int RemainingRange;
	public int RangeSpent;
	public int Count = 1;
	public int HitMode = 7;
	public int TorpedoDamage = 30;
	public string TorpedoType = "";
	public ShipComponent Launcher;
	public int FanSide = -1;
	public int FanBranch;

	private Label3D _label;

	public override void _Ready()
	{
		_label = GetNodeOrNull<Label3D>("Label3D");
		ApplyVisual();
	}

	public void Setup(string id, int side, Vector2I hex, HexDirection direction,
		int speed, int range, int count, int hitMode, int damage,
		string type, ShipComponent launcher, int fanSide, int fanBranch)
	{
		TorpedoId = id;
		Side = side;
		Hex = hex;
		Direction = direction;
		Speed = Mathf.Max(1, speed);
		RemainingRange = Mathf.Max(1, range);
		Count = Mathf.Max(1, count);
		HitMode = hitMode;
		TorpedoDamage = Mathf.Max(1, damage);
		TorpedoType = type;
		Launcher = launcher;
		FanSide = fanSide;
		FanBranch = fanBranch;
		ApplyVisual();
	}

	public void ApplyVisual()
	{
		RotationDegrees = new Vector3(0f, 90f - (int)Direction * 60f, 0f);
		if (_label != null)
		{
			_label.Text = Count > 1 ? $"{TorpedoType}\n×{Count}" : TorpedoType;
		}
	}

	public Tween AnimateMoveTo(MapGenerator map, Vector2I target, float duration)
	{
		Vector3 world = map.HexToWorld(target.X, target.Y);
		Vector3 to = new Vector3(world.X, 0.18f, world.Z);
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Linear);
		tween.TweenProperty(this, "position", to, duration);
		tween.TweenCallback(Callable.From(() =>
		{
			Hex = target;
			RemainingRange--;
			RangeSpent++;
		}));
		return tween;
	}

	public Tween AnimateMovePath(MapGenerator map, IReadOnlyList<Vector2I> path, float perStepDuration)
	{
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Linear);
		foreach (Vector2I target in path)
		{
			Vector3 world = map.HexToWorld(target.X, target.Y);
			Vector3 to = new Vector3(world.X, 0.18f, world.Z);
			tween.TweenProperty(this, "position", to, perStepDuration);
			Vector2I captured = target;
			tween.TweenCallback(Callable.From(() =>
			{
				Hex = captured;
				RemainingRange--;
				RangeSpent++;
			}));
		}
		return tween;
	}
}

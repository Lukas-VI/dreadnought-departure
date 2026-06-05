using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class ShipComponent : Node3D
{
	// 战舰的内存纯数据（子类在 SetupAttributes 里覆写）
	public string ShipName { get; set; } = "HMS Dreadnought";
	public int MaxHp { get; set; } = 100;
	public int CurrentHp { get; set; } = 100;
	public int MoveRange { get; set; } = 3;
	public int AttackRange { get; set; } = 4;
	public int AttackPower { get; set; } = 35;

	// 当前战舰所在的六角格轴向坐标 (Q, R)
	public Vector2I HexCoords { get; set; }

	private Label3D _hpLabel;

	// 子类覆写这个方法，不用再写一遍 AddToGroup / 找 Label3D
	public override void _Ready()
	{
		AddToGroup("Ships");
		_hpLabel = GetNode<Label3D>("Label3D");
		SetupAttributes();   // ← 留给子类的钩子
		UpdateUi();
	}

	// 子类在这里设自己的属性（ShipName, MaxHp 等）
	protected virtual void SetupAttributes() { }

	// 移动到目标六角格（GameplayDirector 调这个，不直接改 Position）
	public void MoveToHex(MapGenerator map, Vector2I target)
	{
		HexCoords = target;
		Vector3 world = map.HexToWorld(target.X, target.Y);
		Position = new Vector3(world.X, 0.3f, world.Z);
	}

	public void TakeDamage(int damage)
	{
		CurrentHp = Mathf.Max(0, CurrentHp - damage);
		UpdateUi();
		
		if (CurrentHp <= 0)
		{
			GD.Print($"{ShipName} 被击沉！");
			QueueFree();
		}
	}

	public void UpdateUi()
	{
		if (_hpLabel != null)
			_hpLabel.Text = $"{ShipName}\nHP: {CurrentHp}/{MaxHp}";
	}
}

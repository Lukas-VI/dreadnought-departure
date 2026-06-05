using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class ShipComponent : Node3D
{
	// 配表资源——在编辑器里拖 .tres 到预制体上，MapGenerator 不需要知道这些
	[Export] public ShipData Data;

	// 战舰的内存纯数据（Data 不为空时会被覆盖）
	public string ShipName { get; set; } = "HMS Dreadnought";
	public int MaxHp { get; set; } = 100;
	public int CurrentHp { get; set; } = 100;
	public int MoveRange { get; set; } = 3;
	public int AttackRange { get; set; } = 4;
	public int AttackPower { get; set; } = 35;

	// 当前战舰所在的六角格轴向坐标 (Q, R)
	public Vector2I HexCoords { get; set; }

	private Label3D _hpLabel;
	private Sprite3D _turnFlag;

	// 子类覆写这个方法，不用再写一遍 AddToGroup / 找 Label3D
	public override void _Ready()
	{
		AddToGroup("Ships");
		_hpLabel = GetNode<Label3D>("Label3D");
		_turnFlag = GetNodeOrNull<Sprite3D>("TurnFlag");
		SetupAttributes();   // ← 留给子类的钩子
		if (Data != null) ApplyData(Data);  // ← 预制体自带配表，自动注入
		if (_turnFlag != null) _turnFlag.Visible = false;  // 初始隐藏flag
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

	
	// 从 ShipData 资源加载属性（子类用这个，不用再覆写 SetupAttributes）
	public void ApplyData(ShipData data)
	{
		if (data == null) return;
		ShipName = data.ShipName;
		MaxHp = data.MaxHp;
		CurrentHp = data.MaxHp;
		MoveRange = data.MoveRange;
		AttackRange = data.AttackRange;
		AttackPower = data.AttackPower;
		UpdateUi();
	}
	public void ShowSelected(bool visible)
	{
		if (_turnFlag != null) _turnFlag.Visible = visible;
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

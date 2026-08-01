using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 舰船实体组件（Node3D），挂载在 tscn 预制体根节点。
/// 包含运行时属性（HP/航速/方向/坐标）、伤害结算缓冲（PendingDamage）、
/// 头顶 UI（Label3D + TurnFlag），以及从 ShipData 资源加载属性的入口。
/// 子类可通过 SetupAttributes 虚函数自定义初始值。
/// </summary>
public partial class ShipComponent : Node3D
{
	// 配表资源——在编辑器里拖 .tres 到预制体上，MapGenerator 不需要知道这些
	[Export] public ShipData Data;
	[Export] public HexDirection InitialDirection = HexDirection.N;
	[Export] public int InitialSpeed;
	[Export] public float DirectionYawOffsetDegrees = 180f;

	// 战舰的内存纯数据（Data 不为空时会被覆盖）
	public string ShipName { get; set; } = "HMS Dreadnought";
	public string ShipClass { get; set; } = "S";
	public int PV { get; set; }
	public int MaxHp { get; set; } = 100;
	public int CurrentHp { get; set; } = 100;
	public int MoveRange { get; set; } = 3;
	public int AttackRange { get; set; } = 4;
	public int AttackPower { get; set; } = 35;
	public int MainAmmo { get; set; } = 20;
	public int MaxSpeed { get; set; } = 5;
	/// <summary>本回合是否执行过转向（Label3D 状态用）。</summary>
	public bool TurnedThisPhase;
	/// <summary>是否已离场（Label3D 状态用，暂由外部规则触发）。</summary>
	public bool IsOffMap;
	public int TileSourceId; // 兼容字段：来自旧 2D 编辑器的 tile ID
	/// <summary>阵营（由生成点决定），用于敌我分组。</summary>
	public GenerationSide BattleSide = GenerationSide.Player;

	// 当前战舰所在的六角格轴向坐标 (Q, R)
	public Vector2I HexCoords { get; set; }
	public int PendingDamage;
	/// <summary>本回合炮击的判定记录，回合结算时打印到 InfoLabel。</summary>
	public List<string> PendingShotChecks { get; } = new();

	private Label3D _hpLabel;
	private Sprite3D _turnFlag;
	private HexDirection _direction = HexDirection.N;
	private int _currentSpeed;

	public HexDirection Direction
	{
		get => _direction;
		set
		{
			_direction = value;
			ApplyDirectionRotation();
			UpdateUi();
		}
	}

	public int CurrentSpeed
	{
		get => _currentSpeed;
		set
		{
			_currentSpeed = Mathf.Max(0, value);
			UpdateUi();
		}
	}

	public int DamageTaken => Mathf.Max(0, MaxHp - CurrentHp);
	public DamageState DamageState => Data != null
		? Data.GetDamageState(DamageTaken)
		: (CurrentHp <= 0 ? DamageState.Sunk : DamageState.Intact);
	public int MaxSpeedForCurrentState => Data?.MaxSpeedForState(DamageState) ?? MaxSpeed;

	// 子类覆写这个方法，不用再写一遍 AddToGroup / 找 Label3D
/// <summary>初始化：加入 Ships 分组、获取 Label3D/Flag、应用配表数据、设置初始方向与航速。</summary>
	public override void _Ready()
	{
		AddToGroup("Ships");
		_hpLabel = GetNode<Label3D>("Label3D");
		_turnFlag = GetNodeOrNull<Sprite3D>("TurnFlag");
		SetupAttributes();   // ← 留给子类的钩子
		if (Data != null) ApplyData(Data);  // ← 预制体自带配表，自动注入
		Direction = InitialDirection;
		CurrentSpeed = InitialSpeed;
		if (_turnFlag != null) _turnFlag.Visible = false;  // 初始隐藏flag
		UpdateUi();
	}

	// 子类在这里设自己的属性（ShipName, MaxHp 等）
/// <summary>子类覆写点——在此处设置自定义属性（ShipName/MaxHp 等），替代在 _Ready 中覆写逻辑。</summary>
	protected virtual void SetupAttributes() { }

	// 移动到目标六角格（GameplayDirector 调这个，不直接改 Position）
/// <summary>将舰船移动到目标六角格。更新 HexCoords 与 Position，不涉及路径动画。</summary>
	public void MoveToHex(MapGenerator map, Vector2I target)
	{
		HexCoords = target;
		Vector3 world = map.HexToWorld(target.X, target.Y);
		Position = new Vector3(world.X, 0.3f, world.Z);
	}
	/// <summary>移动阶段惯性移动动画：从当前位置平滑移动到目标六角格，完成后更新 HexCoords。子类可覆写播放专属动画。</summary>
	public virtual Tween AnimateMoveTo(MapGenerator map, Vector2I target, float duration)
	{
		Vector3 world = map.HexToWorld(target.X, target.Y);
		Vector3 to = new Vector3(world.X, Position.Y, world.Z);
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(this, "position", to, duration);
		tween.TweenCallback(Callable.From(() => HexCoords = target));
		return tween;
	}


	
	// 从 ShipData 资源加载属性（子类用这个，不用再覆写 SetupAttributes）
/// <summary>从 ShipData 资源加载属性。覆盖当前运行时值并更新 UI。</summary>
	public void ApplyData(ShipData data)
	{
		if (data == null) return;
		ShipName = data.ShipName;
		ShipClass = data.ShipClass;
		PV = data.PV;
		MaxHp = data.MaxHp;
		CurrentHp = data.MaxHp;
		MaxSpeed = data.MaxSpeed;
		MoveRange = data.MoveRange;
		AttackRange = data.AttackRange;
		AttackPower = data.AttackPower;
		MainAmmo = data.MainAmmo;
		UpdateUi();
	}

/// <summary>应用初设时调用：设置 InitialDirection/InitialSpeed 并同步到运行时属性。</summary>
	public void ApplyInitialState(HexDirection direction, int speed)
	{
		InitialDirection = direction;
		InitialSpeed = speed;
		Direction = direction;
		CurrentSpeed = speed;
	}

	private void ApplyDirectionRotation()
	{
		RotationDegrees = new Vector3(0f, DirectionYawOffsetDegrees - (int)_direction * 60f, 0f);
	}

/// <summary>显示/隐藏选中标记（TurnFlag 精灵）。</summary>
	public void ShowSelected(bool visible)
	{
		if (_turnFlag != null) _turnFlag.Visible = visible;
	}

/// <summary>直接损伤，扣减 CurrentHp 并检查沉没。</summary>
	public void TakeDamage(int damage)
	{
		CurrentHp = Mathf.Max(0, CurrentHp - damage);
		AfterDamageApplied();
	}

/// <summary>将 PendingDamage 落实为 CurrentHp 损伤，阶段结束时由 GameplayDirector 调用。</summary>
	public void ApplyPendingDamage()
	{
		if (PendingDamage <= 0) return;
		CurrentHp = Mathf.Max(0, CurrentHp - PendingDamage);
		PendingDamage = 0;
		AfterDamageApplied();
	}

	private void AfterDamageApplied()
	{
		UpdateUi();
		if (DamageState == DamageState.Sunk)
		{
			GD.Print($"{ShipName} 沉没！");
			QueueFree();
			return;
		}
	}

	public void UpdateUi()
	{
		if (_hpLabel == null) return;
		bool showStatus = DamageState != DamageState.Intact || IsOffMap || TurnedThisPhase;
		_hpLabel.Visible = showStatus;
		if (showStatus)
		{
			var status = new List<string>();
			status.Add(DamageState switch
			{
				DamageState.Intact => "无损",
				DamageState.Light => "小破",
				DamageState.Moderate => "中破",
				DamageState.Heavy => "大破",
				_ => "沉没"
			});
			if (IsOffMap) status.Add("离场");
			if (TurnedThisPhase) status.Add("转向");
			_hpLabel.Text = string.Join("\n", status);
		}
	}
}

using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>一次炮击的受击反馈事件，供结算阶段逐船演绎。</summary>
public sealed class HitFeedbackEvent
{
	public ShipComponent Attacker;
	public ShipComponent Target;
	public bool Hit;
	public int Damage;
}

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
	/// <summary>NS 尖角朝上下时，船模额外逆时针旋转 30°。</summary>
	[Export] public float NSModelYawOffsetDegrees = 30f;
	/// <summary>转向补间动画时长（秒）。</summary>
	[Export] public float TurnTweenDuration = 0.2f;
	/// <summary>堆叠基准高度；并排时按 Z 轴左右错开。</summary>
	public const float StackBaseY = 0.3f;
	public const float StackZStep = 1.25f;

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
	/// <summary>待命指令：推进阶段前只做预览，不立即生效。</summary>
	public int PendingSpeed = -1;
	public HexDirection? PendingDirection;
	public ShipComponent PendingAttackTarget;
	public int PendingAttackDistance;
	public bool PendingRadarUsed;
	/// <summary>雷达技能是否在本阶段显式激活（由技能按钮写入，仅当回合有效）。</summary>
	public bool PendingRadarActive;
	/// <summary>所属单纵阵首舰；null 表示不在阵中（运行时标记，移动结算按此分组）。</summary>
	public ShipComponent FormationLead;
	/// <summary>在单纵阵中的链内序号：0 为首舰，后续舰依次递增。</summary>
	public int FormationIndex = -1;
	public int TileSourceId; // 兼容字段：来自旧 2D 编辑器的 tile ID
	/// <summary>阵营（由生成点决定），用于敌我分组。</summary>
	public GenerationSide BattleSide = GenerationSide.Player;

	// 当前战舰所在的六角格轴向坐标 (Q, R)
	public Vector2I HexCoords { get; set; }
	public int PendingDamage;
	/// <summary>本回合炮击产生的受击反馈事件。</summary>
	public List<HitFeedbackEvent> PendingHitEvents { get; } = new();
	/// <summary>本回合炮击的判定记录，回合结算时打印到 InfoLabel。</summary>
	public List<string> PendingShotChecks { get; } = new();

	private HexDirection _direction = HexDirection.N;
	private int _currentSpeed;
	private HexOrientation _mapOrientation = HexOrientation.EWHorizontal;

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
	public DamageState DamageState
	{
		get
		{
			if (CurrentHp <= 0) return DamageState.Sunk;
			if (Data != null) return Data.GetDamageState(DamageTaken);
			if (MaxHp <= 0) return DamageState.Intact;
			float pct = (float)DamageTaken / MaxHp;
			if (pct >= 0.8f) return DamageState.Heavy;
			if (pct >= 0.5f) return DamageState.Moderate;
			if (pct >= 0.25f) return DamageState.Light;
			return DamageState.Intact;
		}
	}
	public int MaxSpeedForCurrentState => Data?.MaxSpeedForState(DamageState) ?? MaxSpeed;

	/// <summary>清空待命指令（推进阶段开始时或重选船时调用）。</summary>
	public void ClearPendingCommands()
	{
		PendingSpeed = -1;
		PendingDirection = null;
		PendingAttackTarget = null;
		PendingAttackDistance = 0;
		PendingRadarUsed = false;
		PendingRadarActive = false;
	}

	// 子类覆写这个方法，不用再写一遍 AddToGroup / 找 Label3D
/// <summary>初始化：加入 Ships 分组、获取 Label3D/Flag、应用配表数据、设置初始方向与航速。</summary>
	public override void _Ready()
	{
		AddToGroup("Ships");
		SetupAttributes();   // ← 留给子类的钩子
		if (Data != null) ApplyData(Data);  // ← 预制体自带配表，自动注入
		_mapOrientation = GetNodeOrNull<LevelDataManager>("../../LevelDataManager")?.MapOrientation
			?? HexOrientation.EWHorizontal;
		Direction = InitialDirection;
		CurrentSpeed = InitialSpeed;
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
		Position = new Vector3(world.X, StackBaseY, world.Z);
	}

	/// <summary>基于六角格中心按堆叠序号沿舰船局部侧向轴左右并排，Y 始终保持同一水平高度。</summary>
	public void ApplyStackOffset(int index, int total, Vector3 hexCenter, Vector3 lateralAxis)
	{
		float zOffset = total <= 1 ? 0f : (index - (total - 1) / 2f) * StackZStep;
		Vector3 offset = lateralAxis * zOffset;
		Position = new Vector3(hexCenter.X, StackBaseY, hexCenter.Z) + offset;
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

	/// <summary>沿编队轨迹逐格移动：每到达一格立即更新 HexCoords，并按位移方向转向。</summary>
	/// <param name="headings">逐格指定的到达后航向；为空时按相邻两格位移反推。</param>
	public virtual Tween AnimateMovePath(MapGenerator map, IReadOnlyList<Vector2I> path, float perStepDuration,
		IReadOnlyList<HexDirection> headings = null)
	{
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.InOut);
		Vector2I previous = HexCoords;
		for (int i = 0; i < path.Count; i++)
		{
			Vector2I target = path[i];
			Vector3 world = map.HexToWorld(target.X, target.Y);
			Vector3 to = new Vector3(world.X, Position.Y, world.Z);
			HexDirection dir = headings != null && i < headings.Count
				? headings[i]
				: HexDirectionUtility.DirectionFromOffset(target - previous);
			tween.TweenProperty(this, "position", to, perStepDuration);
			Vector2I captured = target;
			HexDirection capturedDir = dir;
			tween.TweenCallback(Callable.From(() =>
			{
				HexCoords = captured;
				if (Direction != capturedDir)
				{
					AnimateTurnTo(capturedDir);
				}
			}));
			previous = target;
		}
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
		RotationDegrees = new Vector3(0f, DirectionYawDegrees(_direction), 0f);
	}

	private float DirectionYawDegrees(HexDirection dir)
	{
		float mapOffset = _mapOrientation == HexOrientation.NSVertical ? NSModelYawOffsetDegrees : 0f;
		return DirectionYawOffsetDegrees - (int)dir * 60f + mapOffset;
	}

	/// <summary>逻辑航向立即更新，模型沿 Y 轴平滑转到目标航向。</summary>
	public Tween AnimateTurnTo(HexDirection target, float duration = -1f)
	{
		if (_direction == target) return null;
		float turnTime = duration > 0f ? duration : Mathf.Max(0.05f, TurnTweenDuration);
		_direction = target;
		TurnedThisPhase = true;
		UpdateUi();

		float fromYaw = RotationDegrees.Y;
		float toYaw = DirectionYawDegrees(target);
		float delta = (toYaw - fromYaw + 180f) % 360f;
		delta = ((delta + 360f) % 360f) - 180f;
		float endYaw = fromYaw + delta;
		Tween tween = CreateTween();
		tween.SetTrans(Tween.TransitionType.Quad);
		tween.SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(this, "rotation:y", Mathf.DegToRad(endYaw), turnTime);
		return tween;
	}

/// <summary>显示/隐藏选中标记（TurnFlag 精灵）。</summary>
	public void ShowSelected(bool visible)
	{
		EventBus.Instance?.EmitSignal("ShipSelectionChanged", this, visible);
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
			UpdateUi();
			return;
		}
	}

	public void UpdateUi()
	{
		EventBus.Instance?.EmitSignal("ShipStatusChanged", this);
	}

	/// <summary>是否需要显示状态 Label3D。</summary>
	public bool ShouldShowStatus
		=> DamageState != DamageState.Intact || IsOffMap || TurnedThisPhase;

	/// <summary>当前状态文本：小破/中破/大破/沉没 + 离场 + 转向，无损时不输出。</summary>
	public string StatusText
	{
		get
		{
			if (DamageState == DamageState.Sunk) return "沉没";
			var status = new List<string>();
			if (DamageState != DamageState.Intact)
			{
				status.Add(DamageState switch
				{
					DamageState.Light => "小破",
					DamageState.Moderate => "中破",
					DamageState.Heavy => "大破",
					_ => "沉没"
				});
			}
			if (IsOffMap) status.Add("离场");
			if (TurnedThisPhase) status.Add("转向");
			return string.Join("\n", status);
		}
	}
}

using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 舰船静态数据资源（.tres 文件）。
/// 每个舰种一个实例，通过 Inspector 拖入 ShipComponent.Data 字段挂载。
/// 包含基础属性、三向火力、三段装甲、鱼雷参数、技能列表等。
/// </summary>
[GlobalClass]
public partial class ShipData : Resource
{
	[Export] public int TileId;
	[Export] public string ShipName = "未命名";
	[Export] public string ShipClass = "S";
	[Export] public string Rarity = "N";
	[Export] public Texture2D Portrait;
	[Export] public int PV = 0;

	[Export] public int MaxHp = 100;
	[Export] public int MoveRange = 3;
	[Export] public int AttackRange = 4;
	[Export] public int AttackPower = 35;
	[Export] public int MaxSpeed = 5;
	[Export] public int MainAmmo = 20;
	/// <summary>累计损伤点阈值：小破、中破、大破、沉没。</summary>
	[Export] public int HullLight = 11;
	[Export] public int HullModerate = 21;
	[Export] public int HullHeavy = 32;
	[Export] public int HullSunk = 42;
	/// <summary>完好、小破、中破、大破四种状态下的最大航速。</summary>
	[Export] public int SpeedIntact = 5;
	[Export] public int SpeedLight = 5;
	[Export] public int SpeedModerate = 3;
	[Export] public int SpeedHeavy = 2;

	/// <summary>兼容读取：按 [小破, 中破, 大破, 沉没] 顺序返回累计损伤点阈值。</summary>
	public int[] HullThresholds => new[] { HullLight, HullModerate, HullHeavy, HullSunk };
	/// <summary>兼容读取：按 [完好, 小破, 中破, 大破] 顺序返回状态最大航速。</summary>
	public int[] MaxSpeedByState => new[] { SpeedIntact, SpeedLight, SpeedModerate, SpeedHeavy };

	[Export] public int ForwardFire = 6;
	[Export] public int SideFire = 9;
	[Export] public int BackwardFire = 0;
	[Export] public int GunCaliber = 14;

	[Export] public int SecondaryForwardFire = 0;
	[Export] public int SecondarySideFire = 0;
	[Export] public int SecondaryBackwardFire = 0;
	[Export] public int SecondaryGunCaliber = 12;
	/// <summary>副炮基础火力；0 时按主炮 AttackPower × 口径伤害比自动折算。</summary>
	[Export] public int SecondaryAttackPower = 0;

	[Export] public int ArmorClose = 12;
	[Export] public int ArmorMedium = 8;
	[Export] public int ArmorFar = 4;

	[Export] public string TorpedoType = "";
	[Export] public int TorpedoLeftTubes = 0;
	[Export] public int TorpedoCenterTubes = 0;
	[Export] public int TorpedoRightTubes = 0;
	[Export] public bool HasSpareTorpedoes = false;
	[Export] public int TorpedoTubes = 0;
	[Export] public int TorpedoDamage = 30;

	[Export] public string RadarType = "";
	[Export] public int TurnCost = 1;
	[Export] public float TurnRate = 60f;
	[Export] public string[] SkillIds = new string[0];

	/// <summary>三向火力值，供 CombatRulesEvaluator 读取。</summary>
	public Firepower Firepower => new() { Forward = ForwardFire, Side = SideFire, Backward = BackwardFire };
	public Firepower SecondaryFirepower => new()
	{
		Forward = SecondaryForwardFire,
		Side = SecondarySideFire,
		Backward = SecondaryBackwardFire
	};

	/// <summary>按累计损伤点返回损伤状态。</summary>
	public DamageState GetDamageState(int damageTaken)
	{
		if (damageTaken >= HullSunk) return DamageState.Sunk;
		if (damageTaken >= HullHeavy) return DamageState.Heavy;
		if (damageTaken >= HullModerate) return DamageState.Moderate;
		if (damageTaken >= HullLight) return DamageState.Light;
		return DamageState.Intact;
	}

	/// <summary>返回指定损伤状态下的最大航速；未配置时退回基础 MaxSpeed。</summary>
	public int MaxSpeedForState(DamageState state)
	{
		return state switch
		{
			DamageState.Intact => SpeedIntact,
			DamageState.Light => SpeedLight,
			DamageState.Moderate => SpeedModerate,
			DamageState.Heavy => SpeedHeavy,
			_ => 0
		};
	}

	public int TotalTorpedoTubes => TorpedoLeftTubes + TorpedoCenterTubes + TorpedoRightTubes;
}

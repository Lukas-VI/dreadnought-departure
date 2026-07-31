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
	[Export] public string Rarity = "N";
	[Export] public Texture2D Portrait;

	[Export] public int MaxHp = 100;
	[Export] public int MoveRange = 3;
	[Export] public int AttackRange = 4;
	[Export] public int AttackPower = 35;
	[Export] public int MaxSpeed = 5;
	[Export] public int[] DamageThresholds = { 30, 60, 90 };

	[Export] public int ForwardFire = 6;
	[Export] public int SideFire = 9;
	[Export] public int BackwardFire = 3;
	[Export] public int GunCaliber = 14;

	[Export] public int ArmorClose = 12;
	[Export] public int ArmorMedium = 8;
	[Export] public int ArmorFar = 4;

	[Export] public int TorpedoTubes = 0;
	[Export] public int TorpedoDamage = 30;

	[Export] public int TurnCost = 1;
	[Export] public float TurnRate = 60f;
	[Export] public string[] SkillIds = new string[0];

	/// <summary>三向火力值，供 CombatRulesEvaluator 读取。</summary>
	public Firepower Firepower => new() { Forward = ForwardFire, Side = SideFire, Backward = BackwardFire };

	/// <summary>根据当前 HP 百分比返回损伤状态。</summary>
	public DamageState GetDamageState(int hp)
	{
		if (DamageThresholds == null || DamageThresholds.Length < 3) return DamageState.Intact;
		int pct = (int)((float)hp / MaxHp * 100);
		if (pct <= 0) return DamageState.Sunk;
		if (pct <= DamageThresholds[2]) return DamageState.Heavy;
		if (pct <= DamageThresholds[1]) return DamageState.Moderate;
		if (pct <= DamageThresholds[0]) return DamageState.Light;
		return DamageState.Intact;
	}
}

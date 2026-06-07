using Godot;

namespace DreadnoughtDeparture.Core;

[GlobalClass]
public partial class ShipData : Resource
{
	[Export] public int TileId;
	[Export] public string ShipName = "未命名";
	[Export] public string Rarity = "N";
	[Export] public Texture2D Portrait;

	// ── 基础属性 ──
	[Export] public int MaxHp = 100;
	[Export] public int MaxSpeed = 5;
	[Export] public int[] DamageThresholds = { 30, 60, 90 };

	// ── 火力 ──
	[Export] public int ForwardFire = 6;
	[Export] public int SideFire = 9;
	[Export] public int BackwardFire = 3;
	[Export] public int GunCaliber = 14;

	// ── 装甲 ──
	[Export] public int ArmorClose = 12;
	[Export] public int ArmorMedium = 8;
	[Export] public int ArmorFar = 4;

	// ── 鱼雷 ──
	[Export] public int TorpedoTubes = 0;
	[Export] public int TorpedoDamage = 30;

	// ── 转向 ──
	[Export] public int TurnCost = 1;
	[Export] public float TurnRate = 60f;

	// ── 技能 ──
	[Export] public string[] SkillIds = new string[0];

	// ── 便捷 ──
	public Firepower Firepower => new() { Forward = ForwardFire, Side = SideFire, Backward = BackwardFire };

	public DamageState GetDamageState(int currentHp)
	{
		int pct = (int)((float)currentHp / MaxHp * 100);
		if (pct <= 0) return DamageState.Sunk;
		if (pct <= DamageThresholds[2]) return DamageState.Heavy;
		if (pct <= DamageThresholds[1]) return DamageState.Moderate;
		if (pct <= DamageThresholds[0]) return DamageState.Light;
		return DamageState.Intact;
	}
}

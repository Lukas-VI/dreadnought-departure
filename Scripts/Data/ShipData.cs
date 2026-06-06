using Godot;

namespace DreadnoughtDeparture.Core;

[GlobalClass]
public partial class ShipData : Resource
{
    [Export] public int TileId;
    [Export] public string ShipName = "未命名";
    [Export] public string Rarity = "R";
    [Export] public Texture2D Portrait;

    // ── 基础属性 ──
    [Export] public int MaxHp = 100;
    [Export] public int MaxSpeed = 5;          // 完好状态最大航速
    [Export] public int[] DamageThresholds = { 30, 60, 90 }; // [小破, 中破, 大破] HP 阈值

    // ── 火力 ──
    [Export] public int ForwardFire = 6;       // 前向火力骰子数
    [Export] public int SideFire = 9;          // 侧向火力骰子数
    [Export] public int BackwardFire = 3;      // 后向火力骰子数
    [Export] public int GunCaliber = 14;       // 主炮口径（英寸）

    // ── 装甲 ──
    [Export] public int ArmorClose = 12;       // 近距离装甲值
    [Export] public int ArmorMedium = 8;       // 中距离装甲值
    [Export] public int ArmorFar = 4;          // 远距离装甲值

    // ── 鱼雷 ──
    [Export] public int TorpedoTubes = 0;      // 鱼雷发射管数（0 = 无雷装）
    [Export] public int TorpedoDamage = 30;    // 单发鱼雷伤害

    // ── 转向 ──
    [Export] public int TurnCost = 1;          // 每次转向消耗的移动力

    // ── 技能 ──
    [Export] public string[] SkillIds = new string[0];

    // ── 便捷访问 ──
    public Firepower Firepower => new() { Forward = ForwardFire, Side = SideFire, Backward = BackwardFire };

    // 根据当前 HP 查损伤状态
    public DamageState GetDamageState(int currentHp)
    {
        int percent = (int)((float)currentHp / MaxHp * 100);
        if (percent <= 0) return DamageState.Sunk;
        if (percent <= DamageThresholds[2]) return DamageState.Heavy;
        if (percent <= DamageThresholds[1]) return DamageState.Moderate;
        if (percent <= DamageThresholds[0]) return DamageState.Light;
        return DamageState.Intact;
    }
}


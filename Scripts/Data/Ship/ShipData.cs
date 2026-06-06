using Godot;

namespace DreadnoughtDeparture.Core;

[GlobalClass]
public partial class ShipData : Resource
{
	[Export] public int TileId;             // 2D 编辑器对应 tile ID
	[Export] public string ShipName = "未命名";
	[Export] public int MaxHp = 100;
	[Export] public int MoveRange = 3;
	[Export] public int AttackRange = 4;
	[Export] public int AttackPower = 35;
	[Export] public string Rarity = "R";
	[Export] public Texture2D Portrait;

	// ── 海战惯性属性 ──
	[Export] public int Speed = 3;          // 每回合移动格数
	[Export] public float TurnRate = 60f;   // 每格最大转向角（度）
	[Export] public PackedScene Prefab;
	[Export] public string[] SkillIds = new string[0]; // 专属技能 ID，轮盘动态生成按钮
}

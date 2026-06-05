using Godot;

namespace DreadnoughtDeparture.Core;

[GlobalClass]
public partial class ShipData : Resource
{
	[Export] public string ShipName = "未命名";
	[Export] public int MaxHp = 100;
	[Export] public int MoveRange = 3;
	[Export] public int AttackRange = 4;
	[Export] public int AttackPower = 35;
	[Export] public string Rarity = "R";  // R / SR / SSR
	[Export] public Texture2D Portrait;   // 立绘（将来用）
}

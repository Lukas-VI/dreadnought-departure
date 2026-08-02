using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 冲撞裁定：1D10 ≤ 2 发生冲撞；损伤按两船最大船体值之和查简化 A3 表。
/// </summary>
public static class CollisionRulesEvaluator
{
	private static readonly Random _rng = new();

	public static bool IsCollision() => _rng.Next(1, 11) <= 2;

	/// <summary>返回 (1D10, 对应损伤)，按船体值之和分段。</summary>
	public static (int roll, int damage) RollDamage(int hullSum)
	{
		int roll = _rng.Next(1, 11);
		int damage = hullSum switch
		{
			<= 8 => Math.Max(1, roll / 3),
			<= 16 => Math.Max(1, roll / 2),
			_ => Math.Max(1, roll / 2)
		};
		return (roll, damage);
	}
}

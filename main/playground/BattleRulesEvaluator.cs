using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public static class BattleRulesEvaluator
{
    // 计算六角格距离
    public static int GetHexDistance(Vector2I a, Vector2I b)
    {
        return (Mathf.Abs(a.X - b.X) + Mathf.Abs(a.X + a.Y - (b.X + b.Y)) + Mathf.Abs(a.Y - b.Y)) / 2;
    }

    // 未来可以在这里写更复杂的战棋规则：
    // public static int CalculateDamage(ShipComponent attacker, ShipComponent defender) { ... }
    // public static bool IsObstacleInWay(Vector2I from, Vector2I to) { ... }
}
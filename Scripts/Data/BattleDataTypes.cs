using System;

namespace DreadnoughtDeparture.Core;

// =============================================
//  六角格 6 向 · 航向（正对格边，不对顶点）
// =============================================
public enum HexDirection
{
    N = 0, NE = 1, SE = 2, S = 3, SW = 4, NW = 5
}

// =============================================
//  速度 → 三阶段移动力映射表（A2 表）
//  "+" 号表示：奇数回合 +1 格，偶数回合 +0 格
// =============================================
public readonly struct SpeedPhaseEntry
{
    public readonly int Phase1, Phase2, Phase3;
    public readonly bool HasAlternate; // 是否有 "+" 号

    public SpeedPhaseEntry(int p1, int p2, int p3, bool alt = false)
    {
        Phase1 = p1; Phase2 = p2; Phase3 = p3; HasAlternate = alt;
    }
}

public static class SpeedTable
{
    // A2 表：速度 → [Phase1, Phase2, Phase3]
    public static readonly SpeedPhaseEntry[] Table =
    {
        new(0, 0, 0),             // Speed 0: 停船
        new(0, 0, 0, true),       // Speed 1: "+"
        new(0, 1, 0, true),       // Speed 2: "+"
        new(1, 1, 0, false),      // Speed 3
        new(1, 1, 0, true),       // Speed 4: "+"
        new(1, 1, 1, false),      // Speed 5
        new(2, 1, 1, false),      // Speed 6
        new(2, 2, 1, false),      // Speed 7
        new(2, 2, 1, true),       // Speed 8: "+"
        new(2, 2, 2, false),      // Speed 9
        new(3, 2, 2, false),      // Speed 10
        new(3, 3, 2, false),      // Speed 11
        new(3, 3, 2, true),       // Speed 12: "+"
    };

    // 本阶段能走的格数
    public static int MoveForPhase(int speed, int phase, bool isOddTurn)
    {
        if (speed < 0 || speed >= Table.Length) return 0;
        var entry = Table[speed];
        int baseMove = phase switch { 1 => entry.Phase1, 2 => entry.Phase2, _ => entry.Phase3 };
        if (entry.HasAlternate && isOddTurn) baseMove++;
        return baseMove;
    }

    // 速度调整合法性：新速 ∈ [旧速-2, 旧速+3]，且 ≤ 最大航速
    public static bool CanAdjustSpeed(int oldSpeed, int newSpeed, int maxSpeed)
        => newSpeed >= Math.Max(0, oldSpeed - 2)
        && newSpeed <= Math.Min(oldSpeed + 3, maxSpeed);
}

// =============================================
//  损伤状态
// =============================================
public enum DamageState { Intact = 0, Light = 1, Moderate = 2, Heavy = 3, Sunk = 4 }

// =============================================
//  射界火力
// =============================================
[Serializable]
public struct Firepower
{
    public int Forward, Side, Backward;
    public int ForArc(HexDirection shipDir, HexDirection targetDir)
    {
        int diff = ((int)targetDir - (int)shipDir + 6) % 6;
        return diff switch { 0 or 5 => Forward, 1 or 4 => Side, _ => Backward };
    }
}


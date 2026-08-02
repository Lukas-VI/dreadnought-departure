using Godot;
using System;

namespace DreadnoughtDeparture.Core;

// =============================================
//  六角格 6 向 · 航向（正对格边，不对顶点）
// =============================================
public enum HexDirection
{
	N = 0, NE = 1, SE = 2, S = 3, SW = 4, NW = 5
}

public class UnitSpawnData
{
	// 兼容字段：旧地图用 TileId 记录标记来源
	public int TileId { get; set; }
	// 新字段：优先用 ShipId 找 ShipCatalog 里的预制体
	public string ShipId { get; set; } = "";
	// 阵营（从生成点继承，用于敌我分组）
	public GenerationSide Side { get; set; } = GenerationSide.Player;
	public HexDirection Direction { get; set; } = HexDirection.N;
	public int Speed { get; set; }
}

// 六角格轴向偏移工具
public static class HexDirectionUtility
{
	// flat-top 轴向坐标 (q, r) 每步偏移
	public static Vector2I Offset(HexDirection dir) => dir switch
	{
		HexDirection.N  => new Vector2I( 0, -1),
		HexDirection.NE => new Vector2I( 1, -1),
		HexDirection.SE => new Vector2I( 1,  0),
		HexDirection.S  => new Vector2I( 0,  1),
		HexDirection.SW => new Vector2I(-1,  1),
		HexDirection.NW => new Vector2I(-1,  0),
		_ => Vector2I.Zero
	};

	// 转向操作
	public static HexDirection TurnLeft(HexDirection d)  => (HexDirection)(((int)d + 5) % 6);
	public static HexDirection TurnRight(HexDirection d) => (HexDirection)(((int)d + 1) % 6);

	/// <summary>按单步轴向偏移反查航向；非法偏移回退 N。</summary>
	public static HexDirection DirectionFromOffset(Vector2I offset)
	{
		foreach (HexDirection dir in System.Enum.GetValues<HexDirection>())
			if (Offset(dir) == offset) return dir;
		return HexDirection.N;
	}
}

/// <summary>地图六角格朝向：EW = 平行边水平（默认地图），NS = 尖角上下。</summary>
public enum HexOrientation
{
	EWHorizontal = 0,
	NSVertical = 1
}

/// <summary>轴向坐标 ↔ TileMap offset 坐标换算，随地图朝向切换，避免编辑器与战斗各写一套。</summary>
public static class HexGrid
{
	public static Vector2I CellFromAxial(HexOrientation orientation, Vector2I axial)
	{
		return orientation == HexOrientation.NSVertical
			? new Vector2I(axial.X, axial.Y + (axial.X >> 1))
			: new Vector2I(axial.X + (axial.Y >> 1), axial.Y);
	}

	public static Vector2I AxialFromCell(HexOrientation orientation, Vector2I cell)
	{
		return orientation == HexOrientation.NSVertical
			? new Vector2I(cell.X, cell.Y - (cell.X >> 1))
			: new Vector2I(cell.X - (cell.Y >> 1), cell.Y);
	}
}

// =============================================
//  速度 → 三阶段移动力映射表（A2 表）
//  "+" 号表示：该档位每阶段额外 +1 格；回合不再区分奇偶，不按回合奇偶交替
// =============================================
public readonly struct SpeedPhaseEntry
{
	public readonly int Phase1, Phase2, Phase3;
	public readonly bool HasAlternate;

	public SpeedPhaseEntry(int p1, int p2, int p3, bool alt = false)
	{
		Phase1 = p1; Phase2 = p2; Phase3 = p3; HasAlternate = alt;
	}
}

public static class SpeedTable
{
	public static readonly SpeedPhaseEntry[] Table =
	{
		new(0, 0, 0),        // Speed 0: 停船
		new(0, 0, 0, true),  // Speed 1: 第一移动阶段 0+，仅奇数回合移动 1 格
		new(1, 0, 0, false), // Speed 2
		new(1, 1, 0, false), // Speed 3
		new(1, 1, 0, false), // Speed 4
		new(1, 1, 1, false), // Speed 5
		new(2, 1, 1, false), // Speed 6
		new(2, 2, 1, false), // Speed 7
		new(2, 2, 2, false), // Speed 8
	};

	public static int MoveForPhase(int speed, int phase, bool oddTurn)
	{
		if (speed < 0 || speed >= Table.Length) return 0;
		var entry = Table[speed];
		int baseMove = phase switch { 1 => entry.Phase1, 2 => entry.Phase2, _ => entry.Phase3 };
		if (entry.HasAlternate && oddTurn && phase == 1)
			baseMove = Math.Max(baseMove, 1);
		return baseMove;
	}

	public static bool CanAdjustSpeed(int oldSpeed, int newSpeed, int maxSpeed)
		=> newSpeed >= Math.Max(0, oldSpeed - 2)
		&& newSpeed <= Math.Min(oldSpeed + 3, maxSpeed);
}

// =============================================
//  损伤状态
// =============================================
public enum DamageState { Intact = 0, Light = 1, Moderate = 2, Heavy = 3, Sunk = 4 }

// =============================================
//  地形类型
// =============================================
public enum HexTerrainType { DeepSea = 0, Reef = 1, Island = 2 }

// =============================================
//  单位战术状态
// =============================================
public enum UnitTacticalState { Idle = 0, Actioned = 1, Sunk = 2 }

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
		// 正前/正后各 60°，左右侧射各 120°（每侧覆盖两个相邻六向）。
		return diff switch { 0 => Forward, 3 => Backward, _ => Side };
	}
}


// =============================================
//  生成点与初设船（编辑器 v3 数据模型）
// =============================================

/// <summary>生成点阵营：玩家 / 敌方。</summary>
public enum GenerationSide { Player = 0, Enemy = 1 }

/// <summary>一个生成点：阵营 + 所用 tileset 源 ID（仅代表标记外观，不代表船型）。</summary>
public class GenerationPointData
{
	public GenerationSide Side { get; set; } = GenerationSide.Player;
	public int SourceId { get; set; } = 4;
}

/// <summary>挂在生成点上的初设船。ShipId 是 ShipCatalog 中的全局 ID（如 "dreadnought"）。</summary>
public class ShipSpawnData
{
	public string ShipId { get; set; } = "";
	public HexDirection Direction { get; set; } = HexDirection.N;
	public int Speed { get; set; }
}

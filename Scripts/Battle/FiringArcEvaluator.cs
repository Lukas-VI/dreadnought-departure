using Godot;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>目标相对舰艏的射界扇区。</summary>
public enum FiringArc
{
	Center,
	Front,
	Rear,
	Port,
	Starboard
}

/// <summary>
/// 按六角格真实角度划分射界：正前/正后各 60°（±30°），左右侧射各 120°。
/// 使用尖顶六边形轴向坐标换算，整体顺时针偏转 60° 与六角格对齐。
/// </summary>
public static class FiringArcEvaluator
{
	public static FiringArc GetArc(Vector2I shipHex, Vector2I targetHex, HexDirection facing)
	{
		int dq = targetHex.X - shipHex.X;
		int dr = targetHex.Y - shipHex.Y;
		if (dq == 0 && dr == 0) return FiringArc.Center;

		double x = Math.Sqrt(3.0) * (dq + dr / 2.0);
		double y = 1.5 * dr;
		double angle = Math.Atan2(y, x) * 180.0 / Math.PI % 360.0;
		if (angle < 0.0) angle += 360.0;

		double facingAngle = (int)facing * 60.0 + 60.0;
		double relAngle = (angle - facingAngle) % 360.0;
		if (relAngle < 0.0) relAngle += 360.0;

		if (relAngle <= 30.1 || relAngle >= 329.9) return FiringArc.Front;
		if (relAngle >= 149.9 && relAngle <= 210.1) return FiringArc.Rear;
		if (relAngle > 30.1 && relAngle < 149.9) return FiringArc.Port;
		return FiringArc.Starboard;
	}
}

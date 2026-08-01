using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 编辑器画布与 3D 战场共用同一套轴向坐标投影。
/// 编辑器只在这里换算屏幕坐标，避免再引入第二套 offset/TileSet 逻辑。
/// </summary>
public static class HexMath
{
	public const float EditorHexRadius = 60f;

	public static Vector2 HexToLocal(HexOrientation orientation, Vector2I axial, float radius = EditorHexRadius)
	{
		// EW 平边水平用平边轴向投影；NS 尖角上下用尖角轴向投影，避免点状六边形错位留空。
		float x = orientation == HexOrientation.NSVertical
			? Mathf.Sqrt(3f) * radius * (axial.X + axial.Y * 0.5f)
			: 1.5f * radius * axial.X;
		float y = orientation == HexOrientation.NSVertical
			? 1.5f * radius * axial.Y
			: Mathf.Sqrt(3f) * radius * (axial.Y + axial.X * 0.5f);
		return new Vector2(x, y);
	}

	public static Vector2I LocalToHex(HexOrientation orientation, Vector2 local, float radius = EditorHexRadius)
	{
		float q, r;
		if (orientation == HexOrientation.NSVertical)
		{
			q = (local.X / Mathf.Sqrt(3f) - local.Y / 3f) / radius;
			r = (local.Y * 2f / 3f) / radius;
		}
		else
		{
			q = (local.X * 2f / 3f) / radius;
			r = (-local.X / 3f + Mathf.Sqrt(3f) / 3f * local.Y) / radius;
		}
		float s = -q - r;
		int rq = Mathf.RoundToInt(q);
		int rr = Mathf.RoundToInt(r);
		int rs = Mathf.RoundToInt(s);
		if (Mathf.Abs(rq - q) > Mathf.Abs(rs - s) && Mathf.Abs(rq - q) > Mathf.Abs(rr - r))
			rq = -rs - rr;
		else if (Mathf.Abs(rr - r) > Mathf.Abs(rs - s))
			rr = -rq - rs;
		return new Vector2I(rq, rr);
	}

	/// <summary>返回闭合六边形顶点（EW 平边水平，NS 尖角上下）。</summary>
	public static Vector2[] HexagonPoints(HexOrientation orientation, Vector2 center, float radius = EditorHexRadius)
	{
		var points = new Vector2[7];
		float start = orientation == HexOrientation.NSVertical ? Mathf.Pi / 2f : 0f;
		for (int i = 0; i < 6; i++)
		{
			float angle = start - i * Mathf.Pi / 3f;
			points[i] = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
		}
		points[6] = points[0];
		return points;
	}
}

using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 视野规则：基本视野内且视线不跨岛屿可目视；装有雷达的船可在雷达范围内扩展交战能力。
/// </summary>
public static class VisionRulesEvaluator
{
	public static bool HasLineOfSight(Vector2I from, Vector2I to, LevelDataManager data)
	{
		if (data == null) return true;
		if (from == to) return true;
		HexOrientation orientation = data.MapOrientation;
		Vector2 a = HexMath.HexToLocal(orientation, from, GameConfig.HexRadius);
		Vector2 b = HexMath.HexToLocal(orientation, to, GameConfig.HexRadius);
		float distance = a.DistanceTo(b);
		int samples = Mathf.Max(4, Mathf.CeilToInt(distance / (GameConfig.HexRadius * 0.3f)));
		for (int i = 1; i < samples; i++)
		{
			Vector2 point = a.Lerp(b, (float)i / samples);
			Vector2I hex = HexMath.LocalToHex(orientation, point, GameConfig.HexRadius);
			if (data.IsIsland(hex)) return false;
		}
		return true;
	}

	public static bool HasVisual(ShipComponent observer, ShipComponent target, LevelDataManager data)
	{
		if (data == null) return true;
		int dist = BattleRulesEvaluator.GetHexDistance(observer.HexCoords, target.HexCoords);
		return dist <= data.BasicVision && HasLineOfSight(observer.HexCoords, target.HexCoords, data);
	}

	public static bool HasRadarContact(ShipComponent observer, ShipComponent target, LevelDataManager data)
	{
		string radar = observer.Data?.RadarType;
		if (string.IsNullOrEmpty(radar) || data == null) return false;
		int dist = BattleRulesEvaluator.GetHexDistance(observer.HexCoords, target.HexCoords);
		return dist <= RadarRulesEvaluator.GetRange(radar)
			&& HasLineOfSight(observer.HexCoords, target.HexCoords, data);
	}

	public static bool CanEngage(ShipComponent observer, ShipComponent target, LevelDataManager data)
		=> HasVisual(observer, target, data) || HasRadarContact(observer, target, data);

	public static bool IsRadarOnly(ShipComponent observer, ShipComponent target, LevelDataManager data)
		=> !HasVisual(observer, target, data) && HasRadarContact(observer, target, data);
}

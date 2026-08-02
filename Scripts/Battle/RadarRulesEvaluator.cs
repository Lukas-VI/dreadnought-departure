namespace DreadnoughtDeparture.Core;

/// <summary>雷达性能表：有效距离与命中修正。</summary>
public static class RadarRulesEvaluator
{
	public static int GetRange(string radarType)
	{
		if (string.IsNullOrEmpty(radarType)) return 0;
		string key = radarType.ToLowerInvariant();
		if (key.Contains("c")) return 16;
		if (key.Contains("b")) return 10;
		if (key.Contains("a")) return 10;
		return 0;
	}

	public static int GetHitModifier(string radarType)
	{
		if (string.IsNullOrEmpty(radarType)) return 0;
		string key = radarType.ToLowerInvariant();
		if (key.Contains("jp")) return -3;
		if (key.Contains("us")) return key.Contains("a") ? -2 : 0;
		return 0;
	}
}

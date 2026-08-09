namespace DreadnoughtDeparture.Core;

/// <summary>关卡 Special 表约定：不同数值代表不同类型特殊格。</summary>
public enum SpecialCellType
{
	Story = 1,
	Encounter = 2,
	Supply = 3,
	Radar = 4,
	Hazard = 5,
	Objective = 6,
}

public static class SpecialCellCatalog
{
	public static string Name(int specialId) => ((SpecialCellType)specialId) switch
	{
		SpecialCellType.Story => "剧情触发",
		SpecialCellType.Encounter => "遭遇战",
		SpecialCellType.Supply => "补给点",
		SpecialCellType.Radar => "雷达站",
		SpecialCellType.Hazard => "危险区",
		SpecialCellType.Objective => "目标点",
		_ => "未知特殊格",
	};
}

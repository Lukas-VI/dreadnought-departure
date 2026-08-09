using Godot;

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

	public static string ScenePath(int specialId) => specialId switch
	{
		1 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_story.tscn",
		2 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_encounter.tscn",
		3 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_supply.tscn",
		4 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_radar.tscn",
		5 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_hazard.tscn",
		6 => "res://Scenes/Map/Tile/Prefab/Overlay3D/Special/special_objective.tscn",
		_ => "",
	};

	public static Color ColorFor(int specialId) => specialId switch
	{
		1 => new Color(0.95f, 0.75f, 0.2f, 0.6f),
		2 => new Color(0.9f, 0.25f, 0.25f, 0.6f),
		3 => new Color(0.25f, 0.9f, 0.4f, 0.6f),
		4 => new Color(0.25f, 0.8f, 0.95f, 0.6f),
		5 => new Color(0.95f, 0.45f, 0.15f, 0.6f),
		6 => new Color(0.55f, 0.35f, 0.95f, 0.6f),
		_ => new Color(1f, 0.78f, 0.25f, 0.55f),
	};
}

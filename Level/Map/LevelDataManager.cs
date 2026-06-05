using Godot;
using System.Collections.Generic;

using System.Text.Json;

namespace DreadnoughtDeparture.Core;

public partial class LevelDataManager : Node
{
	[Export] public string MapId = "map_01";
	[Export] public bool ForceReextract = false;
	[Export] public PackedScene MapEditorScene; 

	// 提供给全天下的纯粹数据字典
	public Dictionary<Vector2I, string> TerrainData { get; private set; } = new();
	public Dictionary<Vector2I, int> UnitData { get; private set; } = new();

	private string _currentMapId;

	public override void _Ready()
	{
		string jsonPath = $"user://maps/{MapId}.json";

		// 0. 判断数据来源：读档 还是 从 2D 场景抠
		if (!ForceReextract && LoadFromJson(jsonPath))
		{
			// 运行时：直接从 JSON 读到了地图数据，啥也不用干
		}
		else if (MapEditorScene != null)
		{
			// 开发期：从 2D 关卡编辑器场景抠数据，然后顺手存成 JSON
			ExtractFromTileMap();
			SaveToJson(jsonPath);
		}
		else
		{
			GD.PrintErr("❌ 错误: LevelDataManager —— 既没 JSON 也没绑 2D 场景！");
			return;
		}

		// 1. 数据就绪——GameplayDirector 会在自己的 _Ready 里 CallDeferred 启动战场
	}

	// ── 从 2D TileMap 抠数据 ──────────────────

	private void ExtractFromTileMap()
	{
		// 悄悄实例化 2D 场景
		Node2D editorInstance = MapEditorScene.Instantiate<Node2D>();
		
		TileMapLayer terrainLayer = editorInstance.GetNode<TileMapLayer>("TerrainLayer");
		TileMapLayer unitLayer = editorInstance.GetNode<TileMapLayer>("UnitLayer");

		// 抠取地形数据
		if (terrainLayer != null)
		{
			foreach (Vector2I cellCoords in terrainLayer.GetUsedCells())
			{
				Vector2I axial = ConvertToAxial(cellCoords);
				int tileId = terrainLayer.GetCellSourceId(cellCoords);
				TerrainData[axial] = (tileId == 1) ? "island" : "ocean";
			}
		}

		// 抠取单位数据
		if (unitLayer != null)
		{
			foreach (Vector2I cellCoords in unitLayer.GetUsedCells())
			{
				Vector2I axial = ConvertToAxial(cellCoords);
				UnitData[axial] = unitLayer.GetCellSourceId(cellCoords);
			}
		}

		// 寿终正寝，释放内存
		editorInstance.QueueFree();
		GD.Print($"--- 👑 LevelDataManager: 成功抠取地形 {TerrainData.Count} 个，单位 {UnitData.Count} 个 ---");
	}

	// 数学转换公式安心地躺在这里
	private Vector2I ConvertToAxial(Vector2I cellCoords)
	{
		int r = cellCoords.Y;
		int q = cellCoords.X - (cellCoords.Y >> 1); 
		return new Vector2I(q, r);
	}

	// ── 持久化 ──────────────────────────────

	private class MapSaveData
	{
		public string Name { get; set; } = "untitled";
		public int Version { get; set; } = 1;
		public Dictionary<string, string> Terrain { get; set; } = new();
		public Dictionary<string, int> Units { get; set; } = new();
	}

	// Vector2I ↔ "q,r" 字符串
	private static string SerializeKey(Vector2I v) => $"{v.X},{v.Y}";

	private static Vector2I DeserializeKey(string s)
	{
		var parts = s.Split(',');
		return new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
	}

	// 存盘
	private void SaveToJson(string path)
	{
		var data = new MapSaveData { Name = _currentMapId, Version = 1 };
		foreach (var kv in TerrainData) data.Terrain[SerializeKey(kv.Key)] = kv.Value;
		foreach (var kv in UnitData)    data.Units[SerializeKey(kv.Key)] = kv.Value;
		
		var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
		DirAccess.MakeDirRecursiveAbsolute("user://maps");
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file != null) file.StoreString(json);
		else GD.PrintErr($"❌ SaveToJson: 无法写入 {path}");
		GD.Print($"💾 地图已保存: {path}");
	}

	// 读档
	private bool LoadFromJson(string path)
	{
		if (!FileAccess.FileExists(path)) return false;

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		var data = JsonSerializer.Deserialize<MapSaveData>(file.GetAsText());
		if (data == null) return false;

		TerrainData.Clear();
		UnitData.Clear();
		foreach (var kv in data.Terrain) TerrainData[DeserializeKey(kv.Key)] = kv.Value;
		foreach (var kv in data.Units)    UnitData[DeserializeKey(kv.Key)] = kv.Value;

		_currentMapId = data.Name;
		GD.Print($"📂 地图已加载: {path} ({TerrainData.Count} tiles, {UnitData.Count} units)");
		return true;
	}
}

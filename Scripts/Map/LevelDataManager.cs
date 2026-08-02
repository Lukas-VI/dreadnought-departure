using Godot;
using System.Collections.Generic;
using System.Text.Json;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 地图数据中枢（v3 模型）。
/// 编辑器与战斗场景共用：
///  - TerrainSources / GenerationPoints / SpecialTiles / ShipSpawns 为四张独立表，互不影响；
///  - JSON 持久化到 res://export/maps/ 的专用导出文件夹；
///  - 保留 TerrainData / UnitData 旧接口，供 3D 战斗生成器读取。
/// </summary>
public partial class LevelDataManager : Node
{
	// ── 全局静态账本：供寻路/雷达/射击直接查格子 ──
	public static Dictionary<Vector2I, HexTerrainType> GridTopology { get; private set; } = new();
	public static Dictionary<Vector2I, ShipComponent> BattlefieldUnits { get; private set; } = new();

	// ── 导出文件夹（编辑器画布 JSON 专用）──
	public const string DefaultExportFolder = "res://export/maps";

	[Export] public string MapId = "map_01";
	/// <summary>画布菜单选中的画布文件名；进入场景后由 _Ready 消费并清空。</summary>
	public static string RuntimeMapRequest;
	/// <summary>当前战役画布名；战役重试时沿用，避免退回 map_01。</summary>
	public static string ActiveCampaignMap;
	// 编辑器场景置 false：画布由编辑器显式打开，避免启动时自动加载 map_01
	[Export] public bool AutoLoadOnReady = true;

	// ── v3 运行时表（编辑器直接读写这些）──
	public Dictionary<Vector2I, int> TerrainSources { get; private set; } = new();
	public Dictionary<Vector2I, GenerationPointData> GenerationPoints { get; private set; } = new();
	public Dictionary<Vector2I, int> SpecialTiles { get; private set; } = new();
	public Dictionary<Vector2I, List<ShipSpawnData>> ShipSpawns { get; private set; } = new();

	// ── 旧接口兼容视图（3D 战斗用）──
	/// <summary>地形字符串字典（"island"/"ocean"），由 TerrainSources 实时派生。</summary>
	public Dictionary<Vector2I, string> TerrainData
	{
		get
		{
			var result = new Dictionary<Vector2I, string>();
			foreach (var kv in TerrainSources)
				result[kv.Key] = SourceIdToTerrain(kv.Value);
			return result;
		}
	}

	/// <summary>单位初设字典，由生成点 + 船初设实时派生（TileId 为兼容字段，主查找走 ShipId）。</summary>
	public Dictionary<Vector2I, List<UnitSpawnData>> UnitData
	{
		get
		{
			var result = new Dictionary<Vector2I, List<UnitSpawnData>>();
			foreach (var kv in ShipSpawns)
			{
				if (kv.Value == null || kv.Value.Count == 0) continue;
				if (!GenerationPoints.TryGetValue(kv.Key, out var gen)) continue;
				var list = new List<UnitSpawnData>();
				foreach (var ship in kv.Value)
				{
					list.Add(new UnitSpawnData
					{
						ShipId = ship.ShipId,
						TileId = gen.SourceId,
						Side = gen.Side,
						Direction = ship.Direction,
						Speed = ship.Speed
					});
				}
				result[kv.Key] = list;
			}
			return result;
		}
	}

	public string CurrentMapName { get; private set; } = "untitled";
	/// <summary>地图类型："day"/"night"，决定照明阶段是否启用。</summary>
	public string MapType { get; private set; } = "day";
	public bool IsNightBattle => MapType == "night";
	// ── 关卡初设（编辑器可配置，旧地图使用默认值）──
	public int PlayerCommand { get; private set; } = 5;
	public int EnemyCommand { get; private set; } = 4;
	public int PlayerInitialCP { get; private set; } = 8;
	public int EnemyInitialCP { get; private set; } = 8;
	public int InitiativeValue { get; private set; } = 5;
	public string InitiativeOwner { get; private set; } = "player";
	public int BasicVision { get; private set; } = 6;
	public int TorpedoModePlayer { get; private set; } = 7;
	public int TorpedoModeEnemy { get; private set; } = 4;
	public int MaxTurns { get; private set; } = 18;
	/// <summary>鱼雷阶段是否启用；当前鱼雷玩法未实现，默认关闭。</summary>
	public bool TorpedoPhaseEnabled { get; private set; }
	/// <summary>各阶段每船限时（秒）：速度/三移动/视野/炮击/鱼雷/结算。</summary>
	public int[] PhaseSecondsPerShip { get; private set; } = { 5, 5, 5, 5, 5, 10, 10, 0 };
	public int PhaseExtraSeconds { get; private set; } = 5;
	/// <summary>地图六角格朝向：EW 平行边水平 / NS 尖角上下。</summary>
	public HexOrientation MapOrientation { get; private set; } = HexOrientation.EWHorizontal;
	/// <summary>是否由画布菜单的 RuntimeMapRequest 自动打开了画布；编辑器据此决定是否默认显示画布列表。</summary>
	public bool MapAutoOpened { get; private set; }
	private string _currentJsonPath;

	public override void _Ready()
	{
		// 画布菜单把选中画布名写进 RuntimeMapRequest；进入场景后消费一次
		string requestedName = RuntimeMapRequest;
		RuntimeMapRequest = null;
		if (!string.IsNullOrEmpty(requestedName) && LoadMap(requestedName))
		{
			MapAutoOpened = true;
			return;
		}

		if (!AutoLoadOnReady) return;

		// 战役重试：没有新选择时沿用上次战役画布
		if (string.IsNullOrEmpty(requestedName) && !string.IsNullOrEmpty(ActiveCampaignMap)
			&& LoadMap(ActiveCampaignMap))
		{
			return;
		}

		// 战斗场景：优先读导出文件夹，其次读旧 user://maps
		string jsonPath = $"{DefaultExportFolder}/{MapId}.json";
		_currentJsonPath = jsonPath;

		if (LoadMap(jsonPath))
		{
			// 已从 JSON 读取
		}
		else if (LoadMap($"user://maps/{MapId}.json"))
		{
			// 兼容旧版本存放位置
		}
		else
		{
			GD.PrintErr("错误: LevelDataManager —— 没有找到可加载的 JSON 地图！");
		}
	}

	// ── 编辑器 API：地形 ──

	public int GetTerrainSource(Vector2I hex) => TerrainSources.GetValueOrDefault(hex, -1);
	/// <summary>该格是否为岛屿地形（sourceId == 1）。</summary>
	public bool IsIsland(Vector2I hex) => GetTerrainSource(hex) == 1;
	public void SetTerrain(Vector2I hex, int sourceId)
	{
		if (sourceId < 0) TerrainSources.Remove(hex);
		else TerrainSources[hex] = sourceId;
	}
	/// <summary>移除一个地形格。</summary>
	public void EraseTerrain(Vector2I hex) => TerrainSources.Remove(hex);

	// ── 编辑器 API：生成点 ──

	public GenerationPointData GetGenerationAt(Vector2I hex) => GenerationPoints.GetValueOrDefault(hex);
	public void SetGeneration(Vector2I hex, GenerationSide side, int sourceId)
	{
		GenerationPoints[hex] = new GenerationPointData { Side = side, SourceId = sourceId };
	}
	/// <summary>移除生成点及其船初设。</summary>
	public void EraseGeneration(Vector2I hex)
	{
		GenerationPoints.Remove(hex);
		ShipSpawns.Remove(hex); // 生成点删除时连同船初设一起清掉
	}

	// ── 编辑器 API：特殊 ──

	public int GetSpecialSource(Vector2I hex) => SpecialTiles.GetValueOrDefault(hex, -1);
	public void SetSpecial(Vector2I hex, int sourceId)
	{
		if (sourceId < 0) SpecialTiles.Remove(hex);
		else SpecialTiles[hex] = sourceId;
	}
	/// <summary>移除一个特殊格。</summary>
	public void EraseSpecial(Vector2I hex) => SpecialTiles.Remove(hex);

	// ── 编辑器 API：船初设（每个生成点最多 2 艘）──

	public const int MaxShipsPerTile = 2;

	public IReadOnlyList<ShipSpawnData> GetShipsAt(Vector2I hex)
	{
		return ShipSpawns.TryGetValue(hex, out var list) ? list : Array.Empty<ShipSpawnData>();
	}

	/// <summary>添加船初设；超过上限返回 false。</summary>
	public bool AddShip(Vector2I hex, ShipSpawnData ship)
	{
		if (!GenerationPoints.ContainsKey(hex)) return false;
		if (!ShipSpawns.TryGetValue(hex, out var list))
		{
			list = new List<ShipSpawnData>();
			ShipSpawns[hex] = list;
		}
		if (list.Count >= MaxShipsPerTile) return false;
		list.Add(ship);
		return true;
	}

	/// <summary>按堆叠索引移除船初设。</summary>
	public bool RemoveShipAt(Vector2I hex, int index)
	{
		if (!ShipSpawns.TryGetValue(hex, out var list)) return false;
		if (index < 0 || index >= list.Count) return false;
		list.RemoveAt(index);
		if (list.Count == 0) ShipSpawns.Remove(hex);
		return true;
	}

	/// <summary>按堆叠索引覆盖船初设。</summary>
	public bool SetShip(Vector2I hex, int index, ShipSpawnData ship)
	{
		if (!ShipSpawns.TryGetValue(hex, out var list)) return false;
		if (index < 0 || index >= list.Count) return false;
		list[index] = ship;
		return true;
	}

	/// <summary>清空该格的船初设。</summary>
	public void ClearShips(Vector2I hex) => ShipSpawns.Remove(hex);

	// ── 旧接口兼容（RuntimeMapEditorController 使用）──

	public IReadOnlyList<UnitSpawnData> GetUnitsAt(Vector2I coords)
	{
		var ships = GetShipsAt(coords);
		var gen = GetGenerationAt(coords);
		if (gen == null) return Array.Empty<UnitSpawnData>();
		var result = new List<UnitSpawnData>();
		foreach (var s in ships)
			result.Add(new UnitSpawnData { TileId = gen.SourceId, ShipId = s.ShipId, Direction = s.Direction, Speed = s.Speed, Side = gen.Side });
		return result;
	}

	/// <summary>旧接口：按堆叠索引更新初设（TileId 兼容映射）。</summary>
	public bool SetUnitInitialState(Vector2I coords, int stackIndex, int tileId, HexDirection direction, int speed)
	{
		if (stackIndex < 0) return false;
		var ships = GetShipsAt(coords);
		if (stackIndex >= ships.Count) return false;
		string shipId = LegacyTileIdToShipId(tileId);
		if (string.IsNullOrEmpty(shipId)) return false;
		return SetShip(coords, stackIndex, new ShipSpawnData { ShipId = shipId, Direction = direction, Speed = speed });
	}

	/// <summary>旧接口：追加初设船并兼容 TileId 映射。</summary>
	public void AddUnit(Vector2I coords, int tileId, HexDirection direction, int speed)
	{
		string shipId = LegacyTileIdToShipId(tileId);
		if (string.IsNullOrEmpty(shipId)) shipId = "frigate";
		if (!GenerationPoints.ContainsKey(coords))
			SetGeneration(coords, tileId == 4 ? GenerationSide.Enemy : GenerationSide.Player, tileId);
		AddShip(coords, new ShipSpawnData { ShipId = shipId, Direction = direction, Speed = speed });
	}

	/// <summary>旧接口：移除堆叠单位。</summary>
	public bool RemoveUnit(Vector2I coords, int stackIndex) => RemoveShipAt(coords, stackIndex);

	// ── 画布管理 ──

	/// <summary>列出导出文件夹内的所有地图 JSON（文件名，不含路径）。</summary>
	public string[] ListMaps()
	{
		DirAccess.MakeDirRecursiveAbsolute(DefaultExportFolder);
		DirAccess dir = DirAccess.Open(DefaultExportFolder);
		if (dir == null) return Array.Empty<string>();
		var names = new List<string>();
		foreach (string file in dir.GetFiles())
			if (file.EndsWith(".json")) names.Add(file);
		names.Sort();
		return names.ToArray();
	}

	/// <summary>新建空白画布（内存中清空，未落盘）。</summary>
	public void NewMap(string name, HexOrientation orientation = HexOrientation.EWHorizontal,
		int playerCommand = 5, int enemyCommand = 4, int playerCP = 8, int enemyCP = 8,
		int initiativeValue = 5, string initiativeOwner = "player", int vision = 6, int maxTurns = 18,
		int[] phaseSecondsPerShip = null, int phaseExtraSeconds = 5, bool torpedoPhaseEnabled = false)
	{
		CurrentMapName = string.IsNullOrWhiteSpace(name) ? "untitled" : name.Trim();
		MapType = "day";
		MapOrientation = orientation;
		PlayerCommand = playerCommand;
		EnemyCommand = enemyCommand;
		PlayerInitialCP = playerCP;
		EnemyInitialCP = enemyCP;
		InitiativeValue = initiativeValue;
		InitiativeOwner = initiativeOwner;
		BasicVision = vision;
		MaxTurns = maxTurns;
		PhaseSecondsPerShip = phaseSecondsPerShip != null && phaseSecondsPerShip.Length >= 8
			? (int[])phaseSecondsPerShip.Clone()
			: new[] { 5, 5, 5, 5, 5, 10, 10, 0 };
		PhaseExtraSeconds = phaseExtraSeconds;
		TorpedoPhaseEnabled = torpedoPhaseEnabled;
		_currentJsonPath = $"{DefaultExportFolder}/{CurrentMapName}.json";
		ClearAll();
	}

	/// <summary>设置地图昼夜类型，新建或编辑已有画布时调用。</summary>
	public void SetMapType(string mapType)
		=> MapType = string.IsNullOrEmpty(mapType) ? "day" : mapType;

	/// <summary>设置双方鱼雷命中模式。</summary>
	public void SetTorpedoModes(int playerMode, int enemyMode)
	{
		TorpedoModePlayer = playerMode;
		TorpedoModeEnemy = enemyMode;
	}

	/// <summary>打开已有画布时更新关卡初设，不改变名称、朝向与地形/船表。</summary>
	public void ApplyScenarioSettings(
		int playerCommand, int enemyCommand, int playerCP, int enemyCP,
		int initiativeValue, string initiativeOwner, int vision, int maxTurns,
		int[] phaseSecondsPerShip, int phaseExtraSeconds, bool torpedoPhaseEnabled)
	{
		PlayerCommand = playerCommand;
		EnemyCommand = enemyCommand;
		PlayerInitialCP = playerCP;
		EnemyInitialCP = enemyCP;
		InitiativeValue = initiativeValue;
		InitiativeOwner = string.IsNullOrEmpty(initiativeOwner) ? "player" : initiativeOwner;
		BasicVision = vision;
		MaxTurns = maxTurns;
		PhaseSecondsPerShip = phaseSecondsPerShip != null && phaseSecondsPerShip.Length >= 8
			? (int[])phaseSecondsPerShip.Clone()
			: new[] { 5, 5, 5, 5, 5, 10, 10, 0 };
		PhaseExtraSeconds = phaseExtraSeconds;
		TorpedoPhaseEnabled = torpedoPhaseEnabled;
	}

	/// <summary>保存当前画布到导出文件夹，返回是否成功。</summary>
	public bool SaveCurrentMap()
	{
		if (string.IsNullOrEmpty(CurrentMapName)) return false;
		_currentJsonPath ??= $"{DefaultExportFolder}/{CurrentMapName}.json";
		return SaveToJson(_currentJsonPath);
	}

	/// <summary>从导出文件夹加载画布（fileName 为纯文件名）。</summary>
	public bool LoadMap(string fileName)
	{
		string path = fileName.Contains("://") ? fileName : $"{DefaultExportFolder}/{fileName}";
		if (!FileAccess.FileExists(path)) return false;
		if (!LoadFromJson(path)) return false;
		_currentJsonPath = path;
		return true;
	}

	/// <summary>从 PvP 下载/上传的 JSON 字符串加载地图，供 3D 战场生成。</summary>
	public bool LoadMapFromJson(string json)
	{
		if (string.IsNullOrEmpty(json)) return false;
		string path = "user://maps/pvp_download.json";
		DirAccess.MakeDirRecursiveAbsolute("user://maps");
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null) return false;
		file.StoreString(json);
		_currentJsonPath = path;
		return LoadFromJson(path);
	}

	/// <summary>删除导出文件夹内的画布文件。</summary>
	public void DeleteMap(string fileName)
	{
		string path = $"{DefaultExportFolder}/{fileName}";
		if (FileAccess.FileExists(path)) DirAccess.RemoveAbsolute(path);
	}

	// ── JSON 持久化 ──

	private class MapSaveData
	{
		public string Name { get; set; } = "untitled";
		public string MapType { get; set; } = "day";
		public string Orientation { get; set; } = "ew";
		public int PlayerCommand { get; set; } = 5;
		public int EnemyCommand { get; set; } = 4;
		public int PlayerInitialCP { get; set; } = 8;
		public int EnemyInitialCP { get; set; } = 8;
		public int InitiativeValue { get; set; } = 5;
		public string InitiativeOwner { get; set; } = "player";
		public int BasicVision { get; set; } = 6;
		public int TorpedoModePlayer { get; set; } = 7;
		public int TorpedoModeEnemy { get; set; } = 4;
		public int MaxTurns { get; set; } = 18;
		public bool TorpedoPhaseEnabled { get; set; }
		public int[] PhaseSecondsPerShip { get; set; } = { 5, 5, 5, 5, 5, 10, 10, 0 };
		public int PhaseExtraSeconds { get; set; } = 5;
		public int Version { get; set; } = 3;
		public Dictionary<string, int> Terrain { get; set; } = new();
		public Dictionary<string, GenerationPointData> Generation { get; set; } = new();
		public Dictionary<string, int> Special { get; set; } = new();
		public Dictionary<string, List<ShipSpawnData>> Ships { get; set; } = new();
	}

	/// <summary>坐标转 JSON key 字符串。</summary>
	private static string SerializeKey(Vector2I v) => $"{v.X},{v.Y}";
	private static Vector2I DeserializeKey(string s)
	{
		var parts = s.Split(',');
		return new Vector2I(int.Parse(parts[0]), int.Parse(parts[1]));
	}

	private static int[] ReadIntArray(JsonElement element)
	{
		var result = new List<int>();
		foreach (JsonElement item in element.EnumerateArray())
			result.Add(item.GetInt32());
		return result.ToArray();
	}

	/// <summary>把内存表写入 JSON 文件。</summary>
	private bool SaveToJson(string path)
	{
		var data = new MapSaveData
		{
			Name = CurrentMapName,
			Version = 3,
			MapType = MapType,
			Orientation = MapOrientation == HexOrientation.NSVertical ? "ns" : "ew",
			PlayerCommand = PlayerCommand,
			EnemyCommand = EnemyCommand,
			PlayerInitialCP = PlayerInitialCP,
			EnemyInitialCP = EnemyInitialCP,
			InitiativeValue = InitiativeValue,
			InitiativeOwner = InitiativeOwner,
			BasicVision = BasicVision,
			TorpedoModePlayer = TorpedoModePlayer,
			TorpedoModeEnemy = TorpedoModeEnemy,
			MaxTurns = MaxTurns,
			TorpedoPhaseEnabled = TorpedoPhaseEnabled,
			PhaseSecondsPerShip = (int[])PhaseSecondsPerShip.Clone(),
			PhaseExtraSeconds = PhaseExtraSeconds
		};
		foreach (var kv in TerrainSources) data.Terrain[SerializeKey(kv.Key)] = kv.Value;
		foreach (var kv in GenerationPoints) data.Generation[SerializeKey(kv.Key)] = kv.Value;
		foreach (var kv in SpecialTiles) data.Special[SerializeKey(kv.Key)] = kv.Value;
		foreach (var kv in ShipSpawns) data.Ships[SerializeKey(kv.Key)] = kv.Value;

		var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
		DirAccess.MakeDirRecursiveAbsolute(DefaultExportFolder);
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		if (file == null)
		{
			GD.PrintErr($"SaveToJson: 无法写入 {path}");
			return false;
		}
		file.StoreString(json);
		GD.Print($"地图已保存: {path}");
		return true;
	}

	/// <summary>从 JSON 文件恢复内存表并兼容旧版本。</summary>
	private bool LoadFromJson(string path)
	{
		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null) return false;
		using var document = JsonDocument.Parse(file.GetAsText());
		JsonElement root = document.RootElement;
		ClearAll();

		// 兼容 v2：Terrain 值为字符串、Units 为 UnitSpawnData 数组
		if (root.TryGetProperty("Version", out JsonElement versionElement) && versionElement.GetInt32() < 3)
			return LoadLegacyV2(root);

		if (root.TryGetProperty("Terrain", out JsonElement terrain))
		{
			foreach (JsonProperty kv in terrain.EnumerateObject())
			{
				if (kv.Value.ValueKind == JsonValueKind.Number)
					TerrainSources[DeserializeKey(kv.Name)] = kv.Value.GetInt32();
				else if (kv.Value.ValueKind == JsonValueKind.String)
					TerrainSources[DeserializeKey(kv.Name)] = TerrainToSourceId(kv.Value.GetString());
			}
		}

		if (root.TryGetProperty("Generation", out JsonElement generation))
		{
			foreach (JsonProperty kv in generation.EnumerateObject())
			{
				var gen = new GenerationPointData();
				if (kv.Value.ValueKind == JsonValueKind.Number)
				{
					gen.SourceId = kv.Value.GetInt32();
					gen.Side = gen.SourceId == 4 ? GenerationSide.Enemy : GenerationSide.Player;
				}
				else if (kv.Value.ValueKind == JsonValueKind.Object)
				{
					if (kv.Value.TryGetProperty("SourceId", out JsonElement sid)) gen.SourceId = sid.GetInt32();
					if (kv.Value.TryGetProperty("Side", out JsonElement side))
						gen.Side = side.GetInt32() == 1 ? GenerationSide.Enemy : GenerationSide.Player;
				}
				GenerationPoints[DeserializeKey(kv.Name)] = gen;
			}
		}

		if (root.TryGetProperty("Special", out JsonElement special))
		{
			foreach (JsonProperty kv in special.EnumerateObject())
				if (kv.Value.ValueKind == JsonValueKind.Number)
					SpecialTiles[DeserializeKey(kv.Name)] = kv.Value.GetInt32();
		}

		if (root.TryGetProperty("Ships", out JsonElement ships))
		{
			foreach (JsonProperty kv in ships.EnumerateObject())
			{
				var list = new List<ShipSpawnData>();
				foreach (JsonElement item in kv.Value.EnumerateArray())
				{
					if (item.ValueKind != JsonValueKind.Object) continue;
					var ship = new ShipSpawnData();
					if (item.TryGetProperty("ShipId", out JsonElement id)) ship.ShipId = id.GetString() ?? "";
					if (item.TryGetProperty("Direction", out JsonElement dir))
					{
						ship.Direction = dir.ValueKind == JsonValueKind.Number
							? (HexDirection)dir.GetInt32()
							: Enum.TryParse(dir.GetString(), true, out HexDirection parsed) ? parsed : HexDirection.N;
					}
					if (item.TryGetProperty("Speed", out JsonElement speed)) ship.Speed = speed.GetInt32();
					if (!string.IsNullOrEmpty(ship.ShipId)) list.Add(ship);
				}
				if (list.Count > 0) ShipSpawns[DeserializeKey(kv.Name)] = list;
			}
		}

		CurrentMapName = root.TryGetProperty("Name", out JsonElement nameElement) ? nameElement.GetString() : "untitled";
		MapType = root.TryGetProperty("MapType", out JsonElement mapTypeElement) ? mapTypeElement.GetString() ?? "day" : "day";
		MapOrientation = root.TryGetProperty("Orientation", out JsonElement orientationElement)
			&& orientationElement.GetString() == "ns"
			? HexOrientation.NSVertical
			: HexOrientation.EWHorizontal;
		PlayerCommand = root.TryGetProperty("PlayerCommand", out JsonElement playerCommand) ? playerCommand.GetInt32() : 5;
		EnemyCommand = root.TryGetProperty("EnemyCommand", out JsonElement enemyCommand) ? enemyCommand.GetInt32() : 4;
		PlayerInitialCP = root.TryGetProperty("PlayerInitialCP", out JsonElement playerCp) ? playerCp.GetInt32() : 8;
		EnemyInitialCP = root.TryGetProperty("EnemyInitialCP", out JsonElement enemyCp) ? enemyCp.GetInt32() : 8;
		InitiativeValue = root.TryGetProperty("InitiativeValue", out JsonElement initiative) ? initiative.GetInt32() : 5;
		InitiativeOwner = root.TryGetProperty("InitiativeOwner", out JsonElement owner)
			? owner.GetString() ?? "player"
			: "player";
		BasicVision = root.TryGetProperty("BasicVision", out JsonElement vision) ? vision.GetInt32() : 6;
		TorpedoModePlayer = root.TryGetProperty("TorpedoModePlayer", out JsonElement tp) ? tp.GetInt32() : 7;
		TorpedoModeEnemy = root.TryGetProperty("TorpedoModeEnemy", out JsonElement te) ? te.GetInt32() : 4;
		MaxTurns = root.TryGetProperty("MaxTurns", out JsonElement maxTurns) ? maxTurns.GetInt32() : 18;
		TorpedoPhaseEnabled = root.TryGetProperty("TorpedoPhaseEnabled", out JsonElement torpedoEnabled)
			&& torpedoEnabled.ValueKind == JsonValueKind.True;
		PhaseSecondsPerShip = root.TryGetProperty("PhaseSecondsPerShip", out JsonElement phaseSeconds)
			? ReadIntArray(phaseSeconds)
			: new[] { 5, 5, 5, 5, 5, 10, 10, 0 };
		PhaseExtraSeconds = root.TryGetProperty("PhaseExtraSeconds", out JsonElement phaseExtra)
			? phaseExtra.GetInt32()
			: 5;
		GD.Print($"地图已加载: {path} (地形 {TerrainSources.Count}, 生成点 {GenerationPoints.Count}, 船 {ShipSpawns.Count}, 类型 {MapType}, 朝向 {MapOrientation})");
		return true;
	}

	/// <summary>兼容 v2 JSON（Terrain 字符串 + Units 数组）。</summary>
	private bool LoadLegacyV2(JsonElement root)
	{
		if (root.TryGetProperty("Terrain", out JsonElement terrain))
		{
			foreach (JsonProperty kv in terrain.EnumerateObject())
			{
				string type = kv.Value.GetString() ?? "ocean";
				TerrainSources[DeserializeKey(kv.Name)] = TerrainToSourceId(type);
			}
		}

		if (root.TryGetProperty("Units", out JsonElement units))
		{
			foreach (JsonProperty kv in units.EnumerateObject())
			{
				Vector2I hex = DeserializeKey(kv.Name);
				foreach (JsonElement item in kv.Value.EnumerateArray())
				{
					if (item.ValueKind != JsonValueKind.Object) continue;
					int tileId = item.TryGetProperty("TileId", out JsonElement tid) ? tid.GetInt32() : 0;
					int speed = item.TryGetProperty("Speed", out JsonElement spd) ? spd.GetInt32() : 0;
					HexDirection direction = HexDirection.N;
					if (item.TryGetProperty("Direction", out JsonElement dir))
						direction = dir.ValueKind == JsonValueKind.Number ? (HexDirection)dir.GetInt32() : HexDirection.N;
					string shipId = LegacyTileIdToShipId(tileId);
					if (string.IsNullOrEmpty(shipId)) continue;
					if (!GenerationPoints.ContainsKey(hex))
						GenerationPoints[hex] = new GenerationPointData { SourceId = tileId, Side = tileId == 4 ? GenerationSide.Enemy : GenerationSide.Player };
					AddShip(hex, new ShipSpawnData { ShipId = shipId, Direction = direction, Speed = speed });
				}
			}
		}

		CurrentMapName = root.TryGetProperty("Name", out JsonElement nameElement) ? nameElement.GetString() : "untitled";
		MapType = "day";
		MapOrientation = HexOrientation.EWHorizontal;
		return true;
	}

	/// <summary>清空全部运行表与静态战场账本。</summary>
	private void ClearAll()
	{
		TerrainSources.Clear();
		GenerationPoints.Clear();
		SpecialTiles.Clear();
		ShipSpawns.Clear();
		GridTopology.Clear();
		BattlefieldUnits.Clear();
	}

	// ── 类型映射 ──

	private static string SourceIdToTerrain(int sourceId) => sourceId == 1 ? "island" : "ocean";
	private static int TerrainToSourceId(string type) => type == "island" ? 1 : 2;

	/// <summary>旧 v2 地图的 TileId → ShipId 兼容映射。</summary>
	public static string LegacyTileIdToShipId(int tileId) => tileId switch
	{
		4 => "frigate",
		6 => "dreadnought",
		_ => ""
	};
}

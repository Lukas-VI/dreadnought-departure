using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 单位生成器（3D）。
/// 接收 LevelDataManager 派生的 UnitData（含 ShipId/方向/初速/阵营/堆叠信息），
/// 优先通过 ShipCatalog 按 ShipId 找预制体，找不到再退回 ShipRegistry（旧 TileId 索引）。
/// 生成后自动注册到 LevelDataManager.BattlefieldUnits。
/// </summary>
public partial class UnitSpawner : Node3D
{
	[Export] public PackedScene DefaultShipPrefab;
	[Export] public ShipList ShipRegistry;

	/// <summary>根据初设数据生成所有单位。堆叠单位沿 y 轴偏移 0.15 个单位。</summary>
	public void SpawnUnits(Dictionary<Vector2I, List<UnitSpawnData>> unitData)
	{
		foreach (var kvp in unitData)
		{
			Vector2I coords = kvp.Key;
			for (int i = 0; i < kvp.Value.Count; i++)
			{
				UnitSpawnData spawnData = kvp.Value[i];
				PackedScene prefab = ResolvePrefab(spawnData);
				if (prefab == null) continue;

				ShipComponent ship = prefab.Instantiate<ShipComponent>();
				AddChild(ship);
				ship.HexCoords = coords;
				ship.TileSourceId = spawnData.TileId;
				ship.BattleSide = spawnData.Side;

				Vector3 pos = HexToWorld(coords.X, coords.Y);
				ship.Position = new Vector3(pos.X, 0.3f + i * 0.15f, pos.Z);
				ship.ApplyInitialState(spawnData.Direction, spawnData.Speed);
				LevelDataManager.BattlefieldUnits[coords] = ship; // 注册到全局战场表
			}
		}
	}

	/// <summary>按 ShipId（ShipCatalog）→ TileId（ShipRegistry）→ 默认预制体的顺序解析。</summary>
	private PackedScene ResolvePrefab(UnitSpawnData data)
	{
		if (!string.IsNullOrEmpty(data.ShipId))
		{
			PackedScene catalogScene = ShipCatalog.GetScene(data.ShipId);
			if (catalogScene != null) return catalogScene;
		}
		if (ShipRegistry?.Ships != null && ShipRegistry.Ships.TryGetValue(data.TileId, out var registryScene))
			return registryScene;
		return DefaultShipPrefab;
	}

	/// <summary>轴向六角格坐标 (Q,R) → 3D 世界坐标（y=0 海平面）。</summary>
	public Vector3 HexToWorld(int q, int r)
	{
		float x = GameConfig.HexRadius * 1.5f * q;
		float z = GameConfig.HexRadius * Mathf.Sqrt(3.0f) * (r + q / 2.0f);
		return new Vector3(x, 0, z);
	}
}

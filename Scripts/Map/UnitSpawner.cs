using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 单位生成器（3D）。
/// 接收 LevelDataManager 派生的 UnitData（含 ShipId/方向/初速/阵营/堆叠信息），
/// 通过 ShipCatalog 按 ShipId 从运行时数据目录找预制体，找不到再退回默认预制体。
/// 生成后自动注册到 LevelDataManager.BattlefieldUnits。
/// </summary>
public partial class UnitSpawner : Node3D
{
	[Export] public PackedScene DefaultShipPrefab;

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
				Vector2I forwardOff = HexDirectionUtility.Offset(spawnData.Direction);
				Vector3 forward = HexToWorld(forwardOff.X, forwardOff.Y) - HexToWorld(0, 0);
				forward.Y = 0f;
				Vector3 lateral = new Vector3(forward.Z, 0f, -forward.X);
				if (lateral.LengthSquared() > 0f) lateral = lateral.Normalized();
				ship.Position = new Vector3(pos.X, ShipComponent.StackBaseY, pos.Z);
				ship.ApplyStackOffset(i, kvp.Value.Count, pos, lateral);
				ship.ApplyInitialState(spawnData.Direction, spawnData.Speed);
				LevelDataManager.BattlefieldUnits[coords] = ship; // 注册到全局战场表
			}
		}
	}

	/// <summary>按 ShipId（ShipCatalog）→ 默认预制体的顺序解析。</summary>
	private PackedScene ResolvePrefab(UnitSpawnData data)
	{
		if (!string.IsNullOrEmpty(data.ShipId))
		{
			PackedScene catalogScene = ShipCatalog.GetScene(data.ShipId);
			if (catalogScene != null) return catalogScene;
		}
		return DefaultShipPrefab;
	}

	/// <summary>轴向六角格坐标 (Q,R) → 3D 世界坐标（y=0 海平面）。</summary>
	public Vector3 HexToWorld(int q, int r)
	{
		HexOrientation orientation = GetNodeOrNull<LevelDataManager>("../LevelDataManager")?.MapOrientation
			?? HexOrientation.EWHorizontal;
		Vector2 local = HexMath.HexToLocal(orientation, new Vector2I(q, r), GameConfig.HexRadius);
		float x = local.X;
		float z = local.Y;
		return new Vector3(x, 0, z);
	}
}

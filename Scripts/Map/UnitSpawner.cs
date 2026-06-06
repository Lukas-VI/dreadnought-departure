using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class UnitSpawner : Node3D
{

	[Export] public PackedScene DefaultShipPrefab;
	[Export] public ShipList ShipRegistry;

	public void SpawnUnits(Dictionary<Vector2I, List<int>> unitData)
	{
		foreach (var kvp in unitData)
		{
			Vector2I coords = kvp.Key;
			for (int i = 0; i < kvp.Value.Count; i++)
			{
				int tileId = kvp.Value[i];
				PackedScene prefab = DefaultShipPrefab;
				ShipRegistry?.Ships?.TryGetValue(tileId, out prefab);
				if (prefab == null) continue;

				ShipComponent ship = prefab.Instantiate<ShipComponent>();
				AddChild(ship);
				ship.HexCoords = coords;
				ship.TileSourceId = tileId;

				Vector3 pos = HexToWorld(coords.X, coords.Y);
				ship.Position = new Vector3(pos.X, 0.3f + i * 0.15f, pos.Z);
			}
		}
	}

	public Vector3 HexToWorld(int q, int r)
	{
		float x = GameConfig.HexRadius * 1.5f * q;
		float z = GameConfig.HexRadius * Mathf.Sqrt(3.0f) * (r + q / 2.0f);
		return new Vector3(x, 0, z);
	}
}

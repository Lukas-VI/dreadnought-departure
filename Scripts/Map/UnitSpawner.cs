using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class UnitSpawner : Node3D
{
	[Export] public float HexRadius = 1.0f;
	[Export] public PackedScene DefaultShipPrefab;
	[Export] public Godot.Collections.Dictionary<int, PackedScene> ShipPrefabs;

	public void SpawnUnits(Dictionary<Vector2I, int> unitData)
	{
		foreach (var kvp in unitData)
		{
			Vector2I coords = kvp.Key;
			int shipTileId = kvp.Value;

			PackedScene shipPrefab = (ShipPrefabs?.TryGetValue(shipTileId, out var sp) == true ? sp : null) ?? DefaultShipPrefab;
			if (shipPrefab == null) continue;

			ShipComponent shipInstance = shipPrefab.Instantiate<ShipComponent>();
			AddChild(shipInstance);
			shipInstance.HexCoords = coords;

			Vector3 pos = HexToWorld(coords.X, coords.Y);
			shipInstance.Position = new Vector3(pos.X, 0.3f, pos.Z);
		}
	}

	public Vector3 HexToWorld(int q, int r)
	{
		float x = HexRadius * 1.5f * q;
		float z = HexRadius * Mathf.Sqrt(3.0f) * (r + q / 2.0f);
		return new Vector3(x, 0, z);
	}
}

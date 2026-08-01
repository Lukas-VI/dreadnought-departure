using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DreadnoughtDeparture.Core;

public partial class MapGenerator : Node3D
{
	// 六角格半径，当前 mesh 是半径 2 的正六边形
	/// <summary>NS 竖直向地块各自额外旋转 30°。</summary>
	private const float NSAdditionalRotationDegrees = 30f;

	// 🔧 兜底：字典没配时用这个
	[Export] public PackedScene DefaultTilePrefab;

	// 地形 prefab：key = "ocean"/"island" 等，value = hex_tile_xxx.tscn
	[Export] public Godot.Collections.Dictionary<string, PackedScene> TilePrefabs;

	// 后面高亮和战斗需要精准找格子，我们把生成的 3D 格子 Mesh 存起来暴露出去
	public Dictionary<Vector2I, MeshInstance3D> SpawnedTileMeshes { get; private set; } = new();

	private Node3D _mapContainer;
	private HexOrientation _orientation = HexOrientation.EWHorizontal;

	public override void _Ready()
	{
		_mapContainer = GetNode<Node3D>("MapContainer");
	}

	// 生成地图的核心函数，接受从 LevelDataManager 抓取来的纯数据字典
	public void BuildMap(Dictionary<Vector2I, string> terrainData)
	{
		_orientation = GetNodeOrNull<LevelDataManager>("../LevelDataManager")?.MapOrientation
			?? HexOrientation.EWHorizontal;

		// 1. 生成地形
		foreach (var kvp in terrainData)
		{
			Vector2I coords = kvp.Key;
			string type = kvp.Value;

			PackedScene tilePrefab = TilePrefabs.TryGetValue(type, out var p) ? p : DefaultTilePrefab;
			if (tilePrefab == null) continue;

			Node3D tileInstance = tilePrefab.Instantiate<Node3D>();
			_mapContainer.AddChild(tileInstance);

			Vector3 targetPos = HexToWorld(coords.X, coords.Y);

			if (type == "island")
			{
				tileInstance.Position = new Vector3(targetPos.X, 0.2f, targetPos.Z);
				tileInstance.Scale = new Vector3(1.0f, 2.0f, 1.0f);
			}
			else
			{
				tileInstance.Position = targetPos;
			}
			// NS 地图把六角棱柱旋转 30°，尖角朝上下，与编辑器朝向一致。
			if (_orientation == HexOrientation.NSVertical)
				tileInstance.RotateY(Mathf.DegToRad(NSAdditionalRotationDegrees));

			// 记录生成的 Mesh 引用，留给 GridOverlayController 变色用
			var meshInst = tileInstance.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
			if (meshInst != null) SpawnedTileMeshes[coords] = meshInst;
		}

	}

	public Vector3 HexToWorld(int q, int r)
	{
		Vector2 local = HexMath.HexToLocal(_orientation, new Vector2I(q, r), GameConfig.HexRadius);
		float x = local.X;
		float z = local.Y;
		return new Vector3(x, 0f, z);
	}
}

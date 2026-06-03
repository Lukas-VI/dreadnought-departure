using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DreadnoughtDeparture.Core;



public partial class MapGenerator : Node3D
{
    // 导入地块prefab，并且设置六角格半径，当前mesh是边长/半径为1的正六边形，所以半径也就是1
    [Export] public PackedScene TilePrefab; 
    [Export] public float HexRadius = 1.0f;

    // 引擎编辑器场景资源：我们会在内存中实例化它来读取数据，但它永远不会被 AddChild 到舞台上，所以绝对不会渲染出来
    [Export] public PackedScene MapEditorScene; 

    // 注册两种材质，分别对应海洋和陆地
    [Export] public StandardMaterial3D OceanMaterial;
    [Export] public StandardMaterial3D IslandMaterial;

    private Node3D _mapContainer;
    private Dictionary<string, string> _mapData = new();

    public override void _Ready()
    {
        _mapContainer = GetNode<Node3D>("MapContainer");

        // 1. 核心解耦：在内存中悄悄实例化 2D 场景，它不会被 AddChild 到舞台上，所以绝不会渲染出来
        Node2D editorInstance = MapEditorScene.Instantiate<Node2D>();
        
        // 2. 从这个内存实例中精准掐住 2D 瓦片图层
        TileMapLayer tileMap2D = editorInstance.GetNode<TileMapLayer>("BaseTileMap");

        if (tileMap2D != null)
        {
            // 3. 像之前一样，提取数据转成 JSON
            string jsonOutput = Export2DMapToJson(tileMap2D);
            GD.Print("--- 成功跨场景导出地图 JSON ---");
            
            // 4. 解析并生成 3D
            LoadMapFromJson(jsonOutput);
            Generate3DMap();
        }

        // 5. 卸载内存：数据拿到了，2D 场景可以寿终正寝了，彻底释放内存
        editorInstance.QueueFree();

    }

    // 注意：这里传入了我们动态抓取的 tileMap2D 实例
    private string Export2DMapToJson(TileMapLayer tileMap2D)
    {
        var exportDict = new Dictionary<string, string>();
        var usedCells = tileMap2D.GetUsedCells();

        foreach (Vector2I cellCoords in usedCells)
        {
            // === 🛠️ 核心修正：从 Godot 2D 偏移坐标 转换为 标准轴向坐标 ===
            // 在 Godot 4.6 的 Flat-Top Stacked 布局下：
            // r 直接对应 2D 的 Y
            int r = cellCoords.Y;
            
            // q 需要根据行数进行数学数学补偿，消除每行的阶梯错位
            // 公式：q = X - (Y >> 1)  [即 Y 除以 2 并向下取整]
            int q = cellCoords.X - (cellCoords.Y >> 1); 
            
            string key = $"{q},{r}";

            // 获取瓦片类型
            int tileId = tileMap2D.GetCellSourceId(cellCoords);
            string tileType = tileId == 1 ? "island" : "ocean"; 

            exportDict[key] = tileType;
        }

        return JsonSerializer.Serialize(exportDict, new JsonSerializerOptions { WriteIndented = true });
    }

    private void LoadMapFromJson(string jsonString)
    {
        _mapData = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
    }

    private void Generate3DMap()
    {
        if (TilePrefab == null) return;

        foreach (var kvp in _mapData)
        {
            string[] coords = kvp.Key.Split(',');
            int q = int.Parse(coords[0]);
            int r = int.Parse(coords[1]);
            string type = kvp.Value;

            Node3D tileInstance = TilePrefab.Instantiate<Node3D>();
            _mapContainer.AddChild(tileInstance);

            // 正向投影
            float x = HexRadius * 1.5f * q;
            float z = HexRadius * Mathf.Sqrt(3.0f) * (r + q / 2.0f);
            float y = (type == "island") ? 0.4f : 0.0f; 

            tileInstance.GlobalPosition = new Vector3(x, y, z);
            tileInstance.RotationDegrees = new Vector3(0f, -30f, 0f);

            // 抓取子节点中的 MeshInstance3D
            var meshInst = tileInstance.GetNode<MeshInstance3D>("MeshInstance3D");

            if (type == "island")
            {
                tileInstance.Position = new Vector3(x, 0.2f, z); // 陆地稍微拔高
                tileInstance.Scale = new Vector3(1.0f, 2.0f, 1.0f);
                if (meshInst != null) meshInst.MaterialOverride = IslandMaterial; // 注入陆地色块
            }
            else
            {
                tileInstance.Position = new Vector3(x, 0.0f, z);
                tileInstance.Scale = Vector3.One;
                if (meshInst != null) meshInst.MaterialOverride = OceanMaterial; // 注入海洋色块
            }
        }
    }
}
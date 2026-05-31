using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class MapGenerator : Node3D
{
    // 把你刚刚做好的 hex_tile_3d.tscn 拖进这个变量里
    [Export] public PackedScene TilePrefab; 
    [Export] public float HexRadius = 1.0f;

    private Node3D _mapContainer;

    public override void _Ready()
    {
        // 抓取场景树里的容器节点
        _mapContainer = GetNode<Node3D>("MapContainer");

        // 临时伪造一份 2D 坐标数据（未来这里会通过 JSON 读取）
        // 模拟一个 4x4 的平顶六角格矩阵
        for (int q = -2; q <= 2; q++)
        {
            for (int r = -2; r <= 2; r++)
            {
                // 六角格边界修剪数学
                if (Math.Abs(-q - r) <= 2)
                {
                    SpawnTile(q, r);
                }
            }
        }
    }

    private void SpawnTile(int q, int r)
    {
        if (TilePrefab == null) return;

        // 1. 实例化 3D 方块
        Node3D tileInstance = TilePrefab.Instantiate<Node3D>();
        _mapContainer.AddChild(tileInstance);

        // 2. 利用我们之前的平顶六角格公式计算物理位置
        float x = HexRadius * 1.5f * q;
        float z = HexRadius * Mathf.Sqrt(3.0f) * (r + q / 2.0f);

        // 3. 啪的一下摆过去
        tileInstance.GlobalPosition = new Vector3(x, 0.0f, z);
    }
}
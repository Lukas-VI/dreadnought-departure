using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 舰船索引表资源（.tres 文件）。
/// 以 TileId 为键、PackedScene（tscn 预制体）为值的字典。
/// UnitSpawner 通过此表在运行时根据 TileId 实例化对应的舰船预制体。
/// </summary>
[GlobalClass]
public partial class ShipList : Resource
{
	[Export] public Godot.Collections.Dictionary<int, PackedScene> Ships;
}

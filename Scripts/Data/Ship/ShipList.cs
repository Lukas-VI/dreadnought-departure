using Godot;

namespace DreadnoughtDeparture.Core;

[GlobalClass]
public partial class ShipList : Resource
{
	[Export] public Godot.Collections.Dictionary<int, PackedScene> Ships;
}

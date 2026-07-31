using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// Detects clicks on the 2D TileMapLayer in the editor scene
/// and emits HexClicked via EventBus (same path as 3D BattleInputDetector).
/// </summary>
public partial class EditorInputDetector : Node
{
	[Export] public NodePath TileMapLayerPath = "../MapEditor/TerrainLayer";

	private TileMapLayer _tileLayer;
	private EventBus _bus;

	public override void _Ready()
	{
		_tileLayer = GetNodeOrNull<TileMapLayer>(TileMapLayerPath);
		_bus = GetNodeOrNull<EventBus>("../EventBus");
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb || mb.ButtonIndex != MouseButton.Left || !mb.Pressed)
			return;
		if (_tileLayer == null) return;

		Vector2 localPos = _tileLayer.GetLocalMousePosition();
		Vector2I cell = _tileLayer.LocalToMap(localPos);

		// GetCellSourceId returns -1 for empty cells, >=0 for placed tiles
		if (_tileLayer.GetCellSourceId(cell) == -1) return;

		// Convert cell grid coords to axial (offset-even-r -> axial q,r)
		int r = cell.Y;
		int q = cell.X - (cell.Y >> 1);
		Vector2I axial = new(q, r);

		_bus?.EmitSignal("HexClicked", axial);
	}
}

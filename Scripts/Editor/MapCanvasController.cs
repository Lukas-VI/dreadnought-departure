using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>编辑器工具类型。</summary>
public enum MapEditorTool
{
	Select,
	Pencil,
	Fill,
	Eraser
}

/// <summary>
/// 程序化六角格编辑器画布。
/// 地块直接由 _Draw 绘制：填充色采样自 3D 地块材质，边框用代码画六边形；
/// 鼠标操作直接读写 LevelDataManager，不再依赖 TileMapLayer / tileset PNG。
/// </summary>
public partial class MapCanvasController : Node2D
{
	private LevelDataManager _data;
	private ShipNameOverlay _nameOverlay;
	private EditorUIController _ui;

	private StandardMaterial3D _oceanMaterial;
	private StandardMaterial3D _islandMaterial;

	public MapEditorTool CurrentTool { get; private set; } = MapEditorTool.Pencil;
	public string ActiveCategory { get; private set; } = "Terrain";
	public int ActiveSourceId { get; private set; } = 2;
	public bool ShowGrid { get; private set; }

	public HashSet<Vector2I> SelectedCells { get; } = new();

	private Vector2I _pressHex;
	private Vector2I _lastHex;
	private Vector2 _rightPressPos;
	private float _rightDragDistance;
	private bool _leftHeld;
	private bool _rightHeld;

	private HexOrientation Orientation => _data?.MapOrientation ?? HexOrientation.EWHorizontal;
	private float Radius => HexMath.EditorHexRadius;

	private Color OceanColor => _oceanMaterial?.AlbedoColor ?? new Color(0.13f, 0.768f, 1f, 1f);
	private Color IslandColor => _islandMaterial?.AlbedoColor ?? new Color(0.22f, 0.71f, 0.1f, 1f);

	public override void _Ready()
	{
		_data = GetNodeOrNull<LevelDataManager>("../LevelDataManager");
		_nameOverlay = GetNodeOrNull<ShipNameOverlay>("ShipNameOverlay");
		_ui = GetNodeOrNull<EditorUIController>("../CanvasLayer/EditorUI");
		_oceanMaterial = ResourceLoader.Load<StandardMaterial3D>("res://Data/Materials/terrain/mat_ocean.tres");
		_islandMaterial = ResourceLoader.Load<StandardMaterial3D>("res://Data/Materials/terrain/mat_island.tres");
	}

	public void SetTool(MapEditorTool tool)
	{
		CurrentTool = tool;
		if (tool != MapEditorTool.Select) ClearSelection();
	}

	public void SetPalette(string category, int sourceId)
	{
		ActiveCategory = category;
		ActiveSourceId = sourceId;
	}

	public void SetGridVisible(bool visible)
	{
		ShowGrid = visible;
		QueueRedraw();
	}

	/// <summary>数据已由 LevelDataManager 持有，这里只清空选中并重绘。</summary>
	public void ApplyDataToLayers()
	{
		ClearSelection();
		RefreshOverlay();
	}

	public void ClearCanvas()
	{
		ClearSelection();
		RefreshOverlay();
	}

	public void RefreshOverlay()
	{
		_nameOverlay?.RedrawAll();
		QueueRedraw();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb) HandleMouseButton(mb);
		else if (@event is InputEventMouseMotion mm) HandleMouseMotion(mm);
		else if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Delete)
			EraseSelection();
	}

	private void HandleMouseButton(InputEventMouseButton mb)
	{
		if (mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_leftHeld = true;
				_pressHex = MouseHex();
				_lastHex = _pressHex;
				if (CurrentTool == MapEditorTool.Select)
				{
					SelectedCells.Clear();
					SelectedCells.Add(_pressHex);
					QueueRedraw();
				}
				else if (CurrentTool == MapEditorTool.Fill)
				{
					// 油漆桶在释放时执行，避免拖拽误触
				}
				else
				{
					ApplyStroke(_pressHex);
				}
			}
			else
			{
				_leftHeld = false;
				if (CurrentTool == MapEditorTool.Fill) ApplyStroke(_pressHex);
				if (CurrentTool == MapEditorTool.Select) CommitBoxSelection();
			}
		}
		else if (mb.ButtonIndex == MouseButton.Right)
		{
			if (mb.Pressed)
			{
				_rightHeld = true;
				_rightPressPos = mb.Position;
				_rightDragDistance = 0f;
			}
			else if (_rightHeld && _rightDragDistance < 6f)
			{
				_rightHeld = false;
				Vector2I hex = MouseHex();
				if (_data.GetGenerationAt(hex) != null)
				{
					SelectedCells.Clear();
					SelectedCells.Add(hex);
					QueueRedraw();
					_ui?.OpenGenerationInspector(hex);
				}
			}
			else
			{
				_rightHeld = false;
			}
		}
	}

	private void HandleMouseMotion(InputEventMouseMotion mm)
	{
		if (_rightHeld)
			_rightDragDistance += mm.Relative.Length();

		if (!_leftHeld) return;

		Vector2I hex = MouseHex();
		if (hex == _lastHex) return;

		switch (CurrentTool)
		{
			case MapEditorTool.Select:
				UpdateBoxSelection(hex);
				break;
			case MapEditorTool.Pencil:
			case MapEditorTool.Eraser:
				StrokeLine(_lastHex, hex);
				break;
		}
		_lastHex = hex;
	}

	private void ApplyStroke(Vector2I hex)
	{
		switch (CurrentTool)
		{
			case MapEditorTool.Pencil:
				PaintHex(hex);
				break;
			case MapEditorTool.Eraser:
				EraseHex(hex);
				break;
			case MapEditorTool.Fill:
				FloodFill(hex);
				break;
		}
		RefreshOverlay();
	}

	private void StrokeLine(Vector2I from, Vector2I to)
	{
		Vector2I delta = to - from;
		int steps = Mathf.Max(Mathf.Abs(delta.X), Mathf.Abs(delta.Y));
		for (int i = 0; i <= steps; i++)
		{
			Vector2I p = from + delta * i / Mathf.Max(1, steps);
			if (CurrentTool == MapEditorTool.Eraser) EraseHex(p);
			else PaintHex(p);
		}
		RefreshOverlay();
	}

	private void PaintHex(Vector2I hex)
	{
		switch (ActiveCategory)
		{
			case "Generation":
				_data.SetGeneration(hex, ActiveSourceId == 4 ? GenerationSide.Enemy : GenerationSide.Player, ActiveSourceId);
				break;
			case "Special":
				_data.SetSpecial(hex, ActiveSourceId);
				break;
			default:
				_data.SetTerrain(hex, ActiveSourceId);
				break;
		}
	}

	private void EraseHex(Vector2I hex)
	{
		switch (ActiveCategory)
		{
			case "Generation": _data.EraseGeneration(hex); break;
			case "Special": _data.EraseSpecial(hex); break;
			default: _data.EraseTerrain(hex); break;
		}
	}

	private void FloodFill(Vector2I start)
	{
		int replacement = ActiveSourceId;
		int target = GetSourceId(start);
		if (target == replacement) return;
		if (target < 0)
		{
			PaintHex(start);
			return;
		}

		var queue = new Queue<Vector2I>();
		var visited = new HashSet<Vector2I>();
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			Vector2I hex = queue.Dequeue();
			if (!visited.Add(hex)) continue;
			if (GetSourceId(hex) != target) continue;
			PaintHex(hex);
			foreach (HexDirection dir in System.Enum.GetValues<HexDirection>())
				queue.Enqueue(hex + HexDirectionUtility.Offset(dir));
		}
	}

	private int GetSourceId(Vector2I hex)
	{
		return ActiveCategory switch
		{
			"Generation" => _data.GetGenerationAt(hex)?.SourceId ?? -1,
			"Special" => _data.GetSpecialSource(hex),
			_ => _data.GetTerrainSource(hex)
		};
	}

	public void EraseSelection()
	{
		if (SelectedCells.Count == 0) return;
		foreach (Vector2I hex in SelectedCells) EraseHex(hex);
		SelectedCells.Clear();
		RefreshOverlay();
	}

	private Vector2I MouseHex()
	{
		return HexMath.LocalToHex(Orientation, GetLocalMousePosition(), Radius);
	}

	private void UpdateBoxSelection(Vector2I end)
	{
		int minQ = Mathf.Min(_pressHex.X, end.X);
		int maxQ = Mathf.Max(_pressHex.X, end.X);
		int minR = Mathf.Min(_pressHex.Y, end.Y);
		int maxR = Mathf.Max(_pressHex.Y, end.Y);
		SelectedCells.Clear();
		for (int q = minQ; q <= maxQ; q++)
			for (int r = minR; r <= maxR; r++)
				SelectedCells.Add(new Vector2I(q, r));
		QueueRedraw();
	}

	private void CommitBoxSelection()
	{
		UpdateBoxSelection(_lastHex);
	}

	public void ClearSelection()
	{
		SelectedCells.Clear();
		QueueRedraw();
	}

	public override void _Draw()
	{
		DrawTerrain();
		DrawGeneration();
		DrawSpecial();
		if (ShowGrid) DrawGrid();
		DrawSelection();
	}

	private void DrawTerrain()
	{
		foreach (var kv in _data.TerrainSources)
		{
			Color fill = kv.Value == 1 ? IslandColor : OceanColor;
			DrawHex(kv.Key, fill, new Color(0f, 0f, 0f, 0.45f));
		}
	}

	private void DrawSpecial()
	{
		Font font = ThemeDB.FallbackFont;
		foreach (var kv in _data.SpecialTiles)
		{
			DrawHex(kv.Key, SpecialCellCatalog.ColorFor(kv.Value), new Color(0.9f, 0.6f, 0f, 1f));
			Vector2 center = HexMath.HexToLocal(Orientation, kv.Key, Radius);
			DrawString(font, center + new Vector2(-16f, 7f), SpecialCellCatalog.Name(kv.Value),
				HorizontalAlignment.Left, 12f, 18, Colors.White);
		}
	}

	private void DrawGeneration()
	{
		Font font = ThemeDB.FallbackFont;
		foreach (var kv in _data.GenerationPoints)
		{
			Color fill = kv.Value.Side == GenerationSide.Enemy
				? new Color(0.95f, 0.25f, 0.25f, 0.6f)
				: new Color(0.25f, 0.5f, 1f, 0.6f);
			DrawHex(kv.Key, fill, Colors.White);
			Vector2 center = HexMath.HexToLocal(Orientation, kv.Key, Radius);
			string label = kv.Value.Side == GenerationSide.Enemy ? "E" : "P";
			DrawString(font, center + new Vector2(-6f, 7f), label, HorizontalAlignment.Left, 12f, 18, Colors.White);
		}
	}

	private void DrawGrid()
	{
		var cells = new HashSet<Vector2I>();
		foreach (var kv in _data.TerrainSources) cells.Add(kv.Key);
		foreach (var kv in _data.GenerationPoints) cells.Add(kv.Key);
		foreach (var kv in _data.SpecialTiles) cells.Add(kv.Key);
		if (cells.Count == 0)
		{
			for (int q = -5; q <= 5; q++)
				for (int r = -5; r <= 5; r++)
					cells.Add(new Vector2I(q, r));
		}

		Color gridColor = new(1f, 1f, 1f, 0.35f);
		foreach (Vector2I hex in cells)
		{
			Vector2 center = HexMath.HexToLocal(Orientation, hex, Radius);
			DrawPolyline(HexMath.HexagonPoints(Orientation, center, Radius), gridColor, 1f, true);
		}
	}

	private void DrawSelection()
	{
		foreach (Vector2I hex in SelectedCells)
		{
			Vector2 center = HexMath.HexToLocal(Orientation, hex, Radius);
			Vector2[] points = HexMath.HexagonPoints(Orientation, center, Radius);
			DrawColoredPolygon(points, new Color(1f, 1f, 0.4f, 0.25f));
			DrawPolyline(points, new Color(1f, 1f, 0.4f, 0.9f), 2f, true);
		}
	}

	private void DrawHex(Vector2I hex, Color fill, Color border)
	{
		Vector2 center = HexMath.HexToLocal(Orientation, hex, Radius);
		Vector2[] points = HexMath.HexagonPoints(Orientation, center, Radius);
		DrawColoredPolygon(points, fill);
		DrawPolyline(points, border, 1.2f, true);
	}
}

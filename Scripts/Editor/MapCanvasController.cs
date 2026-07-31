using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>编辑器工具类型。</summary>
public enum MapEditorTool
{
	Select,   // 点选 + 拖拽框选
	Pencil,   // 铅笔：单格/连续绘制
	Fill,     // 油漆桶：连通区域填充
	Eraser    // 橡皮：清除当前层图块
}

/// <summary>
/// 编辑器画布控制器（Node2D）。
/// 职责：把鼠标操作翻译为 TileMapLayer 的增删改，并同步回 LevelDataManager；
/// 同时负责框选高亮绘制（_Draw）与右键生成点检查器的触发。
/// 图层划分：地形=TerrainLayer，生成=GenerationLayer，特殊=SpecialLayer。
/// </summary>
public partial class MapCanvasController : Node2D
{
	private TileMapLayer _terrainLayer;
	private TileMapLayer _generationLayer;
	private TileMapLayer _specialLayer;
	private LevelDataManager _data;
	private ShipNameOverlay _nameOverlay;
	private EditorUIController _ui;

	// ── 当前编辑状态 ──
	public MapEditorTool CurrentTool { get; private set; } = MapEditorTool.Pencil;
	public string ActiveCategory { get; private set; } = "Terrain";
	public int ActiveSourceId { get; private set; } = 2;
	/// <summary>是否绘制六角网格线（工具栏“网格”开关）。</summary>
	public bool ShowGrid { get; private set; }

	// ── 框选状态 ──
	public HashSet<Vector2I> SelectedCells { get; } = new();

	private Vector2I _pressCell;
	private Vector2I _lastCell;
	private Vector2 _rightPressPos;
	private float _rightDragDistance;
	private bool _leftHeld;
	private bool _rightHeld;

	public override void _Ready()
	{
		_terrainLayer = GetNodeOrNull<TileMapLayer>("../MapEditor/TerrainLayer");
		_generationLayer = GetNodeOrNull<TileMapLayer>("../MapEditor/GenerationLayer");
		_specialLayer = GetNodeOrNull<TileMapLayer>("../MapEditor/SpecialLayer");
		_data = GetNodeOrNull<LevelDataManager>("../LevelDataManager");
		_nameOverlay = GetNodeOrNull<ShipNameOverlay>("ShipNameOverlay");
		_ui = GetNodeOrNull<EditorUIController>("../CanvasLayer/EditorUI");
	}

	// ── 公开配置 ──

	/// <summary>切换工具（由工具栏按钮调用）。</summary>
	public void SetTool(MapEditorTool tool)
	{
		CurrentTool = tool;
		if (tool != MapEditorTool.Select) ClearSelection();
	}

	/// <summary>切换当前绘制图块（由调色板按钮调用）。</summary>
	public void SetPalette(string category, int sourceId)
	{
		ActiveCategory = category;
		ActiveSourceId = sourceId;
	}

	/// <summary>切换网格线显示（由工具栏“网格”开关调用）。</summary>
	public void SetGridVisible(bool visible)
	{
		ShowGrid = visible;
		QueueRedraw();
	}

	/// <summary>返回当前分类对应的 TileMapLayer。</summary>
	public TileMapLayer LayerForCategory(string category) => category switch
	{
		"Generation" => _generationLayer,
		"Special" => _specialLayer,
		_ => _terrainLayer
	};

	private TileMapLayer ActiveLayer => LayerForCategory(ActiveCategory);

	// ── 数据 ↔ 图层 ──

	/// <summary>把 LevelDataManager 的数据整体刷到三个 TileMapLayer（打开/新建画布后调用）。</summary>
	public void ApplyDataToLayers()
	{
		_terrainLayer.Clear();
		_generationLayer.Clear();
		_specialLayer.Clear();

		foreach (var kv in _data.TerrainSources)
			SetCellWithData(_terrainLayer, kv.Key, kv.Value);
		foreach (var kv in _data.GenerationPoints)
			SetCellWithData(_generationLayer, kv.Key, kv.Value.SourceId);
		foreach (var kv in _data.SpecialTiles)
			SetCellWithData(_specialLayer, kv.Key, kv.Value);

		ClearSelection();
		RefreshOverlay();
	}

	/// <summary>清空三个图层与内存数据（新建画布时使用）。</summary>
	public void ClearCanvas()
	{
		_terrainLayer.Clear();
		_generationLayer.Clear();
		_specialLayer.Clear();
		ClearSelection();
		RefreshOverlay();
	}

	/// <summary>重绘船名覆盖层与选中框。</summary>
	public void RefreshOverlay()
	{
		_nameOverlay?.RedrawAll();
		QueueRedraw();
	}

	/// <summary>轴向坐标转 TileMap cell 后写入图块。</summary>
	private static void SetCellWithData(TileMapLayer layer, Vector2I axial, int sourceId)
	{
		layer.SetCell(CellFromAxial(axial), sourceId, Vector2I.Zero);
	}

	// ── 输入处理 ──

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb) HandleMouseButton(mb);
		else if (@event is InputEventMouseMotion mm) HandleMouseMotion(mm);
		else if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Delete)
			EraseSelection();
	}

	/// <summary>左键绘制或框选；右键短按打开生成点检查器。</summary>
	private void HandleMouseButton(InputEventMouseButton mb)
	{
		if (mb.ButtonIndex == MouseButton.Left)
		{
			if (mb.Pressed)
			{
				_leftHeld = true;
				_pressCell = MouseCell();
				_lastCell = _pressCell;
				if (CurrentTool == MapEditorTool.Select)
				{
					SelectedCells.Clear();
					SelectedCells.Add(_pressCell);
					QueueRedraw();
				}
				else if (CurrentTool == MapEditorTool.Fill)
				{
					// 油漆桶在释放时执行，避免拖拽误触
				}
				else
				{
					ApplyStroke(_pressCell);
				}
			}
			else
			{
				_leftHeld = false;
				if (CurrentTool == MapEditorTool.Fill) ApplyStroke(_pressCell);
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
				// 短按右键 = 打开生成点检查器
				_rightHeld = false;
				Vector2I cell = MouseCell();
				Vector2I hex = AxialFromCell(cell);
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

	/// <summary>拖拽时更新框选或连续绘制路径。</summary>
	private void HandleMouseMotion(InputEventMouseMotion mm)
	{
		if (_rightHeld)
			_rightDragDistance += mm.Relative.Length();

		if (!_leftHeld) return;

		Vector2I cell = MouseCell();
		if (cell == _lastCell) return;

		switch (CurrentTool)
		{
			case MapEditorTool.Select:
				UpdateBoxSelection(cell);
				break;
			case MapEditorTool.Pencil:
			case MapEditorTool.Eraser:
				StrokeLine(_lastCell, cell);
				break;
		}
		_lastCell = cell;
	}

	// ── 绘制/填充/擦除 ──

	private void ApplyStroke(Vector2I cell)
	{
		switch (CurrentTool)
		{
			case MapEditorTool.Pencil:
				PaintCell(cell, ActiveLayer, ActiveSourceId);
				break;
			case MapEditorTool.Eraser:
				EraseCellAt(cell, ActiveLayer);
				break;
			case MapEditorTool.Fill:
				FloodFill(cell, ActiveLayer, ActiveSourceId);
				break;
		}
		SyncActiveLayerToData();
		RefreshOverlay();
	}

	/// <summary>用直线步进补全拖拽路径上的中间格。</summary>
	private void StrokeLine(Vector2I from, Vector2I to)
	{
		// 用直线步进补全拖拽路径上的中间格，避免快速拖动出现断点
		Vector2I delta = to - from;
		int steps = Mathf.Max(Mathf.Abs(delta.X), Mathf.Abs(delta.Y));
		for (int i = 0; i <= steps; i++)
		{
			Vector2I p = from + delta * i / Mathf.Max(1, steps);
			if (CurrentTool == MapEditorTool.Eraser) EraseCellAt(p, ActiveLayer);
			else PaintCell(p, ActiveLayer, ActiveSourceId);
		}
		SyncActiveLayerToData();
		RefreshOverlay();
	}

	/// <summary>在指定 cell 写入源图块。</summary>
	private void PaintCell(Vector2I cell, TileMapLayer layer, int sourceId)
	{
		layer.SetCell(cell, sourceId, Vector2I.Zero);
	}

	/// <summary>擦除指定 cell 的图块。</summary>
	private void EraseCellAt(Vector2I cell, TileMapLayer layer)
	{
		layer.EraseCell(cell);
	}

	/// <summary>油漆桶：从起点 BFS 填充同源连通区域。</summary>
	private void FloodFill(Vector2I startCell, TileMapLayer layer, int replacement)
	{
		if (layer == null) return;
		int target = layer.GetCellSourceId(startCell);
		if (target == replacement) return;
		// 空白格没有连通区域，直接落单格，避免一次填满整张画布
		if (target < 0)
		{
			layer.SetCell(startCell, replacement, Vector2I.Zero);
			return;
		}

		var queue = new Queue<Vector2I>();
		var visited = new HashSet<Vector2I>();
		queue.Enqueue(startCell);

		while (queue.Count > 0)
		{
			Vector2I c = queue.Dequeue();
			if (!visited.Add(c)) continue;
			if (layer.GetCellSourceId(c) != target) continue;
			layer.SetCell(c, replacement, Vector2I.Zero);
			foreach (Vector2I n in CellNeighbors(c)) queue.Enqueue(n);
		}
	}

	/// <summary>删除当前选中区域（Delete 键 / 编辑器按钮）。</summary>
	public void EraseSelection()
	{
		if (SelectedCells.Count == 0) return;
		TileMapLayer layer = ActiveLayer;
		foreach (Vector2I hex in SelectedCells)
		{
			layer.EraseCell(CellFromAxial(hex));
			EraseFromData(hex);
		}
		SelectedCells.Clear();
		SyncActiveLayerToData();
		RefreshOverlay();
	}

	/// <summary>根据当前分类把某格从数据表里移除。</summary>
	private void EraseFromData(Vector2I hex)
	{
		switch (ActiveCategory)
		{
			case "Generation": _data.EraseGeneration(hex); break;
			case "Special": _data.EraseSpecial(hex); break;
			default: _data.EraseTerrain(hex); break;
		}
	}

	// ── 数据同步 ──

	/// <summary>把当前活动图层的全部格子写回 LevelDataManager。</summary>
	private void SyncActiveLayerToData()
	{
		TileMapLayer layer = ActiveLayer;
		foreach (Vector2I cell in layer.GetUsedCells())
		{
			int sourceId = layer.GetCellSourceId(cell);
			Vector2I hex = AxialFromCell(cell);
			switch (ActiveCategory)
			{
				case "Generation":
					// 4=敌方标记，6=玩家标记（兼容现有 tileset）
					_data.SetGeneration(hex, sourceId == 4 ? GenerationSide.Enemy : GenerationSide.Player, sourceId);
					break;
				case "Special":
					_data.SetSpecial(hex, sourceId);
					break;
				default:
					_data.SetTerrain(hex, sourceId);
					break;
			}
		}
		// 删除层上已不存在的格子（防止拖拽擦除后残留内存数据）
		HashSet<Vector2I> remaining = layer.GetUsedCells().Select(AxialFromCell).ToHashSet();
		var stale = new List<Vector2I>();
		switch (ActiveCategory)
		{
			case "Generation":
				foreach (var hex in _data.GenerationPoints.Keys) if (!remaining.Contains(hex)) stale.Add(hex);
				foreach (var hex in stale) _data.EraseGeneration(hex);
				break;
			case "Special":
				foreach (var hex in _data.SpecialTiles.Keys) if (!remaining.Contains(hex)) stale.Add(hex);
				foreach (var hex in stale) _data.EraseSpecial(hex);
				break;
			default:
				foreach (var hex in _data.TerrainSources.Keys) if (!remaining.Contains(hex)) stale.Add(hex);
				foreach (var hex in stale) _data.EraseTerrain(hex);
				break;
		}
	}

	// ── 坐标换算 ──

	private Vector2I MouseCell()
	{
		TileMapLayer layer = ActiveLayer;
		if (layer == null) return Vector2I.Zero;
		return layer.LocalToMap(layer.ToLocal(GetGlobalMousePosition()));
	}

	/// <summary>offset cell 坐标转轴向坐标。</summary>
	private static Vector2I AxialFromCell(Vector2I cell) => new(cell.X - (cell.Y >> 1), cell.Y);
	private static Vector2I CellFromAxial(Vector2I axial) => new(axial.X + (axial.Y >> 1), axial.Y);

	/// <summary>六角格相邻 cell 坐标（偶数行偏移布局）。</summary>
	private static IEnumerable<Vector2I> CellNeighbors(Vector2I cell)
	{
		Vector2I axial = AxialFromCell(cell);
		foreach (HexDirection dir in System.Enum.GetValues<HexDirection>())
			yield return CellFromAxial(axial + HexDirectionUtility.Offset(dir));
	}

	/// <summary>拖拽时实时更新框选矩形。</summary>
	private void UpdateBoxSelection(Vector2I end)
	{
		int minX = Mathf.Min(_pressCell.X, end.X);
		int maxX = Mathf.Max(_pressCell.X, end.X);
		int minY = Mathf.Min(_pressCell.Y, end.Y);
		int maxY = Mathf.Max(_pressCell.Y, end.Y);
		SelectedCells.Clear();
		for (int y = minY; y <= maxY; y++)
			for (int x = minX; x <= maxX; x++)
				SelectedCells.Add(AxialFromCell(new Vector2I(x, y)));
		QueueRedraw();
	}

	/// <summary>拖拽结束时提交框选区域。</summary>
	private void CommitBoxSelection()
	{
		// 拖拽框选：以按下格与当前格为对角线取矩形内的所有格
		Vector2I end = _lastCell;
		int minX = Mathf.Min(_pressCell.X, end.X);
		int maxX = Mathf.Max(_pressCell.X, end.X);
		int minY = Mathf.Min(_pressCell.Y, end.Y);
		int maxY = Mathf.Max(_pressCell.Y, end.Y);
		SelectedCells.Clear();
		for (int y = minY; y <= maxY; y++)
			for (int x = minX; x <= maxX; x++)
				SelectedCells.Add(AxialFromCell(new Vector2I(x, y)));
		QueueRedraw();
	}

	/// <summary>清空当前选中格并重绘画布。</summary>
	public void ClearSelection()
	{
		SelectedCells.Clear();
		QueueRedraw();
	}

	// ── 绘制：框选高亮 ──

	public override void _Draw()
	{
		if (ShowGrid) DrawGrid();
		if (SelectedCells.Count == 0) return;
		TileMapLayer layer = _terrainLayer;
		if (layer == null) return;
		Vector2I tileSize = layer.TileSet?.TileSize ?? new Vector2I(120, 140);

		foreach (Vector2I hex in SelectedCells)
		{
			Vector2 center = layer.MapToLocal(CellFromAxial(hex));
			Rect2 rect = new(center - new Vector2(tileSize.X, tileSize.Y) / 2f, tileSize);
			DrawRect(rect, new Color(1f, 1f, 0.4f, 0.25f), true);
			DrawRect(rect, new Color(1f, 1f, 0.4f, 0.9f), false, 2f);
		}
	}

	/// <summary>绘制六角网格线：遍历已用格；空画布时回退到原点附近一块区域。</summary>
	private void DrawGrid()
	{
		TileMapLayer layer = _terrainLayer ?? _generationLayer ?? _specialLayer;
		if (layer == null) return;
		Vector2I tileSize = layer.TileSet?.TileSize ?? new Vector2I(120, 140);

		var cells = new HashSet<Vector2I>();
		foreach (TileMapLayer l in new[] { _terrainLayer, _generationLayer, _specialLayer })
		{
			if (l == null) continue;
			foreach (Vector2I c in l.GetUsedCells()) cells.Add(c);
		}
		if (cells.Count == 0)
		{
			for (int y = -5; y <= 5; y++)
				for (int x = -5; x <= 5; x++)
					cells.Add(new Vector2I(x, y));
		}

		float w = tileSize.X * 0.5f;
		float h = tileSize.Y * 0.5f;
		Color gridColor = new(1f, 1f, 1f, 0.35f);
		foreach (Vector2I cell in cells)
		{
			Vector2 center = layer.MapToLocal(cell);
			var points = new Vector2[]
			{
				center + new Vector2(w, 0f),
				center + new Vector2(w * 0.5f, -h),
				center + new Vector2(-w * 0.5f, -h),
				center + new Vector2(-w, 0f),
				center + new Vector2(-w * 0.5f, h),
				center + new Vector2(w * 0.5f, h),
				center + new Vector2(w, 0f)
			};
			DrawPolyline(points, gridColor, 1f, true);
		}
	}
}
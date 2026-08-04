using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战术范围高亮控制器。监听 EventBus 的 OverlayDrawRequested 等信号，
/// 用 Overlay3D 模型实例覆盖移动/攻击范围；场景未配置时退回材质高亮。
/// </summary>
public partial class GridOverlayController : Node
{
	[Export] public StandardMaterial3D MoveMaterial;
	[Export] public StandardMaterial3D AttackMaterial;
	[Export] public StandardMaterial3D AttackFrontMaterial;
	[Export] public PackedScene GridOverlayScene;
	[Export] public PackedScene DirectionOverlayScene;
	[Export] public PackedScene ArriveOverlayScene;
	[Export] public PackedScene AttackFrontBackScene;
	[Export] public PackedScene AttackSideScene;
	/// <summary>方向性 overlay 模型的基础朝向，和船模共用同一套角度。</summary>
	[Export] public float DirectionYawOffsetDegrees = 180f;
	[Export] public float NSModelYawOffsetDegrees = 30f;
	/// <summary>overlay 模型相对六角格中心的竖轴偏移。</summary>
	[Export] public float OverlayHeightOffset = 0.25f;

	private Dictionary<Vector2I, MeshInstance3D> _targets = new();
	private Dictionary<MeshInstance3D, Material> _originalMaterials = new();
	private MapGenerator _mapGenerator;
	private LevelDataManager _dataManager;
	private HexOrientation _orientation = HexOrientation.EWHorizontal;
	private Node3D _overlayRoot;
	private Node3D _directionRoot;
	private Node3D _globalGridRoot;
	private readonly List<Node3D> _directionInstances = new();
	private readonly List<Node3D> _globalGridInstances = new();

	public override void _Ready()
	{
		_mapGenerator = GetNodeOrNull<MapGenerator>("../MapGenerator");
		_dataManager = GetNodeOrNull<LevelDataManager>("../LevelDataManager");
		_orientation = _dataManager?.MapOrientation ?? HexOrientation.EWHorizontal;
		_overlayRoot = new Node3D { Name = "OverlayRoot" };
		AddChild(_overlayRoot);
		_directionRoot = new Node3D { Name = "DirectionOverlayRoot" };
		AddChild(_directionRoot);
		_globalGridRoot = new Node3D { Name = "GlobalGridOverlayRoot" };
		AddChild(_globalGridRoot);
		EnsureOverlayScenes();

		var bus = GetNode<EventBus>("../EventBus");
		bus.OverlayDrawRequested += DrawTacticalRange;
		bus.OverlayArcDrawRequested += DrawForwardArc;
		bus.OverlayClearRequested += ClearOverlay;
		bus.MoveTargetHighlighted += HighlightMoveTarget;
		bus.DirectionOverlayRequested += ShowDirection;
		bus.DirectionOverlayClearRequested += ClearDirectionOverlay;
	}

	public void InitializeOverlayTargets(Dictionary<Vector2I, MeshInstance3D> meshes)
	{
		_targets = meshes;
		_originalMaterials.Clear();
		foreach (var (_, mesh) in meshes)
			if (GodotObject.IsInstanceValid(mesh))
				_originalMaterials[mesh] = mesh.MaterialOverride;
	}

	// 攻击范围：前/后射界用 AttackFrontMaterial，侧射用 AttackMaterial
	public void DrawTacticalRange(Vector2I center, int moveRange, int attackRange,
		int directionInt, int arcMask, int stateInt = 0)
	{
		ClearOverlay();
		if ((UnitTacticalState)stateInt == UnitTacticalState.Actioned) return;
		HexDirection direction = (HexDirection)directionInt;

		if (OverlayModelMode())
		{
			foreach (var (coords, _) in _targets)
			{
				int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
				if (dist <= moveRange && dist > 0)
				{
					continue;
				}
				else if (dist <= attackRange && dist > moveRange)
				{
					FiringArc arc = FiringArcEvaluator.GetArc(center, coords, direction);
					int arcBit = arc switch
					{
						FiringArc.Front => 1,
						FiringArc.Rear => 4,
						_ => 2
					};
					if ((arcMask & arcBit) == 0) continue;
					SpawnOverlay(
						arc is FiringArc.Front or FiringArc.Rear
							? AttackFrontBackScene
							: AttackSideScene,
						coords,
						direction);
				}
			}
			return;
		}

		foreach (var (coords, mesh) in _targets)
		{
			int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
			if (dist <= moveRange && dist > 0) mesh.MaterialOverride = MoveMaterial;
			else if (dist <= attackRange && dist > moveRange)
			{
				FiringArc arc = FiringArcEvaluator.GetArc(center, coords, direction);
				int arcBit = arc switch
				{
					FiringArc.Front => 1,
					FiringArc.Rear => 4,
					_ => 2
				};
				if ((arcMask & arcBit) == 0) continue;
				mesh.MaterialOverride = arc is FiringArc.Front or FiringArc.Rear
					? AttackFrontMaterial
					: AttackMaterial;
			}
		}
	}

	// 机动目标：只高亮惯性推算的唯一到达格
	public void HighlightMoveTarget(Vector2I target, int directionInt)
	{
		ClearOverlay();
		if (OverlayModelMode())
		{
			SpawnOverlay(ArriveOverlayScene, target, (HexDirection)directionInt);
			return;
		}
		if (_targets.TryGetValue(target, out var mesh) && GodotObject.IsInstanceValid(mesh))
			mesh.MaterialOverride = MoveMaterial;
	}

	/// <summary>航向锥形可到达格预览：高亮前方 120° 扇面内、距离范围内的格。</summary>
	public void DrawForwardArc(Vector2I center, int directionInt, int range)
	{
		ClearOverlay();
		HexDirection direction = (HexDirection)directionInt;
		if (OverlayModelMode())
		{
			foreach (var (coords, _) in _targets)
			{
				int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
				if (dist <= range && dist > 0
					&& MoveRulesEvaluator.IsInForwardArc(center, coords, direction))
					continue;
			}
			return;
		}
		foreach (var (coords, mesh) in _targets)
		{
			int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
			if (dist <= range && dist > 0
				&& MoveRulesEvaluator.IsInForwardArc(center, coords, direction))
				mesh.MaterialOverride = MoveMaterial;
		}
	}

	public void ClearOverlay()
	{
		if (_overlayRoot != null)
		{
			foreach (Node child in _overlayRoot.GetChildren())
			{
				_overlayRoot.RemoveChild(child);
				child.QueueFree();
			}
		}
		foreach (var (_, mesh) in _targets)
		{
			if (!GodotObject.IsInstanceValid(mesh)) continue;
			mesh.MaterialOverride = _originalMaterials.TryGetValue(mesh, out var orig) ? orig : null;
		}
	}

	/// <summary>在单位所在格显示控制方向标记（单纵阵头 / 独行舰）。</summary>
	public void ShowDirection(Vector2I hex, int directionInt)
	{
		RefreshDirections(new[] { (hex, (HexDirection)directionInt) });
	}

	/// <summary>刷新所有“控制方向”单位的方向标记：单纵阵头与独行舰。</summary>
	public void RefreshDirections(IReadOnlyList<(Vector2I Hex, HexDirection Direction)> entries)
	{
		ClearDirectionOverlay();
		if (DirectionOverlayScene == null || _directionRoot == null) return;
		foreach (var entry in entries)
		{
			_directionInstances.Add(SpawnOverlay(
				DirectionOverlayScene,
				entry.Hex,
				entry.Direction,
				_directionRoot));
		}
	}

	public void ClearDirectionOverlay()
	{
		foreach (Node3D instance in _directionInstances)
		{
			if (!GodotObject.IsInstanceValid(instance)) continue;
			_directionRoot?.RemoveChild(instance);
			instance.QueueFree();
		}
		_directionInstances.Clear();
	}

	/// <summary>
	/// 以全地图最西北格为 0,0 生成全局坐标网格。Label3D 显示相对轴向坐标。
	/// </summary>
	public void BuildGlobalGrid()
	{
		ClearGlobalGrid();
		if (GridOverlayScene == null || _dataManager == null || _globalGridRoot == null) return;

		var hexes = new List<Vector2I>(_dataManager.TerrainSources.Keys);
		if (hexes.Count == 0) return;

		Vector3 minWorld = HexToWorld(hexes[0]);
		foreach (Vector2I hex in hexes)
		{
			Vector3 world = HexToWorld(hex);
			minWorld = new Vector3(Mathf.Min(minWorld.X, world.X), 0f, Mathf.Min(minWorld.Z, world.Z));
		}

		float radius = GameConfig.HexRadius;
		float cellWidth = _orientation == HexOrientation.NSVertical
			? Mathf.Sqrt(3f) * radius
			: 1.5f * radius;
		float cellHeight = _orientation == HexOrientation.NSVertical
			? 1.5f * radius
			: Mathf.Sqrt(3f) * radius;

		foreach (Vector2I hex in hexes)
		{
			Node3D instance = SpawnOverlay(GridOverlayScene, hex, null, _globalGridRoot);
			if (instance == null) continue;
			Label3D label = instance.FindChild("Label3D", true, false) as Label3D;
			if (label != null)
			{
				Vector3 world = HexToWorld(hex);
				int col = Mathf.RoundToInt((world.X - minWorld.X) / cellWidth);
				int row = Mathf.RoundToInt((world.Z - minWorld.Z) / cellHeight);
				label.Text = EncodeCoord(col) + EncodeCoord(row);
			}
			_globalGridInstances.Add(instance);
		}
	}

	/// <summary>两位 base-36：0-9 后接 A-Z，例如 1 → 01、35 → 0Z、36 → 10。</summary>
	private static string EncodeCoord(int value)
	{
		const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		int safe = Math.Max(0, value);
		int high = Mathf.Clamp(safe / 36, 0, 35);
		int low = safe % 36;
		return $"{digits[high]}{digits[low]}";
	}

	public void ClearGlobalGrid()
	{
		foreach (Node3D instance in _globalGridInstances)
		{
			if (!GodotObject.IsInstanceValid(instance)) continue;
			_globalGridRoot?.RemoveChild(instance);
			instance.QueueFree();
		}
		_globalGridInstances.Clear();
	}

	private Vector3 HexToWorld(Vector2I hex)
		=> _mapGenerator?.HexToWorld(hex.X, hex.Y) ?? Vector3.Zero;

	private bool OverlayModelMode()
		=> GridOverlayScene != null || DirectionOverlayScene != null
			|| ArriveOverlayScene != null || AttackFrontBackScene != null
			|| AttackSideScene != null;

	private void EnsureOverlayScenes()
	{
		const string overlayRoot = "res://Scenes/Map/Tile/Prefab/Overlay3D";
		GridOverlayScene ??= ResourceLoader.Load<PackedScene>($"{overlayRoot}/grid.tscn");
		DirectionOverlayScene ??= ResourceLoader.Load<PackedScene>($"{overlayRoot}/direction.tscn");
		ArriveOverlayScene ??= ResourceLoader.Load<PackedScene>($"{overlayRoot}/arrive.tscn");
		AttackFrontBackScene ??= ResourceLoader.Load<PackedScene>($"{overlayRoot}/attack_behind_front.tscn");
		AttackSideScene ??= ResourceLoader.Load<PackedScene>($"{overlayRoot}/attack_side.tscn");
	}

	private Node3D SpawnOverlay(PackedScene scene, Vector2I hex,
		HexDirection? direction = null, Node3D parent = null)
	{
		parent ??= _overlayRoot;
		if (scene == null || parent == null) return null;
		Node3D instance = scene.Instantiate<Node3D>();
		parent.AddChild(instance);
		Vector3 world = _mapGenerator?.HexToWorld(hex.X, hex.Y) ?? Vector3.Zero;
		instance.Position = new Vector3(world.X, OverlayHeightOffset, world.Z);
		if (direction.HasValue)
			instance.RotationDegrees = new Vector3(0f, OverlayYaw(direction.Value), 0f);
		return instance;
	}

	private float OverlayYaw(HexDirection direction)
	{
		float mapOffset = _orientation == HexOrientation.NSVertical ? NSModelYawOffsetDegrees : 0f;
		return DirectionYawOffsetDegrees - (int)direction * 60f + mapOffset;
	}
}

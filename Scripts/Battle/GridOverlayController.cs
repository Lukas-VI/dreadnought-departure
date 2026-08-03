using Godot;
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

	private Dictionary<Vector2I, MeshInstance3D> _targets = new();
	private Dictionary<MeshInstance3D, Material> _originalMaterials = new();
	private MapGenerator _mapGenerator;
	private LevelDataManager _dataManager;
	private HexOrientation _orientation = HexOrientation.EWHorizontal;
	private Node3D _overlayRoot;

	public override void _Ready()
	{
		_mapGenerator = GetNodeOrNull<MapGenerator>("../MapGenerator");
		_dataManager = GetNodeOrNull<LevelDataManager>("../LevelDataManager");
		_orientation = _dataManager?.MapOrientation ?? HexOrientation.EWHorizontal;
		_overlayRoot = new Node3D { Name = "OverlayRoot" };
		AddChild(_overlayRoot);
		EnsureOverlayScenes();

		var bus = GetNode<EventBus>("../EventBus");
		bus.OverlayDrawRequested += DrawTacticalRange;
		bus.OverlayArcDrawRequested += DrawForwardArc;
		bus.OverlayClearRequested += ClearOverlay;
		bus.MoveTargetHighlighted += HighlightMoveTarget;
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
			if (moveRange > 0)
				SpawnOverlay(DirectionOverlayScene, center, direction);
			foreach (var (coords, _) in _targets)
			{
				int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
				if (dist <= moveRange && dist > 0)
				{
					SpawnOverlay(GridOverlayScene, coords);
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
	public void HighlightMoveTarget(Vector2I target)
	{
		ClearOverlay();
		if (OverlayModelMode())
		{
			SpawnOverlay(ArriveOverlayScene, target);
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
			SpawnOverlay(DirectionOverlayScene, center, direction);
			foreach (var (coords, _) in _targets)
			{
				int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
				if (dist <= range && dist > 0
					&& MoveRulesEvaluator.IsInForwardArc(center, coords, direction))
					SpawnOverlay(GridOverlayScene, coords);
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

	private Node3D SpawnOverlay(PackedScene scene, Vector2I hex, HexDirection? direction = null)
	{
		if (scene == null || _overlayRoot == null) return null;
		Node3D instance = scene.Instantiate<Node3D>();
		_overlayRoot.AddChild(instance);
		Vector3 world = _mapGenerator?.HexToWorld(hex.X, hex.Y) ?? Vector3.Zero;
		instance.Position = new Vector3(world.X, 0.18f, world.Z);
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

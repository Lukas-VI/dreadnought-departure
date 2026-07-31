using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战术范围高亮控制器。监听 EventBus 的 OverlayDrawRequested 等信号，
/// 通过替换 MeshInstance3D 的 MaterialOverride 来高亮移动/攻击范围。
/// InitializeOverlayTargets 在战场生成时注入所有瓦片 Mesh 引用。
/// </summary>
public partial class GridOverlayController : Node
{
	[Export] public StandardMaterial3D MoveMaterial;
	[Export] public StandardMaterial3D AttackMaterial;

	private Dictionary<Vector2I, MeshInstance3D> _targets = new();
	private Dictionary<MeshInstance3D, Material> _originalMaterials = new();

	public override void _Ready()
	{
		var bus = GetNode<EventBus>("../EventBus");
		bus.OverlayDrawRequested += DrawTacticalRange;
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

	// 攻击范围：显示所有射程内格
	public void DrawTacticalRange(Vector2I center, int moveRange, int attackRange, int stateInt = 0)
	{
		ClearOverlay();
		if ((UnitTacticalState)stateInt == UnitTacticalState.Actioned) return;
		foreach (var (coords, mesh) in _targets)
		{
			int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
			if (dist <= moveRange && dist > 0) mesh.MaterialOverride = MoveMaterial;
			else if (dist <= attackRange && dist > moveRange) mesh.MaterialOverride = AttackMaterial;
		}
	}

	// 机动目标：只高亮惯性推算的唯一到达格
	public void HighlightMoveTarget(Vector2I target)
	{
		if (_targets.TryGetValue(target, out var mesh) && GodotObject.IsInstanceValid(mesh))
			mesh.MaterialOverride = MoveMaterial;
	}

	public void ClearOverlay()
	{
		foreach (var (_, mesh) in _targets)
		{
			if (!GodotObject.IsInstanceValid(mesh)) continue;
			mesh.MaterialOverride = _originalMaterials.TryGetValue(mesh, out var orig) ? orig : null;
		}
	}
}

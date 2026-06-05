using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class GridOverlayController : Node
{
    [Export] public StandardMaterial3D MoveMaterial;
    [Export] public StandardMaterial3D AttackMaterial;

    private Dictionary<Vector2I, MeshInstance3D> _targets = new();
    private Dictionary<MeshInstance3D, Material> _originalMaterials = new();

    // 初始化时，生成器把做好的 3D 网格小卡片塞给它，顺手记下每个格子的原始地形色
    public void InitializeOverlayTargets(Dictionary<Vector2I, MeshInstance3D> meshes)
    {
        _targets = meshes;
        _originalMaterials.Clear();
        foreach (var (_, mesh) in meshes)
            if (GodotObject.IsInstanceValid(mesh))
                _originalMaterials[mesh] = mesh.MaterialOverride;
    }

    public void DrawTacticalRange(Vector2I center, int moveRange, int attackRange)
    {
        ClearOverlay();
        foreach (var (coords, mesh) in _targets)
        {
            int dist = BattleRulesEvaluator.GetHexDistance(center, coords);
            if (dist <= moveRange && dist > 0) mesh.MaterialOverride = MoveMaterial;
            else if (dist <= attackRange && dist > moveRange) mesh.MaterialOverride = AttackMaterial;
        }
    }

    public void ClearOverlay()
    {
        foreach (var (_, mesh) in _targets)
        {
            if (!GodotObject.IsInstanceValid(mesh)) continue;
            // 恢复到该格子原本的地形色，而不是清成白色
            mesh.MaterialOverride = _originalMaterials.TryGetValue(mesh, out var orig) ? orig : null;
        }
    }
}



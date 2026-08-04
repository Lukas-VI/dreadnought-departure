using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>炮击反馈：受击目标 shader 闪红 + HIT/MISS 气泡。</summary>
public partial class BattleFeedbackController : Node3D
{
	private ShaderMaterial _hitMaterial;

	public override void _Ready()
	{
		GetNode<EventBus>("../EventBus").HitFeedbackRequested += OnHitFeedbackRequested;
	}

	private void OnHitFeedbackRequested(ShipComponent target, bool hit, int damage)
	{
		if (!GodotObject.IsInstanceValid(target))
		{
			return;
		}
		PlayFeedback(target, hit, damage);
	}

	public void PlayFeedback(ShipComponent target, bool hit, int damage)
	{
		if (!GodotObject.IsInstanceValid(target))
		{
			return;
		}
		_ = PlayFeedbackAsync(target, hit, damage);
	}

	private async System.Threading.Tasks.Task PlayFeedbackAsync(
		ShipComponent target, bool hit, int damage)
	{
		EnsureHitMaterial();
		var meshes = new List<MeshInstance3D>();
		CollectMeshes(target, meshes);
		var originals = new Dictionary<MeshInstance3D, Material>();
		if (hit && _hitMaterial != null)
		{
			foreach (MeshInstance3D mesh in meshes)
			{
				if (!GodotObject.IsInstanceValid(mesh)) continue;
				originals[mesh] = mesh.MaterialOverride;
				mesh.MaterialOverride = _hitMaterial;
			}
		}

		var label = new Label3D
		{
			Text = hit ? $"HIT -{damage}" : "MISS",
			FontSize = 52,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			Modulate = hit ? new Color(1f, 0.55f, 0.4f, 1f) : new Color(0.6f, 0.8f, 1f, 1f),
			Position = new Vector3(
				target.GlobalPosition.X,
				target.GlobalPosition.Y + 2.8f,
				target.GlobalPosition.Z),
		};
		AddChild(label);

		Tween tween = CreateTween();
		tween.SetParallel();
		tween.TweenProperty(label, "position:y", label.Position.Y + 1.4f, 0.8f);
		tween.TweenProperty(label, "modulate:a", 0f, 0.8f);
		tween.Chain().TweenCallback(Callable.From(() =>
		{
			if (GodotObject.IsInstanceValid(label))
				label.QueueFree();
			foreach (var (mesh, original) in originals)
			{
				if (!GodotObject.IsInstanceValid(mesh)) continue;
				mesh.MaterialOverride = original;
			}
		}));
		await ToSignal(tween, Tween.SignalName.Finished);
	}

	private void EnsureHitMaterial()
	{
		if (_hitMaterial != null) return;
		var shader = new Shader
		{
			Code = "shader_type spatial;\n"
				+ "render_mode unshaded;\n"
				+ "void fragment() {\n"
				+ "\tALBEDO = vec3(1.0, 0.25, 0.2);\n"
				+ "\tEMISSION = vec3(0.8, 0.05, 0.0);\n"
				+ "}\n",
		};
		_hitMaterial = new ShaderMaterial { Shader = shader };
	}

	private static void CollectMeshes(Node node, List<MeshInstance3D> result)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;
		if (node is MeshInstance3D mesh)
		{
			result.Add(mesh);
		}
		foreach (Node child in node.GetChildren())
		{
			CollectMeshes(child, result);
		}
	}
}

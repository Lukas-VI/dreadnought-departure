using Godot;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 3D 场景鼠标左键点击 → 六角格坐标事件发射器。
/// 两阶段射线检测：① 物理层 Layer 2 命中 ShipComponent → 发射单位所在格；
/// ② 命中 y=0 海平面 → 反算世界坐标 → 轴向六角格坐标 (Q,R)。
/// 两种结果都通过 EventBus.HexClicked 路由给 PlayerController。
/// </summary>
public partial class BattleInputDetector : Node
{
	private EventBus _bus;
	private HexOrientation _orientation = HexOrientation.EWHorizontal;

	public override void _Ready()
	{
		_bus = GetNodeOrNull<EventBus>("../EventBus");
		_orientation = GetNodeOrNull<LevelDataManager>("../LevelDataManager")?.MapOrientation
			?? HexOrientation.EWHorizontal;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left && mb.Pressed)
		{
			Camera3D camera = GetViewport().GetCamera3D();
			if (camera == null) return;

			Vector3 rayOrigin = camera.ProjectRayOrigin(mb.Position);
			Vector3 rayNormal = camera.ProjectRayNormal(mb.Position);
			Vector3 rayEnd = rayOrigin + rayNormal * 1000f;

			// 1. 先查船——物理射线（Layer 2 = 船碰撞体）
			var space = GetViewport().GetWorld3D().DirectSpaceState;
			var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd, 2);
			// 舰船碰撞体是 Area3D，默认射线不碰 Area，必须显式开启。
			query.CollideWithAreas = true;
			query.CollideWithBodies = false;
			var hit = space.IntersectRay(query);
			if (hit.Count > 0 && hit["collider"].Obj is Node3D hitNode)
			{
				var ship = hitNode.GetParentOrNull<ShipComponent>();
				if (ship != null)
				{
					_bus?.EmitSignal("HexClicked", ship.HexCoords);
					return;
				}
			}

			// 2. 没碰到船——算地面六角格
			if (rayNormal.Y != 0)
			{
				float t = -rayOrigin.Y / rayNormal.Y;
				Vector3 worldClickPos = rayOrigin + rayNormal * t;
				_bus?.EmitSignal("HexClicked", WorldToHex(worldClickPos, GameConfig.HexRadius));
			}
		}
	}

	private Vector2I WorldToHex(Vector3 w, float radius)
	{
		return HexMath.LocalToHex(_orientation, new Vector2(w.X, w.Z), radius);
	}
}

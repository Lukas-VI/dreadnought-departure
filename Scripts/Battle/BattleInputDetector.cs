using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class BattleInputDetector : Node
{
	[Signal] public delegate void HexClickedEventHandler(Vector2I hexCoords);

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
			var hit = space.IntersectRay(query);
			if (hit.Count > 0 && hit["collider"].Obj is Node3D hitNode)
			{
				var ship = hitNode.GetParentOrNull<ShipComponent>();
				if (ship != null)
				{
					EmitSignal(SignalName.HexClicked, ship.HexCoords);
					return;
				}
			}

			// 2. 没碰到船——算地面六角格
			if (rayNormal.Y != 0)
			{
				float t = -rayOrigin.Y / rayNormal.Y;
				Vector3 worldClickPos = rayOrigin + rayNormal * t;
				EmitSignal(SignalName.HexClicked, WorldToHex(worldClickPos, GameConfig.HexRadius));
			}
		}
	}

	private Vector2I WorldToHex(Vector3 w, float radius)
	{
		float q = (w.X * 2f / 3f) / radius;
		float r = ((-w.X / 3f) + (Mathf.Sqrt(3f) / 3f * w.Z)) / radius;
		float y = -q - r;
		int rx = Mathf.RoundToInt(q), ry = Mathf.RoundToInt(y), rz = Mathf.RoundToInt(r);
		if (Mathf.Abs(rx - q) > Mathf.Abs(ry - y) && Mathf.Abs(rx - q) > Mathf.Abs(rz - r)) rx = -ry - rz;
		else if (Mathf.Abs(rz - r) > Mathf.Abs(ry - y)) rz = -rx - ry;
		return new Vector2I(rx, rz);
	}
}

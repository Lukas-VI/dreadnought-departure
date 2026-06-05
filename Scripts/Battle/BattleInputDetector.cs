using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class BattleInputDetector : Node
{
    [Signal] public delegate void HexClickedEventHandler(Vector2I hexCoords);
    [Export] public float HexRadius = 1.0f;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left && mouseButton.Pressed)
        {
            Camera3D camera = GetViewport().GetCamera3D();
            if (camera == null) return;

            Vector3 rayOrigin = camera.ProjectRayOrigin(mouseButton.Position);
            Vector3 rayNormal = camera.ProjectRayNormal(mouseButton.Position);

            if (rayNormal.Y != 0)
            {
                float t = -rayOrigin.Y / rayNormal.Y;
                Vector3 worldClickPos = rayOrigin + rayNormal * t;
                
                // 纯粹的数学解算
                Vector2I clickedHex = WorldToHex(worldClickPos, HexRadius);
                
                // 啪的一下发射信号，谁爱听谁听，反正我不处理
                EmitSignal(SignalName.HexClicked, clickedHex);
            }
        }
    }

    private Vector2I WorldToHex(Vector3 worldPos, float radius)
    {
        float q = (worldPos.X * 2.0f / 3.0f) / radius;
        float r = ((-worldPos.X / 3.0f) + (Mathf.Sqrt(3.0f) / 3.0f * worldPos.Z)) / radius;
        
        float x = q; float z = r; float y = -x - z;
        int rx = Mathf.RoundToInt(x); int ry = Mathf.RoundToInt(y); int rz = Mathf.RoundToInt(z);

        if (Mathf.Abs(rx - x) > Mathf.Abs(ry - y) && Mathf.Abs(rx - x) > Mathf.Abs(rz - z)) rx = -ry - rz;
        else if (Mathf.Abs(rz - z) > Mathf.Abs(ry - y)) rz = -rx - ry;

        return new Vector2I(rx, rz);
    }
}
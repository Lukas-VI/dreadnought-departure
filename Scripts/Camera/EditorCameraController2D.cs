using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 2D camera controller for the tilemap editor scene.
/// Right-click drag pans; wheel zoom is smoothed and anchored to the world point
/// currently under the cursor, so the point under the mouse stays put.
/// The mouse itself is never warped or reset.
/// </summary>
public partial class EditorCameraController2D : Camera2D
{
	// World-space pan speed at zoom 1 (pixels per second).
	[Export] public float PanSpeed = 400f;
	// Fractional zoom change per scroll tick.
	[Export] public float ZoomStep = 0.15f;
	// Exponential smoothing rate for async wheel zoom.
	[Export] public float ZoomSmoothSpeed = 8f;
	// Zoom clamp so the tilemap stays usable at extreme values.
	[Export] public Vector2 ZoomRange = new(0.5f, 8f);

	private bool _isPanning;
	private Vector2 _lastMouse;
	private float _targetZoom = 1f;

	public override void _Ready()
	{
		_targetZoom = Zoom.X;
	}

	public override void _Process(double delta)
	{
		if (Mathf.IsEqualApprox(Zoom.X, _targetZoom)) return;

		// Anchor on the current cursor: keep the world point under the mouse fixed
		// while the zoom eases toward the target.
		Vector2 worldAnchor = GetGlobalMousePosition();
		float nextZoom = Mathf.Lerp(Zoom.X, _targetZoom, 1f - Mathf.Exp(-ZoomSmoothSpeed * (float)delta));
		Zoom = new Vector2(nextZoom, nextZoom);
		Position += worldAnchor - GetGlobalMousePosition();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			// Right-click drag start/end for panning.
			if (mb.ButtonIndex == MouseButton.Right)
			{
				_isPanning = mb.Pressed;
				_lastMouse = mb.Position;
			}

			// Wheel zoom: only move the target; _Process does the async smoothing.
			if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp)
				_targetZoom = Mathf.Clamp(_targetZoom * (1f + ZoomStep), ZoomRange.X, ZoomRange.Y);
			if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown)
				_targetZoom = Mathf.Clamp(_targetZoom / (1f + ZoomStep), ZoomRange.X, ZoomRange.Y);
		}

		// Right-click drag: invert the delta so the world drags under the cursor.
		if (@event is InputEventMouseMotion mm && _isPanning)
		{
			Vector2 delta = mm.Position - _lastMouse;
			Position -= delta / Zoom;
			_lastMouse = mm.Position;
		}
	}
}
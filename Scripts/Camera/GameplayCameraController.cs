using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 3D orbit camera rig for the battle scene.
/// Right-drag pans the focus, middle-drag orbits around the focus,
/// wheel zoom eases toward the target distance and keeps the ground point
/// under the cursor anchored. The cursor is never captured or warped.
/// Also exposes a global focus API (EventBus signals) used by phase logic,
/// story/cutscene overlays, and the ship operation pipeline.
/// </summary>
public partial class GameplayCameraController : Node3D
{
	[Export] public Camera3D GameplayCamera;
	[Export] public float MoveSpeed = 15.0f;
	[Export] public float ZoomSpeed = 2.0f;
	[Export] public Vector2 ZoomRange = new Vector2(5.0f, 40.0f);
	[Export] public float ZoomSmoothSpeed = 8f;
	[Export] public float FocusSmoothSpeed = 6f;

	[Export] public float RotateSensitivity = 0.25f;
	// 相机仰角（从水平面向上）范围；避免翻到地面以下。
	[Export] public Vector2 PitchLimit = new Vector2(15.0f, 80.0f);
	[Export] public float TopDownPitch = 75f;
	[Export] public float TopDownDistance = 16f;

	private Camera3D _camera;
	private bool _isDragging;
	private bool _isRotating;
	private Vector2 _lastMousePosition;
	private float _targetDistance = 20f;
	private float _currentDistance = 20f;
	private float _pitchDegrees = 45f;
	private float _targetPitch = 45f;
	private float _yawDegrees;
	private Vector3 _targetFocusPosition;
	private bool _hasFocusTarget;
	private bool _applyZoomAnchor;
	private Vector2 _zoomAnchorScreen;

	public override void _Ready()
	{
		_camera = GetNode<Camera3D>("Camera3D");
		if (_camera == null) return;
		_currentDistance = _camera.Position.Length();
		_targetDistance = _currentDistance;
		_pitchDegrees = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(
			_camera.Position.Y / Mathf.Max(0.01f, _currentDistance), -1f, 1f)));
		_targetPitch = _pitchDegrees;
		_yawDegrees = RotationDegrees.Y;
		_targetFocusPosition = Position;
		_hasFocusTarget = true;
		ApplyOrbit();

		var bus = GetNodeOrNull<EventBus>("../EventBus");
		if (bus != null)
		{
			bus.CameraFocusRequested += FocusOn;
			bus.CameraFocusBetweenRequested += FocusBetween;
			bus.CameraTopDownRequested += FocusTopDown;
		}
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta;
		Vector3 inputDir = Vector3.Zero;

		if (Input.IsKeyPressed(Key.D)) inputDir.X += 1;
		if (Input.IsKeyPressed(Key.A)) inputDir.X -= 1;
		if (Input.IsKeyPressed(Key.S)) inputDir.Z += 1;
		if (Input.IsKeyPressed(Key.W)) inputDir.Z -= 1;

		if (inputDir != Vector3.Zero)
		{
			inputDir = inputDir.Normalized();
			Translate(inputDir * MoveSpeed * dt);
		}

		if (_camera == null) return;

		// 平滑移动焦点与仰角，给运镜反馈。
		if (_hasFocusTarget)
		{
			Position = Position.Lerp(_targetFocusPosition, 1f - Mathf.Exp(-FocusSmoothSpeed * dt));
			_pitchDegrees = Mathf.Lerp(_pitchDegrees, _targetPitch, 1f - Mathf.Exp(-FocusSmoothSpeed * dt));
		}

		bool zooming = !Mathf.IsEqualApprox(_currentDistance, _targetDistance);
		if (zooming)
		{
			if (_applyZoomAnchor)
			{
				// 手动滚轮缩放：保持鼠标射线与地面的交点在屏幕上不动。
				_zoomAnchorScreen = GetViewport().GetMousePosition();
				Vector3 groundBefore = GroundPointAt(_zoomAnchorScreen);
				_currentDistance = Mathf.Lerp(_currentDistance, _targetDistance,
					1f - Mathf.Exp(-ZoomSmoothSpeed * dt));
				ApplyOrbit();
				Vector3 groundAfter = GroundPointAt(_zoomAnchorScreen);
				Vector3 anchorDelta = groundBefore - groundAfter;
				anchorDelta.Y = 0f;
				Position += anchorDelta;
				return;
			}

			_currentDistance = Mathf.Lerp(_currentDistance, _targetDistance,
				1f - Mathf.Exp(-ZoomSmoothSpeed * dt));
		}

		ApplyOrbit();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				_isDragging = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
				// 玩家手动平移后不再被自动焦点拉回。
				if (mouseButton.Pressed) _hasFocusTarget = false;
			}

			if (mouseButton.ButtonIndex == MouseButton.Middle)
			{
				_isRotating = mouseButton.Pressed;
				_lastMousePosition = mouseButton.Position;
			}

			if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelUp)
			{
				_targetDistance = Mathf.Clamp(_targetDistance - ZoomSpeed, ZoomRange.X, ZoomRange.Y);
				_applyZoomAnchor = true;
			}
			if (mouseButton.Pressed && mouseButton.ButtonIndex == MouseButton.WheelDown)
			{
				_targetDistance = Mathf.Clamp(_targetDistance + ZoomSpeed, ZoomRange.X, ZoomRange.Y);
				_applyZoomAnchor = true;
			}
		}

		if (@event is InputEventMouseMotion mouseMotion)
		{
			Vector2 deltaMouse = mouseMotion.Position - _lastMousePosition;

			if (_isDragging)
			{
				if (_camera == null) return;
				float dragSensitivity = 0.0015f * _currentDistance;
				Vector3 dragMove = new Vector3(-deltaMouse.X * dragSensitivity, 0f, -deltaMouse.Y * dragSensitivity);
				Translate(dragMove);
			}

			if (_isRotating)
			{
				_yawDegrees -= mouseMotion.Relative.X * RotateSensitivity;
				_pitchDegrees = Mathf.Clamp(
					_pitchDegrees - mouseMotion.Relative.Y * RotateSensitivity,
					PitchLimit.X, PitchLimit.Y);
				_targetPitch = _pitchDegrees;
				ApplyOrbit();
			}

			_lastMousePosition = mouseMotion.Position;
		}
	}

	/// <summary>全局焦点接口：把镜头平滑移动到 worldPos，目标距离与仰角由调用方给出。</summary>
	public void FocusOn(Vector3 worldPos, float distance, float pitchDegrees)
	{
		_targetFocusPosition = new Vector3(worldPos.X, 0f, worldPos.Z);
		_targetDistance = Mathf.Clamp(distance, ZoomRange.X, ZoomRange.Y);
		_targetPitch = Mathf.Clamp(pitchDegrees, PitchLimit.X, PitchLimit.Y);
		_hasFocusTarget = true;
		_applyZoomAnchor = false;
	}

	/// <summary>焦点放在两点中点，距离按两点间距自适应（用于船-目标/船-预测格）。</summary>
	public void FocusBetween(Vector3 from, Vector3 to)
	{
		Vector3 mid = (from + to) * 0.5f;
		float span = (from - to).Length();
		float distance = Mathf.Clamp(span * 1.8f + 10f, ZoomRange.X, ZoomRange.Y);
		FocusOn(mid, distance, 45f);
	}

	/// <summary>以 worldPos 为中心俯视（方向调整/单位激活用）。</summary>
	public void FocusTopDown(Vector3 worldPos) => FocusOn(worldPos, TopDownDistance, TopDownPitch);

	/// <summary>按当前距离/仰角/偏航把相机摆到环绕焦点（rig 原点）的位置。</summary>
	private void ApplyOrbit()
	{
		if (_camera == null) return;
		RotationDegrees = new Vector3(0f, _yawDegrees, 0f);
		float pitchRad = Mathf.DegToRad(_pitchDegrees);
		_camera.Position = new Vector3(
			0f,
			_currentDistance * Mathf.Sin(pitchRad),
			_currentDistance * Mathf.Cos(pitchRad));
		_camera.RotationDegrees = new Vector3(-_pitchDegrees, 0f, 0f);
	}

	/// <summary>把屏幕坐标投射到 y=0 地面平面，返回地面交点。</summary>
	private Vector3 GroundPointAt(Vector2 screenPos)
	{
		Vector3 origin = _camera.ProjectRayOrigin(screenPos);
		Vector3 dir = _camera.ProjectRayNormal(screenPos);
		float t = -origin.Y / Mathf.Max(0.001f, dir.Y);
		if (t < 0f) t = 0f;
		return origin + dir * t;
	}
}
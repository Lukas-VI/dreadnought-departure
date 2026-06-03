using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class TacticalCameraController : Node3D
{

    // 强行把相机暴露给编辑器
    [Export] public Camera3D TacticalCamera;
    [Export] public float MoveSpeed = 15.0f;
    [Export] public float ZoomSpeed = 2.0f;
    [Export] public Vector2 ZoomRange = new Vector2(5.0f, 40.0f);
    
    [Export] public float RotateSensitivity = 0.25f; // 视角旋转灵敏度
    [Export] public Vector2 PitchLimit = new Vector2(-75.0f, -15.0f); // 限制低头/抬头角度，防止翻车

    

    private Camera3D _camera;
    private bool _isDragging = false;       // 右键平移开关
    private bool _isRotating = false;       // 中键旋转开关
    private Vector2 _lastMousePosition;

    public override void _Ready()
    {
        // 如果是用代码抓取，用断言锁死它
        _camera = GetNode<Camera3D>("Camera3D");
        
        // 调试期绝杀：如果路径错了，这里会立刻弹窗中断，绝不留隐患到后面
        System.Diagnostics.Debug.Assert(_camera != null, "相机路径配置错误喵，别想玩了喵！");
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
            // 基于悬臂当前朝向进行丝滑平移
            Translate(inputDir * MoveSpeed * dt);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            // 【右键】按下：开始抓取平移沙盘
            if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                _isDragging = mouseButton.Pressed;
                _lastMousePosition = mouseButton.Position;
            }

            // 【中键（滚轮按下）】：开始控制俯仰/旋转
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _isRotating = mouseButton.Pressed;
                _lastMousePosition = mouseButton.Position;
                
                // 细节体验：旋转时把鼠标指针藏起来，体验更硬核
                Input.MouseMode = _isRotating ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }

            // 【滚轮】：斜向推拉缩放
            if (_camera != null)
            {
                // 拿到相机当前镜头面朝方向的绝对 Z 轴向量（局部空间方向）
                Vector3 forwardVector = _camera.Transform.Basis.Z;

                if (mouseButton.ButtonIndex == MouseButton.WheelUp && mouseButton.Pressed)
                {
                    // 放大：顺着镜头正前方突进
                    Vector3 nextPos = _camera.Position - forwardVector * ZoomSpeed;
                    
                    // 安全锁：防止相机穿透到父节点（悬臂中心）的另一侧去
                    if (nextPos.Z > ZoomRange.X) 
                    {
                        _camera.Position = nextPos;
                    }
                }
                
                if (mouseButton.ButtonIndex == MouseButton.WheelDown && mouseButton.Pressed)
                {
                    // 缩小：顺着镜头正后方拉远
                    Vector3 nextPos = _camera.Position + forwardVector * ZoomSpeed;
                    
                    // 安全锁：防止拉得太远飞出宇宙
                    if (nextPos.Z < ZoomRange.Y) 
                    {
                        _camera.Position = nextPos;
                    }
                }
            }
        }

        if (@event is InputEventMouseMotion mouseMotion)
        {
            Vector2 deltaMouse = mouseMotion.Position - _lastMousePosition;
            
            // 如果是在 Captured 模式下，直接使用引擎自带的 Relative 相对位移（更精准）
            if (Input.MouseMode == Input.MouseModeEnum.Captured)
            {
                deltaMouse = mouseMotion.Relative;
            }

            // 1. 处理右键沙盘平移
            if (_isDragging)
            {
                float dragSensitivity = 0.0015f * _camera.Position.Z; 
                Vector3 dragMove = new Vector3(-deltaMouse.X * dragSensitivity, 0f, -deltaMouse.Y * dragSensitivity);
                Translate(dragMove);
            }

            // 2. 核心绝杀：处理中键鼠标上下晃动的【俯仰角控制】
            if (_isRotating && _camera != null)
            {
                // 鼠标上下移动 (deltaMouse.Y) 改变相机自身的 X 轴旋转（低头/抬头）
                Vector3 camRot = _camera.RotationDegrees;
                camRot.X -= deltaMouse.Y * RotateSensitivity;
                
                // 必须限制死俯仰角！防止相机转到地底下或者肚皮翻天
                camRot.X = Mathf.Clamp(camRot.X, PitchLimit.X, PitchLimit.Y);
                _camera.RotationDegrees = camRot;

                // 顺便：鼠标左右移动 (deltaMouse.X) 可以让整个悬臂绕着战场中心旋转（环视战场）
                Vector3 boomRot = RotationDegrees;
                boomRot.Y -= deltaMouse.X * RotateSensitivity;
                RotationDegrees = boomRot;
            }

            _lastMousePosition = mouseMotion.Position;
        }
    }
}
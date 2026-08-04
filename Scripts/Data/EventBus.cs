using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 全局事件总线。
/// 所有系统间通信通过此处定义的 Signal 解耦——UI、输入、AI、战斗结算、
/// 阶段管理各子系统不直接引用彼此。
/// 静态 Instance 属性供非 Node 的静态工具类访问。
/// </summary>
public partial class EventBus : Node
{
	public static EventBus Instance { get; private set; }

	[Signal] public delegate void HexClickedEventHandler(Vector2I hex);
	[Signal] public delegate void TurnStartedEventHandler(string side);
	[Signal] public delegate void PlayerSideFinishedEventHandler();
	[Signal] public delegate void EnemySideFinishedEventHandler();
	[Signal] public delegate void BattleEndedEventHandler(string result, string detail);
	[Signal] public delegate void EndTurnClickedEventHandler();
	[Signal] public delegate void CombatResultEventHandler(string desc);
	[Signal] public delegate void ActionSelectedEventHandler(string actionId);
	[Signal] public delegate void LogMessageEventHandler(string message);
	[Signal] public delegate void ShipInfoRequestedEventHandler(ShipComponent ship);
	[Signal] public delegate void ShipStatusChangedEventHandler(ShipComponent ship);
	[Signal] public delegate void ShipSelectionChangedEventHandler(ShipComponent ship, bool selected);
	[Signal] public delegate void OverlayDrawRequestedEventHandler(
		Vector2I center, int move, int attack, int direction, int arcMask, int state);
	[Signal] public delegate void OverlayArcDrawRequestedEventHandler(Vector2I center, int direction, int range);
	[Signal] public delegate void OverlayClearRequestedEventHandler();
	[Signal] public delegate void MoveTargetHighlightedEventHandler(Vector2I target, int direction);
	[Signal] public delegate void DirectionOverlayRequestedEventHandler(Vector2I hex, int direction);
	[Signal] public delegate void DirectionOverlayClearRequestedEventHandler();

	// -- 相机焦点（全局运镜接口，剧情/演绎可直接使用）--
	[Signal] public delegate void CameraFocusRequestedEventHandler(Vector3 worldPos, float distance, float pitchDegrees);
	[Signal] public delegate void CameraFocusBetweenRequestedEventHandler(Vector3 from, Vector3 to);
	[Signal] public delegate void CameraTopDownRequestedEventHandler(Vector3 worldPos);

	// -- 阶段管理 --
	[Signal] public delegate void PhaseChangedEventHandler(string phaseName, int phaseIndex, int turnNumber);
	[Signal] public delegate void AdvancePhaseClickedEventHandler();
	[Signal] public delegate void CpUpdatedEventHandler(int current, int max);
	[Signal] public delegate void CommandStateUpdatedEventHandler(
		int playerCommand, int playerCP, int playerMaxCP,
		int enemyCommand, int enemyCP, int enemyMaxCP,
		int playerScore, int enemyScore);
	[Signal] public delegate void PhaseTimerUpdatedEventHandler(float remaining, float total);

	public override void _Ready()
	{
		base._Ready();
		Instance = this;
	}

	/// <summary>发送日志消息（线程/协程安全）。</summary>
	public void EmitLog(string message)
	{
		if (GodotObject.IsInstanceValid(this))
			EmitSignal(SignalName.LogMessage, message);
	}

	/// <summary>全局运镜：聚焦 worldPos，距离与仰角由调用方给出。</summary>
	public void RequestCameraFocus(Vector3 worldPos, float distance, float pitchDegrees)
	{
		if (GodotObject.IsInstanceValid(this))
			EmitSignal(SignalName.CameraFocusRequested, worldPos, distance, pitchDegrees);
	}

	/// <summary>全局运镜：聚焦两点中点（船-目标/船-预测格）。</summary>
	public void RequestCameraFocusBetween(Vector3 from, Vector3 to)
	{
		if (GodotObject.IsInstanceValid(this))
			EmitSignal(SignalName.CameraFocusBetweenRequested, from, to);
	}

	/// <summary>全局运镜：以 worldPos 为中心俯视。</summary>
	public void RequestCameraTopDown(Vector3 worldPos)
	{
		if (GodotObject.IsInstanceValid(this))
			EmitSignal(SignalName.CameraTopDownRequested, worldPos);
	}
}

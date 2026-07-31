using Godot;
using System;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战斗 HUD 底部信息栏（继承 Label）。
/// 通过 EventBus 的 LogMessage / ShipInfoRequested 信号驱动。
/// </summary>
public partial class BattleHudBroker : Label
{
	/// <summary>显示一条日志文本。</summary>
	public void DisplayConsoleLog(string message)
	{
		Text = message;
	}

	/// <summary>显示当前选中舰船的基本信息。</summary>
	public void DisplayShipSelected(ShipComponent ship)
	{
		if (ship != null)
			Text = "【已锁定】舰名: " + ship.ShipName + " | 装甲: " + ship.CurrentHp + "/" + ship.MaxHp;
	}
}

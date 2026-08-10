using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战斗 HUD 底部信息栏（继承 Label）。
/// 通过 EventBus 的 LogMessage / ShipInfoRequested 信号驱动。
/// </summary>
public partial class BattleHudBroker : Label
{
	private const int MaxLines = 25;
	private readonly List<string> _history = new();

	/// <summary>显示一条日志文本。</summary>
	public void DisplayConsoleLog(string message)
	{
		AddLogLine(message);
	}

	/// <summary>显示当前选中舰船的基本信息。</summary>
	public void DisplayShipSelected(ShipComponent ship)
	{
		if (ship != null)
			AddLogLine("【已选中】" + ship.ShipName + " | 装甲: " + ship.CurrentHp + "/" + ship.MaxHp
				+ " | 弹药: " + ship.MainAmmo);
	}

	private void AddLogLine(string message)
	{
		_history.Add(message);
		while (_history.Count > MaxLines)
			_history.RemoveAt(0);
		Text = string.Join("\n", _history);
	}
}

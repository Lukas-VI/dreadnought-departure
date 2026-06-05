using Godot;
using System;

namespace DreadnoughtDeparture.Core;

// 直接继承 Label——脚本本身挂在 InfoLabel 上，this 就是 Label
public partial class BattleHudBroker : Label
{
	public void DisplayConsoleLog(string message)
	{
		Text = message;
	}

	public void DisplayShipSelected(ShipComponent ship)
	{
		if (ship != null)
			Text = $"【就绪】\n舰名: {ship.ShipName} | 装甲: {ship.CurrentHp}/{ship.MaxHp}";
	}
}

using Godot;
using System;

namespace DreadnoughtDeparture.Core;

public partial class BattleHudBroker : Label
{
	public void DisplayConsoleLog(string message)
	{
		Text = message;
	}

	public void DisplayShipSelected(ShipComponent ship)
	{
		if (ship != null)
			Text = $"【指挥链已锁定制导】\n舰名: {ship.ShipName} | 装甲: {ship.CurrentHp}/{ship.MaxHp}";
	}
}

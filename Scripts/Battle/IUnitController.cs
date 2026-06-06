using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public interface IUnitController
{
    void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
                  MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
                  Action onComplete);
}


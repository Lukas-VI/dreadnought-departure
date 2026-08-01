using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 单位控制接口——PlayerController（人类玩家）与 AIController（电脑）统一实现此接口。
/// GameplayDirector 在每一阶段通过此接口分派操作，实现人机回合逻辑的解耦。
/// </summary>
public interface IUnitController
{
    void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
                  MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
                  BattlePhase phase, Action onComplete);
}

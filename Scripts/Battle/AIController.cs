using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// AI 控制器——实现 IUnitController 接口。
/// 每回合对己方存活单位执行简单的"找最近敌 → 在射程内则攻击，否则机动靠近"策略。
/// 供 TurnManager 在 AI 阶段调用。
/// </summary>
public partial class AIController : Node, IUnitController
{
    private Random _rng = new();

    /// <summary>AI 回合逻辑：每个存活单位寻找最近敌人，射程内攻击，射程外机动靠近。</summary>
    public void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
                         MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
                         Action onComplete)
    {
        foreach (var ship in myUnits)
        {
            if (ship.CurrentHp <= 0) continue;

            // 找最近敌方
            var target = enemyUnits.Where(e => e.CurrentHp > 0)
                .OrderBy(e => BattleRulesEvaluator.GetHexDistance(ship.HexCoords, e.HexCoords))
                .FirstOrDefault();
            if (target == null) break;

            int dist = BattleRulesEvaluator.GetHexDistance(ship.HexCoords, target.HexCoords);
            if (dist <= ship.AttackRange)
            {
                target.TakeDamage(ship.AttackPower);
                hud?.DisplayConsoleLog("💥 敌方 " + ship.ShipName + " 开火!" + target.ShipName + " 受损 " + ship.AttackPower + " 点!");
            }
            else
            {
                // 朝目标走一步（简单格子逼近）
                Vector2I delta = target.HexCoords - ship.HexCoords;
                int dq = Math.Sign(delta.X), dr = Math.Sign(delta.Y);
                Vector2I[] dirs = { new(dq, dr), new(dq - dr, dr), new(dq, dr - dq) };
                foreach (var dir in dirs)
                {
                    Vector2I next = ship.HexCoords + dir;
                    if (BattleRulesEvaluator.GetHexDistance(ship.HexCoords, next) == 1)
                    {
                        ship.MoveToHex(map, next);
                        hud?.DisplayConsoleLog("⚓ 敌方 " + ship.ShipName + " 向 " + next + " 机动!");
                        break;
                    }
                }
            }
        }
        onComplete?.Invoke();
    }
}

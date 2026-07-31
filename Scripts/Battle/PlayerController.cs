using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 人类玩家控制器——实现 IUnitController 接口。
/// GameplayDirector 在每个可交互阶段调用 BeginPhaseAction 激活本控制器；
/// 玩家通过点击水面选择单位、操作轮盘菜单发出指令、点击目标格执行。
/// 涉及速度调整、转向、分段机动、编队跟随、火炮射击等兵棋规则的交互逻辑。
/// </summary>
public partial class PlayerController : Node, IUnitController
{
	private GameplayDirector _director;
	private MapGenerator _map;
	private GridOverlayController _overlay;
	private List<ShipComponent> _myUnits, _enemyUnits;
	private ShipComponent _selected;
	private string _pendingAction;
	private int _actionsLeft;

	public override void _Ready()
	{
		GetNode<EventBus>("../EventBus").HexClicked += OnHexClicked;
		GetNode<EventBus>("../EventBus").ActionSelected += OnWheelAction;
	}

/// <summary>
/// GameplayDirector 在每个可交互阶段开始时调用
/// </summary>
	public void BeginPhaseAction(GameplayDirector director,
		List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
		MapGenerator map, GridOverlayController overlay)
	{
		_director = director;
		_map = map; _overlay = overlay;
		_myUnits = myUnits; _enemyUnits = enemyUnits;
		_selected = null; _pendingAction = null;

		int phase = director.CurrentMovePhase;
		if (phase > 0)
		{
			_actionsLeft = myUnits
				.Where(s => GodotObject.IsInstanceValid(s) && s.CurrentHp > 0)
				.Sum(s => MoveRulesEvaluator.MovementForPhase(
					s.CurrentSpeed, phase, director.IsOddTurn));
		}
		else
		{
			_actionsLeft = myUnits.Count(s =>
				GodotObject.IsInstanceValid(s) && s.CurrentHp > 0);
		}

		if (_actionsLeft <= 0) _actionsLeft = 1;
		GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
			$"⚓ 本阶段 {_actionsLeft} 次行动。");
	}

	// 兼容 IUnitController（AI 用）
/// <summary>IUnitController 接口实现——占位，AI 回合不走此路径。</summary>
	public void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
		MapGenerator map, GridOverlayController overlay, BattleHudBroker hud, Action onComplete)
	{
		// 占位：AI 回合仍走 TurnManager
		onComplete?.Invoke();
	}

	private int StepsForShip(ShipComponent ship)
	{
		int phase = _director?.CurrentMovePhase ?? 0;
		if (phase <= 0) return 1;
		return MoveRulesEvaluator.MovementForPhase(
			ship.CurrentSpeed, phase, _director.IsOddTurn);
	}

	// ═══════════════════════════════════════════
/// <summary>处理轮盘菜单指令：变速/转向即时执行，移动/攻击进入 _pendingAction 等待点击目标。</summary>
	private void OnWheelAction(string actionId)
	{
		if (actionId == "_show_wheel") return;
		if (_selected == null) return;

		var phase = _director?.CurrentPhase ?? BattlePhase.EndTurn;

		if (actionId == "speed_up" || actionId == "speed_down")
		{
			if (phase != BattlePhase.SpeedAdjust && phase != BattlePhase.MovePhase1 &&
				phase != BattlePhase.MovePhase2 && phase != BattlePhase.MovePhase3)
			{
				RejectAction("❌ 仅速度调整或移动阶段可变更航速");
				return;
			}
			ExecuteSpeedAdjust(actionId == "speed_up" ? +1 : -1);
			return;
		}

		bool canMove = phase is BattlePhase.MovePhase1 or BattlePhase.MovePhase2 or BattlePhase.MovePhase3;
		bool canAttack = phase is BattlePhase.Gunfire;
		bool canTurn = canMove;

		if (actionId == "move" && !canMove)
		{ RejectAction("❌ 当前阶段不可机动"); return; }
		if (actionId == "attack" && !canAttack)
		{ RejectAction("❌ 当前阶段不可射击"); return; }
		if ((actionId == "turn_left" || actionId == "turn_right") && !canTurn)
		{ RejectAction("❌ 当前阶段不可转向"); return; }

		_pendingAction = actionId;

		if (actionId == "move")
		{
			int steps = StepsForShip(_selected);
			if (steps <= 0)
			{ _pendingAction = null; RejectAction("❌ 当前航速无机动能力"); return; }
			Vector2I off = HexDirectionUtility.Offset(_selected.Direction);
			Vector2I target = _selected.HexCoords + off * steps;
			GetNode<EventBus>("../EventBus").EmitSignal("MoveTargetHighlighted", target);
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
				$"🎯 航向 {_selected.Direction} × {steps} 格 → {target}");
		}
		else if (actionId == "attack")
		{
			GetNode<EventBus>("../EventBus").EmitSignal("OverlayDrawRequested",
				_selected.HexCoords, 0, _selected.AttackRange, (int)UnitTacticalState.Idle);
		}
		else { ExecuteInstantAction(actionId); }
	}

/// <summary>执行航速增减：检查变速限幅与 CP 消耗，更新 ship.CurrentSpeed。</summary>
	private void ExecuteSpeedAdjust(int delta)
	{
		int old = _selected.CurrentSpeed;
		int max = _selected.Data != null ? _selected.Data.MaxSpeed : 5;
		int wish = old + delta;
		if (!SpeedTable.CanAdjustSpeed(old, wish, max))
		{ RejectAction($"❌ 航速调整超限（当前 {old}）"); return; }

		int cpCost = Math.Abs(delta);
		if (cpCost > 0 && _director != null && !_director.TryConsumeCP(cpCost))
		{ RejectAction($"❌ CP 不足（需要 {cpCost}，剩余 {_director.CurrentCP}）"); return; }

		_selected.CurrentSpeed = wish;
		GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", $"⚙ 航速 {old} → {wish}（消耗 {cpCost} CP）");
		EndAction();
	}

/// <summary>点击六角格：无选中单位则尝试选中；有 _pendingAction 则执行。</summary>
	private void OnHexClicked(Vector2I hex)
	{
		if (_myUnits == null) return;
		if (_selected == null)
		{
			var ship = _myUnits.Find(s => s.HexCoords == hex && GodotObject.IsInstanceValid(s));
			if (ship != null && ship.CurrentHp > 0)
			{
				_selected = ship;
				_selected.ShowSelected(true);
				GetNode<EventBus>("../EventBus").EmitSignal("ShipInfoRequested", ship);
				GetNode<EventBus>("../EventBus").EmitSignal("ActionSelected", "_show_wheel");
			}
		}
		else if (_pendingAction != null) { ExecutePendingAction(hex); }
	}

/// <summary>执行移动或攻击指令：校验惯性规则/射程/编队跟随，更新单位状态。</summary>
	private void ExecutePendingAction(Vector2I hex)
	{
		if (_enemyUnits == null) return;
		int d = BattleRulesEvaluator.GetHexDistance(_selected.HexCoords, hex);

		if (_pendingAction == "attack")
		{
			var target = _enemyUnits.Find(s => s.HexCoords == hex && GodotObject.IsInstanceValid(s));
			if (target != null && d <= _selected.AttackRange)
			{
				(bool hit, int dmg, string desc) = CombatRulesEvaluator.FireEx(_selected, target, d);
				GetNode<EventBus>("../EventBus").EmitSignal("CombatResult", desc);
				if (hit) EndAction();
			}
			else RejectAction("❌ 目标无效或不在射程内！");
		}
		else if (_pendingAction == "move")
		{
			if (!MoveRulesEvaluator.IsInForwardArc(_selected.HexCoords, hex, _selected.Direction))
			{ RejectAction("❌ 仅可朝前方 120° 扇面机动"); return; }

			int steps = StepsForShip(_selected);
			Vector2I off = HexDirectionUtility.Offset(_selected.Direction);
			Vector2I expected = _selected.HexCoords + off * steps;

			var formation = MoveRulesEvaluator.DetectLineAhead(_selected, _myUnits);
			bool isFollower = formation.IsInFormation && formation.LeadShip != _selected;

			if (hex == expected && d == steps)
			{
				_selected.MoveToHex(_map, hex);
				GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
					isFollower ? $"⚓ 跟随首舰机动至 {hex}（编队自动转向）" : $"⚓ 舰队机动至 {hex}（航向 {_selected.Direction}）");
				EndAction();
			}
			else RejectAction($"❌ 惯性规则：仅可抵达 {expected}");
		}
	}

	private void ExecuteInstantAction(string id)
	{
		switch (id)
		{
			case "turn_left":
			{
				var nd = HexDirectionUtility.TurnLeft(_selected.Direction);
				int cost = MoveRulesEvaluator.TurnCostToFace(_selected.Direction, nd);
				if (_director != null && !_director.TryConsumeCP(cost))
				{ _pendingAction = null; RejectAction($"❌ 转向需要 {cost} CP"); return; }
				_selected.Direction = nd;
				GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", $"↩ 左转 60°→ 航向 {nd}（消耗 {cost} CP）");
				break;
			}
			case "turn_right":
			{
				var nd = HexDirectionUtility.TurnRight(_selected.Direction);
				int cost = MoveRulesEvaluator.TurnCostToFace(_selected.Direction, nd);
				if (_director != null && !_director.TryConsumeCP(cost))
				{ _pendingAction = null; RejectAction($"❌ 转向需要 {cost} CP"); return; }
				_selected.Direction = nd;
				GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", $"↪ 右转 60°→ 航向 {nd}（消耗 {cost} CP）");
				break;
			}
			default:
				GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", id);
				break;
		}
		EndAction();
	}

/// <summary>拒绝操作：输出原因日志，若单位仍选中则重新弹出轮盘菜单。</summary>
	private void RejectAction(string message)
	{
		var bus = GetNode<EventBus>("../EventBus");
		bus.EmitSignal("LogMessage", message);
		if (_selected != null)
			bus.EmitSignal("ActionSelected", "_show_wheel");
	}

/// <summary>结束当前行动：递减行动力计数，归零则冻结选中，否则重新弹出轮盘。</summary>
	private void EndAction()
	{
		GetNode<EventBus>("../EventBus").EmitSignal("OverlayClearRequested");
		_pendingAction = null;
		_actionsLeft--;
		if (_actionsLeft <= 0)
		{
			if (_selected != null) _selected.ShowSelected(false);
			_selected = null;
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", "⏸ 本阶段行动力耗尽，请推进阶段");
		}
		else
		{
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", $"⚓ 还有 {_actionsLeft} 次行动。");
			if (_selected != null)
				GetNode<EventBus>("../EventBus").EmitSignal("ActionSelected", "_show_wheel");
		}
	}
}

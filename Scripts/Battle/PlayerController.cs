using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 人类玩家控制器——按阶段逐船驱动操作队列。
/// GameplayDirector 在每个可交互阶段调用 BeginPhaseAction，控制器按存活舰船顺序
/// 自动激活当前船并弹出底部操作菜单；船完成行动后自动轮到下一艘。
/// 机动在三个移动阶段各自推进时按航向/速度自动执行，玩家只负责变速、转向与炮击选目标。
/// 相机反馈：激活/转向用俯视，变速用“船-推算到达格”中点，炮击确认目标后
/// 用“船-敌舰”中点，保证操作时菜单与战场焦点可见。
/// </summary>
public partial class PlayerController : Node, IUnitController
{
	private GameplayDirector _director;
	private LevelDataManager _data;
	private MapGenerator _map;
	private GridOverlayController _overlay;
	private List<ShipComponent> _myUnits;
	private List<ShipComponent> _enemyUnits;
	private ShipComponent _selected;
	private string _pendingAction;
	private Queue<ShipComponent> _pendingShips = new();
	private readonly Dictionary<Vector2I, ShipComponent> _lastStackSelection = new();

	public override void _Ready()
	{
		_data = GetNode<LevelDataManager>("../LevelDataManager");
		GetNode<EventBus>("../EventBus").HexClicked += OnHexClicked;
		GetNode<EventBus>("../EventBus").ActionSelected += OnActionSelected;
	}

	/// <summary>阶段开始入口：重建逐船队列并激活第一艘可操作船。</summary>
	public void BeginPhaseAction(GameplayDirector director,
		List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
		MapGenerator map, GridOverlayController overlay)
	{
		_director = director;
		_map = map;
		_overlay = overlay;
		_myUnits = myUnits;
		_enemyUnits = enemyUnits;
		_pendingAction = null;
		_selected = null;

		foreach (var ship in _myUnits)
			if (GodotObject.IsInstanceValid(ship))
				ship.ShowSelected(false);

		_pendingShips = new Queue<ShipComponent>(myUnits.Where(s =>
			GodotObject.IsInstanceValid(s) && s.CurrentHp > 0));

		if (_pendingShips.Count == 0)
		{
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
				"舰船均已下达指令，请推进阶段");
			NotifyPlayerFinished();
			return;
		}

		GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
			$"⚓ 本阶段 {_pendingShips.Count} 艘舰船待操作。");
		SelectNextShip();
	}

	// 兼容 IUnitController（AI 用）
	/// <summary>IUnitController 接口实现——占位，AI 回合尚未接入阶段管线。</summary>
	public void TakeTurn(List<ShipComponent> myUnits, List<ShipComponent> enemyUnits,
		MapGenerator map, GridOverlayController overlay, BattleHudBroker hud,
		BattlePhase phase, Action onComplete)
	{
		// 玩家侧操作由 BeginPhaseAction 逐船驱动，不经过该接口。
		onComplete?.Invoke();
	}

	/// <summary>从队列取出下一艘存活船，激活选中态并弹出底部操作菜单。</summary>
	private void SelectNextShip()
	{
		var bus = GetNode<EventBus>("../EventBus");
		while (_pendingShips.Count > 0)
		{
			var ship = _pendingShips.Dequeue();
			if (!GodotObject.IsInstanceValid(ship) || ship.CurrentHp <= 0)
				continue;
			// 单纵阵后续舰自动跟随，不占用操作队列；玩家仍可手动点击覆盖或离队。
			if (ship.FormationLead != null && !ReferenceEquals(ship.FormationLead, ship))
				continue;
			// 已写入待命指令的船跳过，避免重复操作。
			if (ship.PendingSpeed >= 0 || ship.PendingDirection.HasValue)
				continue;

			SelectShip(ship);
			return;
		}

		_selected = null;
		bus.EmitSignal("LogMessage", "舰船均已下达指令，请推进阶段");
		FocusPlayerFleet();
	}

	/// <summary>选中船：点亮标记、更新 HUD、弹出操作菜单并俯视运镜到船体中心。</summary>
	private void SelectShip(ShipComponent ship)
	{
		if (_selected != null && !ReferenceEquals(_selected, ship))
			_selected.ShowSelected(false);
		_selected = ship;
		_selected.ShowSelected(true);
		var bus = GetNode<EventBus>("../EventBus");
		bus.EmitSignal("OverlayClearRequested");
		PreviewNextArrival(ship);
		bus.EmitSignal("ShipInfoRequested", ship);
		bus.EmitSignal("ActionSelected", "_show_menu");
		bus.EmitSignal("CameraTopDownRequested", ship.GlobalPosition);
		bus.EmitSignal("LogMessage", $"⚓ 轮到 {ship.ShipName}（剩余 {_pendingShips.Count} 艘）");
	}

	/// <summary>处理操作菜单指令：变速/转向即时执行，炮击进入 _pendingAction 等待点击目标，待命直接跳过。</summary>
	private void OnActionSelected(string actionId)
	{
		if (actionId == "_show_menu") return;
		if (_selected == null) return;

		if (actionId == "skip")
		{
			_selected.ClearPendingCommands();
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", $"⏭ {_selected.ShipName} 待命");
			EndAction();
			return;
		}

		var phase = _director?.CurrentPhase ?? BattlePhase.EndTurn;
		if (actionId != "_show_menu" && actionId != "skip")
		{
			GetNode<EventBus>("../EventBus").EmitSignal("PlayerActionPerformed", actionId);
		}

		if (actionId == "radar")
		{
			if (string.IsNullOrEmpty(_selected.Data?.RadarType)
				|| _selected.DamageState is not (DamageState.Intact or DamageState.Light))
			{
				RejectAction("❌ 雷达不可用（未装备或已中破）");
				return;
			}
			_selected.PendingRadarActive = !_selected.PendingRadarActive;
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
				_selected.PendingRadarActive
					? $"📡 {_selected.ShipName} 已激活雷达，可越过基本视野交战"
					: $"📡 {_selected.ShipName} 已关闭雷达");
			// 技能只改状态，不消耗该船本阶段行动；重新弹出菜单供继续选炮击或待命。
			GetNode<EventBus>("../EventBus").EmitSignal("ActionSelected", "_show_menu");
			return;
		}

		if (actionId == "speed_up" || actionId == "speed_down")
		{
			if (phase != BattlePhase.SpeedAdjust)
			{
				RejectAction("❌ 仅速度调整阶段可变更航速");
				return;
			}
			ExecuteSpeedAdjust(actionId == "speed_up" ? +1 : -1);
			return;
		}

		bool canTurn = phase is BattlePhase.SpeedAdjust or BattlePhase.MovePhase1
			or BattlePhase.MovePhase2 or BattlePhase.MovePhase3;
		bool canAttack = phase == BattlePhase.Gunfire;
		bool canTorpedo = phase == BattlePhase.Torpedo;

		if (actionId == "attack" && _selected.MainAmmo <= 0)
		{ RejectAction("❌ 主炮弹药耗尽！"); return; }
		if ((actionId == "turn_left" || actionId == "turn_right") && !canTurn)
		{ RejectAction("❌ 当前阶段不可转向"); return; }
		if (actionId == "attack" && !canAttack)
		{ RejectAction("❌ 当前阶段不可射击"); return; }
		bool isTorpedo = TryParseTorpedoAction(actionId, out int torpedoSide, out int torpedoBranch);
		if (isTorpedo && !canTorpedo)
		{ RejectAction("❌ 当前阶段不可雷击"); return; }

		if (isTorpedo)
		{
			ExecuteTorpedoPending(torpedoSide, torpedoBranch);
			return;
		}

		_pendingAction = actionId;

		if (actionId == "attack")
		{
			GetNode<EventBus>("../EventBus").EmitSignal("OverlayDrawRequested",
				_selected.HexCoords, 0, _selected.AttackRange,
				(int)_selected.Direction, AttackArcMask(_selected),
				(int)UnitTacticalState.Idle);
			FocusOnAttackRange();
		}
		else
		{
			ExecuteInstantAction(actionId);
		}
	}

	private void ExecuteTorpedoPending(int side, int branch)
	{
		ShipComponent ship = _selected;
		if (ship == null) return;
		if (!TorpedoRulesEvaluator.CanLaunch(ship, side))
		{
			RejectAction("❌ 无法雷击：无鱼雷管、已中破/大破、本回合已雷击或该侧已无鱼雷");
			return;
		}
		if (_director == null) return;
		bool second = _director.IsPlayerSecondTurn;
		int cost = CommandRulesEvaluator.TorpedoCPCost(ship, second);
		if (_director.CurrentCP < cost)
		{
			RejectAction($"❌ 雷击需要 {cost} CP，剩余 {_director.CurrentCP}");
			return;
		}
		int range = ship.Data?.TorpedoRange ?? 4;
		bool radarActive = ship.Data != null
			&& !string.IsNullOrEmpty(ship.Data.RadarType)
			&& ship.DamageState is DamageState.Intact or DamageState.Light;
		bool hasTarget = _enemyUnits != null && _enemyUnits.Any(target =>
			GodotObject.IsInstanceValid(target)
			&& target.CurrentHp > 0
			&& BattleRulesEvaluator.GetHexDistance(ship.HexCoords, target.HexCoords) <= range
			&& VisionRulesEvaluator.CanEngage(ship, target, _data,
				radarActive));
		if (!hasTarget)
		{
			RejectAction("❌ 射程/视野内没有可雷击的敌舰");
			return;
		}

		ship.PendingTorpedoSide = side;
		ship.PendingTorpedoBranch = branch;
		string sideText = side < 0 ? "左舷" : "右舷";
		string branchText = branch == 0 ? "正" : "斜";
		string sideName = $"{sideText}·{branchText}";
		GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
			$"💣 {ship.ShipName} {sideName}雷击待命（{cost} CP，推进后发射）");
		EndAction();
	}

	private static bool TryParseTorpedoAction(string actionId, out int side, out int branch)
	{
		side = 0;
		branch = 0;
		if (actionId == "torpedo_left_0") { side = -1; branch = 0; return true; }
		if (actionId == "torpedo_left_1") { side = -1; branch = 1; return true; }
		if (actionId == "torpedo_right_0") { side = 1; branch = 0; return true; }
		if (actionId == "torpedo_right_1") { side = 1; branch = 1; return true; }
		return false;
	}

	/// <summary>执行航速增减：检查变速限幅与 CP 消耗，更新 ship.CurrentSpeed，并按新航速推算到达格运镜。</summary>
	private List<ShipComponent> GetFormationMembersForSelected()
	{
		var markerMembers = MoveRulesEvaluator.RuntimeFormationMembers(_selected, _myUnits);
		if (markerMembers.Count >= 2 && ReferenceEquals(markerMembers[0], _selected))
			return markerMembers;
		var formation = MoveRulesEvaluator.DetectLineAhead(_selected, _myUnits);
		return formation.IsInFormation && ReferenceEquals(formation.LeadShip, _selected)
			? formation.Ships
			: null;
	}

	private void ExecuteSpeedAdjust(int delta)
	{
		int old = _selected.CurrentSpeed;
		int max = _selected.MaxSpeedForCurrentState;
		int wish = old + delta;
		if (!SpeedTable.CanAdjustSpeed(old, wish, max))
		{ RejectAction($"❌ 航速调整超限（当前 {old}）"); return; }

		int cpCost = 1;
		var formationShips = GetFormationMembersForSelected();
		bool leadFormation = formationShips != null;
		if (cpCost > 0 && _director != null && !_director.TryConsumeCP(cpCost))
		{ RejectAction($"❌ CP 不足（需要 {cpCost}，剩余 {_director.CurrentCP}）"); return; }

		if (leadFormation)
		{
			for (int i = 0; i < formationShips.Count; i++)
			{
				var ship = formationShips[i];
				ship.PendingSpeed = wish;
				ship.FormationLead = _selected;
				ship.FormationIndex = i;
			}
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
				$"⚙ 编队航速待命 {old} → {wish}（{formationShips.Count} 艘，{cpCost} CP）");
		}
		else
		{
			_selected.PendingSpeed = wish;
			_selected.FormationLead = null;
			_selected.FormationIndex = -1;
			GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
				$"⚙ 航速待命 {old} → {wish}（消耗 {cpCost} CP，推进后生效）");
		}

		EndAction();
	}

	/// <summary>点击六角格：无选中单位则尝试选中队首船；有炮击待命则执行目标确认。</summary>
	private void OnHexClicked(Vector2I hex)
	{
		if (_myUnits == null) return;

		if (_selected == null)
		{
			TrySelectAt(hex);
			return;
		}

		if (_pendingAction != null)
		{
			var other = _myUnits.Find(s =>
				s != _selected && s.HexCoords == hex
				&& GodotObject.IsInstanceValid(s) && s.CurrentHp > 0);
			if (other != null)
			{
				_pendingAction = null;
				_selected.ClearPendingCommands();
				SelectShip(other);
				return;
			}
			ExecutePendingAction(hex);
			return;
		}

		// 未在等待目标时点击当前船，重新弹出操作菜单。
		if (_selected.HexCoords == hex)
		{
			int stackCount = _myUnits.Count(s =>
				s.HexCoords == hex && GodotObject.IsInstanceValid(s) && s.CurrentHp > 0);
			if (stackCount > 1)
				TrySelectAt(hex);
			else
				GetNode<EventBus>("../EventBus").EmitSignal("ActionSelected", "_show_menu");
			return;
		}

		TrySelectAt(hex);
	}

	/// <summary>只允许选中当前队首船，保证按阶段顺序逐船操作。</summary>
	private void TrySelectAt(Vector2I hex)
	{
		var ships = _myUnits.Where(s =>
			s.HexCoords == hex && GodotObject.IsInstanceValid(s) && s.CurrentHp > 0).ToList();
		if (ships.Count == 0) return;
		_pendingAction = null;
		ShipComponent selected;
		if (ships.Count > 1)
		{
			int index = -1;
			if (_selected != null && _selected.HexCoords == hex && ships.Contains(_selected))
				index = ships.IndexOf(_selected);
			else if (_lastStackSelection.TryGetValue(hex, out ShipComponent last)
				&& ships.Contains(last))
			{
				selected = last;
				if (_selected != null && !ReferenceEquals(_selected, selected))
					_selected.ClearPendingCommands();
				SelectShip(selected);
				return;
			}
			selected = ships[(index + 1) % ships.Count];
			_lastStackSelection[hex] = selected;
		}
		else
		{
			selected = ships[0];
			_lastStackSelection[hex] = selected;
		}
		if (_selected != null && !ReferenceEquals(_selected, selected))
			_selected.ClearPendingCommands();
		SelectShip(selected);
	}

	/// <summary>执行炮击：校验敌舰与射程，确认目标后以船-敌舰中点运镜并结算射击。</summary>
	private void ExecutePendingAction(Vector2I hex)
	{
		if (_pendingAction != "attack") return;
		if (_enemyUnits == null) return;

		int d = BattleRulesEvaluator.GetHexDistance(_selected.HexCoords, hex);
		var target = _enemyUnits.Find(s =>
			s.HexCoords == hex && GodotObject.IsInstanceValid(s) && s.CurrentHp > 0);
		if (target == null || d > _selected.AttackRange)
		{ RejectAction("❌ 目标无效或不在射程内！"); return; }
		if (!CombatRulesEvaluator.CanFireInArc(_selected, target))
		{ RejectAction("❌ 目标不在当前舰炮射界内！"); return; }
		if (!VisionRulesEvaluator.CanEngage(_selected, target, _data, _selected.PendingRadarActive))
		{ RejectAction("❌ 目标不在视野或雷达范围内！"); return; }

		_selected.PendingAttackTarget = target;
		_selected.PendingAttackDistance = d;
		_selected.PendingRadarUsed = VisionRulesEvaluator.IsRadarOnly(
			_selected, target, _data, _selected.PendingRadarActive);
		// 选中敌方后：以船与敌舰中点为焦点，炮击在推进阶段才结算。
		GetNode<EventBus>("../EventBus").EmitSignal("CameraFocusBetweenRequested",
			ShipWorld(_selected), ShipWorld(target));
		GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
			$"🔫 {_selected.ShipName} 炮击待命 → {target.ShipName}（推进后结算）");
		EndAction();
	}

	/// <summary>执行转向：扣 CP、更新航向，然后俯视运镜到船体中心。</summary>
	private void ExecuteInstantAction(string id)
	{
		switch (id)
		{
			case "turn_left":
			{
				var nd = HexDirectionUtility.TurnLeft(_selected.Direction);
				int cost = MoveRulesEvaluator.TurnCostToFace(_selected.Direction, nd);
				var formationShips = GetFormationMembersForSelected();
				bool leadFormation = formationShips != null;
				if (leadFormation) cost = 1;
				if (_director != null && !_director.TryConsumeCP(cost))
				{ _pendingAction = null; RejectAction($"❌ 转向需要 {cost} CP"); return; }
				if (leadFormation)
				{
					for (int i = 0; i < formationShips.Count; i++)
					{
						formationShips[i].FormationLead = _selected;
						formationShips[i].FormationIndex = i;
						if (formationShips[i].HexCoords == _selected.HexCoords)
							formationShips[i].PendingDirection = nd;
					}
					GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
						$"↩ 编队左转待命 → 航向 {nd}（首船转向，{formationShips.Count} 艘按轨迹跟随，{cost} CP）");
				}
				else
				{
					_selected.PendingDirection = nd;
					_selected.FormationLead = null;
					_selected.FormationIndex = -1;
					GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
						$"↩ 左转待命 → 航向 {nd}（消耗 {cost} CP，推进后生效）");
				}
				EndAction();
				break;
			}
			case "turn_right":
			{
				var nd = HexDirectionUtility.TurnRight(_selected.Direction);
				int cost = MoveRulesEvaluator.TurnCostToFace(_selected.Direction, nd);
				var formationShips = GetFormationMembersForSelected();
				bool leadFormation = formationShips != null;
				if (leadFormation) cost = 1;
				if (_director != null && !_director.TryConsumeCP(cost))
				{ _pendingAction = null; RejectAction($"❌ 转向需要 {cost} CP"); return; }
				if (leadFormation)
				{
					for (int i = 0; i < formationShips.Count; i++)
					{
						formationShips[i].FormationLead = _selected;
						formationShips[i].FormationIndex = i;
						if (formationShips[i].HexCoords == _selected.HexCoords)
							formationShips[i].PendingDirection = nd;
					}
					GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
						$"↪ 编队右转待命 → 航向 {nd}（首船转向，{formationShips.Count} 艘按轨迹跟随，{cost} CP）");
				}
				else
				{
					_selected.PendingDirection = nd;
					_selected.FormationLead = null;
					_selected.FormationIndex = -1;
					GetNode<EventBus>("../EventBus").EmitSignal("LogMessage",
						$"↪ 右转待命 → 航向 {nd}（消耗 {cost} CP，推进后生效）");
				}
				EndAction();
				break;
			}
			default:
				GetNode<EventBus>("../EventBus").EmitSignal("LogMessage", id);
				break;
		}

	}

	/// <summary>只高亮当前指令后下一移动阶段将到达的单个格子。</summary>
	private void PreviewNextArrival(ShipComponent ship)
	{
		if (_map == null || _director == null) return;
		var bus = GetNode<EventBus>("../EventBus");
		bus.EmitSignal("OverlayClearRequested");
		int movePhase = _director.CurrentMovePhase;
		if (movePhase <= 0) return;
		int speed = ship.PendingSpeed >= 0 ? ship.PendingSpeed : ship.CurrentSpeed;
		// 移动阶段转向在移动完成后才生效，预览按原航向推算到达格。
		HexDirection dir = ship.Direction;
		bool oddTurn = _director.TurnNumber % 2 == 1;
		int steps = SpeedTable.MoveForPhase(speed, movePhase, oddTurn);
		var others = (_myUnits ?? new List<ShipComponent>())
			.Concat(_enemyUnits ?? new List<ShipComponent>())
			.Where(candidate => GodotObject.IsInstanceValid(candidate)
				&& !ReferenceEquals(candidate, ship)
				&& candidate.CurrentHp > 0)
			.ToList();
		var path = MoveRulesEvaluator.ResolvePreviewPath(
			ship.HexCoords,
			dir,
			steps,
			hex =>
			{
				if (_data?.IsIsland(hex) ?? false) return true;
				var occupants = others.Where(candidate => candidate.HexCoords == hex).ToList();
				if (occupants.Count == 0) return false;
				if (occupants.Any(candidate => candidate.BattleSide != ship.BattleSide)) return true;
				return occupants.Count >= 2;
			});
		if (path.Count == 0) return;
		Vector2I target = path[^1].Hex;
		bus.EmitSignal("MoveTargetHighlighted", target, (int)dir);
		bus.EmitSignal("CameraFocusBetweenRequested", ShipWorld(ship), _map.HexToWorld(target.X, target.Y));
	}

	/// <summary>全部舰船下达指令后拉高相机，等待玩家点击推进。</summary>
	private void FocusPlayerFleet()
	{
		if (_map == null) return;
		Vector3 center = Vector3.Zero;
		int count = 0;
		foreach (var ship in _myUnits)
			if (GodotObject.IsInstanceValid(ship) && ship.CurrentHp > 0)
			{
				center += ShipWorld(ship);
				count++;
			}
		if (count > 0) center /= count;
		GetNode<EventBus>("../EventBus").EmitSignal("CameraFocusRequested", center, 32f, 60f);
	}

	/// <summary>进入炮击待命时拉高镜头，让射程圈完整可见。</summary>
	private static int AttackArcMask(ShipComponent ship)
	{
		if (ship?.Data == null) return 7;
		var main = ship.Data.Firepower;
		var secondary = ship.Data.SecondaryFirepower;
		int mask = 0;
		if (main.Forward > 0 || secondary.Forward > 0) mask |= 1;
		if (main.Side > 0 || secondary.Side > 0) mask |= 2;
		if (main.Backward > 0 || secondary.Backward > 0) mask |= 4;
		return mask;
	}

	private void FocusOnAttackRange()
	{
		float distance = Mathf.Clamp(8f + _selected.AttackRange * 3f, 12f, 40f);
		GetNode<EventBus>("../EventBus").EmitSignal("CameraFocusRequested",
			ShipWorld(_selected), distance, 55f);
	}

	/// <summary>船所在六角格的世界坐标（y=0 海平面）。</summary>
	private Vector3 ShipWorld(ShipComponent ship)
	{
		if (_map != null)
			return _map.HexToWorld(ship.HexCoords.X, ship.HexCoords.Y);
		return ship.GlobalPosition;
	}

	/// <summary>拒绝操作：输出原因日志，若单位仍选中则重新弹出操作菜单。</summary>
	private void RejectAction(string message)
	{
		var bus = GetNode<EventBus>("../EventBus");
		bus.EmitSignal("LogMessage", message);
		if (_selected != null)
			bus.EmitSignal("ActionSelected", "_show_menu");
	}

	/// <summary>结束当前行动：清除高亮与选中态，自动轮到下一艘船。</summary>
	private void EndAction()
	{
		GetNode<EventBus>("../EventBus").EmitSignal("OverlayClearRequested");
		_pendingAction = null;
		if (_selected != null) _selected.ShowSelected(false);
		_selected = null;
		SelectNextShip();
	}

	/// <summary>玩家队列清空后通知 GameplayDirector 接续敌方行动。</summary>
	private void NotifyPlayerFinished()
	{
		GetNode<EventBus>("../EventBus").EmitSignal("PlayerSideFinished");
	}
}

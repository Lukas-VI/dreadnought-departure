using Godot;
using System;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// 战斗场景 UI 控制器（CanvasLayer 下的 Control）。
/// 监听 EventBus 信号更新底部 HUD 信息栏、阶段标签、CP 显示；
/// 管理底部 PhaseActionMenu 的显示/隐藏；提供阶段推进和重试按钮回调。
/// </summary>
public partial class BattleUIController : Control
{
	private BattleHudBroker _hud;
	private PhaseActionMenu _actionMenu;
	private ShipComponent _pendingShip;
	private Label _phaseLabel;
	private Control _resultOverlay;
	private Label _resultLabel;
	private Label _commandLabel;
	private VBoxContainer _playerShipList;
	private VBoxContainer _enemyShipList;
	private Control _timerPanel;
	private ProgressBar _timerBar;
	private Label _countdownLabel;
	private int _phaseIndex;

	public override void _Ready()
	{
		_hud = GetNodeOrNull<BattleHudBroker>("InfoLabel");
		_actionMenu = GetNodeOrNull<PhaseActionMenu>("ActionMenu");

		_phaseLabel = GetNodeOrNull<Label>("PhaseControlMargin/VBoxContainer/LableContainer/PhaseLabel");
		_resultOverlay = GetNodeOrNull<Control>("ResultOverlay");
		_resultLabel = GetNodeOrNull<Label>("ResultOverlay/Center/Panel/Box/ResultLabel");
		_commandLabel = GetNodeOrNull<Label>("TopCenterPanel/Box/CommandLabel");
		_playerShipList = GetNodeOrNull<VBoxContainer>("LeftPanel/Box/PlayerScroll/PlayerShipList");
		_enemyShipList = GetNodeOrNull<VBoxContainer>("RightPanel/Box/EnemyScroll/EnemyShipList");
		_timerPanel = GetNodeOrNull<Control>("PhaseControlMargin/VBoxContainer/TimerPanel");
		_timerBar = GetNodeOrNull<ProgressBar>("TimerMergin/TimerPanel/TimerBox/TimerBar");
		_countdownLabel = GetNodeOrNull<Label>("TimerMergin/TimerPanel/TimerBox/CountdownLabel");

		var bus = GetNode<EventBus>("../../EventBus");
		bus.LogMessage += (msg) => _hud?.DisplayConsoleLog(msg);
		bus.ShipInfoRequested += OnShipInfoRequested;
		bus.ShipClicked += OnShipClickedRefresh;
		bus.HexClicked += OnHexClicked;
		bus.ActionSelected += OnActionSelected;
		bus.OverlayClearRequested += HideActionMenu;
		bus.PhaseChanged += OnPhaseChanged;
		bus.CommandStateUpdated += OnCommandStateUpdated;
		bus.PhaseTimerUpdated += OnPhaseTimerUpdated;
		bus.BattleEnded += OnBattleEnded;

		if (_actionMenu != null)
			_actionMenu.ActionSelected += OnActionMenuAction;
	}

	/// <summary>选中舰船：更新 HUD 信息并缓存引用供操作菜单使用。</summary>
	private void OnShipInfoRequested(ShipComponent ship)
	{
		_pendingShip = ship;
		RefreshShipLists();
	}

	private void OnShipClickedRefresh(ShipComponent ship)
	{
		RefreshShipLists();
	}

	private void OnHexClicked(Vector2I hex)
	{
		foreach (Node node in GetTree().GetNodesInGroup("Ships"))
		{
			if (node is ShipComponent ship && GodotObject.IsInstanceValid(ship) && ship.HexCoords == hex)
			{
				RefreshShipLists();
				return;
			}
		}
	}

	private void RefreshShipLists()
	{
		if (_playerShipList == null || _enemyShipList == null) return;
		QueueFreeChildren(_playerShipList);
		QueueFreeChildren(_enemyShipList);
		var playerShips = new List<ShipComponent>();
		var enemyShips = new List<ShipComponent>();
		foreach (Node node in GetTree().GetNodesInGroup("Ships"))
		{
			if (node is not ShipComponent ship || !GodotObject.IsInstanceValid(ship)) continue;
			if (ship.BattleSide == GenerationSide.Enemy)
				enemyShips.Add(ship);
			else
				playerShips.Add(ship);
		}
		AddShipRows(_playerShipList, playerShips);
		AddShipRows(_enemyShipList, enemyShips);
	}

	private void AddShipRows(VBoxContainer box, List<ShipComponent> ships)
	{
		var assignments = BuildFormationAssignments(ships);
		var renderedGroups = new HashSet<int>();
		foreach (var ship in ships)
		{
			if (assignments.TryGetValue(ship, out var entry))
			{
				if (renderedGroups.Add(entry.Group))
					box.AddChild(new Label
					{
						Text = $"单纵阵 {entry.Group}",
						CustomMinimumSize = new Vector2(0, 22),
						HorizontalAlignment = HorizontalAlignment.Left
					});
				AddShipRow(box, ship, $"阵{entry.Index} · ");
			}
			else
			{
				AddShipRow(box, ship, "");
			}
		}
	}

	private void AddShipRow(VBoxContainer box, ShipComponent ship, string prefix)
	{
		var row = new Button
		{
			Text = prefix + FormatShipRow(ship),
			Alignment = HorizontalAlignment.Left,
			CustomMinimumSize = new Vector2(0, 32)
		};
		var hex = ship.HexCoords;
		row.Pressed += () => GetNode<EventBus>("../../EventBus").EmitSignal("HexClicked", hex);
		box.AddChild(row);
	}

	/// <summary>按当前几何关系把同速同向、首尾相邻的舰船分成单纵阵组；组内阵号从 1 开始。</summary>
	private static Dictionary<ShipComponent, (int Group, int Index)> BuildFormationAssignments(
		List<ShipComponent> ships)
	{
		var assignments = new Dictionary<ShipComponent, (int Group, int Index)>();
		var used = new HashSet<ShipComponent>();
		int groupNumber = 1;
		foreach (var ship in ships)
		{
			if (used.Contains(ship)) continue;
			var markerMembers = MoveRulesEvaluator.RuntimeFormationMembers(ship, ships);
			if (markerMembers.Count >= 2 && ReferenceEquals(ship.FormationLead, ship))
			{
				for (int i = 0; i < markerMembers.Count; i++)
					assignments[markerMembers[i]] = (groupNumber, i + 1);
				groupNumber++;
				foreach (var member in markerMembers)
					used.Add(member);
				continue;
			}
			var formation = MoveRulesEvaluator.DetectLineAhead(ship, ships);
			if (formation.IsInFormation && ReferenceEquals(formation.LeadShip, ship))
			{
				for (int i = 0; i < formation.Ships.Count; i++)
					assignments[formation.Ships[i]] = (groupNumber, i + 1);
				groupNumber++;
				foreach (var member in formation.Ships)
					used.Add(member);
			}
			else
			{
				used.Add(ship);
			}
		}
		return assignments;
	}

	private static string FormatShipRow(ShipComponent ship)
	{
		if (ship == null) return "未选中";
		return $"{ship.ShipName}\n HP {ship.CurrentHp}/{ship.MaxHp}  {ship.DamageState}  " +
			$"速 {ship.CurrentSpeed}/{ship.MaxSpeedForCurrentState}";
	}

	/// <summary>收到 actionId 后，若为 _show_menu 则按当前阶段弹出底部操作菜单。</summary>
	private void OnActionSelected(string actionId)
	{
		if (actionId == "_show_menu" && _pendingShip != null)
			_actionMenu?.ShowFor(_pendingShip, (BattlePhase)_phaseIndex);
	}

	/// <summary>底部菜单选中操作：隐藏菜单并将 actionId 转发至 EventBus。</summary>
	private void OnActionMenuAction(string actionId)
	{
		HideActionMenu();
		GetNode<EventBus>("../../EventBus").EmitSignal("ActionSelected", actionId);
	}

	/// <summary>阶段变更时更新阶段标签并缓存阶段索引。</summary>
	private void OnPhaseChanged(string phaseName, int phaseIndex, int turnNumber)
	{
		_phaseIndex = phaseIndex;
		if (_phaseLabel != null)
			_phaseLabel.Text = $"第 {turnNumber} 回合 · {phaseName}";
		RefreshShipLists();
	}

	/// <summary>指挥值/CP/PV 更新时刷新左侧状态栏。</summary>
	private void OnCommandStateUpdated(
		int playerCommand, int playerCP, int playerMaxCP,
		int enemyCommand, int enemyCP, int enemyMaxCP,
		int playerScore, int enemyScore)
	{
		if (_commandLabel != null)
			_commandLabel.Text =
				$"指挥值\n {playerCommand} : {enemyCommand}\n" +
				$"CP\n {playerCP}/{playerMaxCP} : {enemyCP}/{enemyMaxCP}\n" +
				$"PV\n {playerScore} : {enemyScore}";
	}

	private void OnPhaseTimerUpdated(float remaining, float total)
	{
		if (_timerPanel != null)
			_timerPanel.Visible = total > 0f;
		if (_timerBar != null)
		{
			_timerBar.MaxValue = Mathf.Max(total, 1f);
			_timerBar.Value = Mathf.Clamp(remaining, 0f, total);
		}
		if (_countdownLabel != null)
			_countdownLabel.Text = total > 0f ? $"{remaining:0.0}s" : "";
	}

	/// <summary>隐藏底部操作菜单。</summary>
	public void HideActionMenu() => _actionMenu?.HideMenu();

	/// <summary>推进阶段按钮回调——改为 AdvancePhase 而非 EndTurn。</summary>
	public void _OnEndTurnPressed()
	{
		HideActionMenu();
		GetNode<EventBus>("../../EventBus").EmitSignal("AdvancePhaseClicked");
	}

	/// <summary>重试按钮：重新加载战斗场景。</summary>
	public void _OnRetryPressed() => GetTree().ChangeSceneToFile("res://Scenes/Battle/battle_scene.tscn");

	/// <summary>战斗结束：显示结果面板并隐藏操作菜单。</summary>
	private void OnBattleEnded(string result, string detail)
	{
		HideActionMenu();
		if (_resultLabel != null)
			_resultLabel.Text = $"{result}\n{detail}";
		if (_resultOverlay != null)
			_resultOverlay.Visible = true;
	}

	public void _OnResultRetryPressed() => _OnRetryPressed();

	public void _OnResultExitPressed()
		=> GetTree().ChangeSceneToFile("res://Scenes/UI/Menu/MainMenu/main_menu.tscn");

	private static void QueueFreeChildren(Node parent)
	{
		foreach (Node child in parent.GetChildren())
		{
			parent.RemoveChild(child);
			child.QueueFree();
		}
	}
}

using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector : Node
{
    private LevelDataManager _dataManager;
    private MapGenerator _mapGenerator;
    private GridOverlayController _overlay;
    private BattleHudBroker _hud;
    
    // 跟踪战棋游戏时序的状态控制变量
    private ShipComponent _selectedShip = null;
    private Dictionary<Vector2I, ShipComponent> _activeShips = new();

    public override void _Ready()
    {
        // 1. 抓取各个独立组件
        _dataManager = GetNode<LevelDataManager>("LevelDataManager");
        _mapGenerator = GetNode<MapGenerator>("MapGenerator");
        _overlay = GetNode<GridOverlayController>("GridOverlayController");
        _hud = GetNode<BattleHudBroker>("CanvasLayer/MarginContainer/InfoLabel");
        // 2. 时序控制：监听输入的独立触角信号
        var inputDetector = GetNodeOrNull<BattleInputDetector>("BattleInputDetector");
        if (inputDetector != null) inputDetector.HexClicked += OnTacticalHexClicked;

        // 3. 递延一帧，启动整个世界的沙盘灌注
        CallDeferred(MethodName.LaunchBattleField);
    }

    public void LaunchBattleField()
    {
        GD.Print("--- 🚀 GameplayDirector: 收到 LevelDataManager 的就绪信号，开始生成战场 ---");
        // 驱动地图与船只生成
        _mapGenerator.BuildMap(_dataManager.TerrainData, _dataManager.UnitData);
        
        // 核心数据对齐：把生成好的格子 Mesh 丢给化妆师
        _overlay.InitializeOverlayTargets(_mapGenerator.SpawnedTileMeshes);
        
        // 记录当前战场上的所有战舰实例（供寻路和开炮查找）
        UpdateActiveShipsRegistry();

        _hud.DisplayConsoleLog("⚓ 开始。");

        GD.Print("--- 🎬 GameplayDirector: 战场生成完毕，玩家可以开始指挥了！ ---");
    }

    // 🔥 整个战棋的核心状态时序机
    private void OnTacticalHexClicked(Vector2I clickedHex)
    {
        if (_selectedShip == null)
        {
            // 状态A：选船
            if (_activeShips.TryGetValue(clickedHex, out var ship))
            {
                _selectedShip = ship;
                _overlay.DrawTacticalRange(_selectedShip.HexCoords, _selectedShip.MoveRange, _selectedShip.AttackRange);
                _hud.DisplayShipSelected(_selectedShip);
            }
        }
        else
        {
            // 状态B：已有选定船，下达战术指令
            if (_activeShips.TryGetValue(clickedHex, out var targetShip) && targetShip != _selectedShip)
            {
                int dist = BattleRulesEvaluator.GetHexDistance(_selectedShip.HexCoords, clickedHex);
                if (dist <= _selectedShip.AttackRange)
                {
                    targetShip.TakeDamage(_selectedShip.AttackPower);
                    _hud.DisplayConsoleLog($"💥 主炮齐射！对 {targetShip.ShipName} 造成 {_selectedShip.AttackPower} 点伤害！");
                }
                else _hud.DisplayConsoleLog("❌ 报告长官：目标在射程之外！");
            }
            else
            {
                // 机动
                int dist = BattleRulesEvaluator.GetHexDistance(_selectedShip.HexCoords, clickedHex);
                if (dist <= _selectedShip.MoveRange)
                {
                    _selectedShip.MoveToHex(_mapGenerator, clickedHex);
                    _hud.DisplayConsoleLog($"⚓ 舰队已机动至：{clickedHex}");
                }
                else _hud.DisplayConsoleLog("❌ 报告长官：超出机动范围！");
            }

            // 指令结算，清空时序状态
            _overlay.ClearOverlay();
            _selectedShip = null;
            UpdateActiveShipsRegistry();
        }
    }

    private void UpdateActiveShipsRegistry()
    {
        _activeShips.Clear();
        foreach (var node in GetTree().GetNodesInGroup("Ships"))
        {
            if (node is ShipComponent ship) _activeShips[ship.HexCoords] = ship;
        }
    }
}





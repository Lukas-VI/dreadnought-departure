using Godot;
using System.Collections.Generic;
using System.Linq;

namespace DreadnoughtDeparture.Core;

public partial class GameplayDirector : Node
{
	private LevelDataManager _dataManager;
	private MapGenerator _mapGenerator;
	private UnitSpawner _unitSpawner;
	private GridOverlayController _overlay;
	private BattleUIController _ui;
	private TurnManager _turnManager;
	private PlayerController _playerController;
	private AIController _aiController;
	private BattleInputDetector _input;

	public override void _Ready()
	{
		_dataManager = GetNode<LevelDataManager>("LevelDataManager");
		_mapGenerator = GetNode<MapGenerator>("MapGenerator");
		_unitSpawner = GetNode<UnitSpawner>("UnitSpawner");
		_overlay = GetNode<GridOverlayController>("GridOverlayController");
		_ui = GetNode<BattleUIController>("BattleUI");
		_turnManager = GetNode<TurnManager>("TurnManager");
		_playerController = GetNode<PlayerController>("PlayerController");
		_aiController = GetNode<AIController>("AIController");
		_input = GetNodeOrNull<BattleInputDetector>("BattleInputDetector");

		if (_input != null) _playerController.Setup(_input, _ui);
		_turnManager.Setup(_playerController, _aiController, _mapGenerator, _overlay, _ui);

		CallDeferred(MethodName.LaunchBattleField);
	}

	public void LaunchBattleField()
	{
		_mapGenerator.BuildMap(_dataManager.TerrainData);
		_unitSpawner.SpawnUnits(_dataManager.UnitData);
		_overlay.InitializeOverlayTargets(_mapGenerator.SpawnedTileMeshes);
		StartTurns();
	}

	private async void StartTurns()
	{
		await ToSignal(GetTree(), "process_frame");
		var all = new List<ShipComponent>();
		foreach (var n in GetTree().GetNodesInGroup("Ships"))
			if (n is ShipComponent s) all.Add(s);
		var player = all.Where(s => s.TileSourceId == 6).ToList();
		var enemy = all.Where(s => s.TileSourceId != 6).ToList();
		_turnManager.Start(player, enemy);
	}
}

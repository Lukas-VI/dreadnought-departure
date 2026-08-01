using Godot;

namespace DreadnoughtDeparture.Core;

/// <summary>
/// ESC key pause overlay for the battle scene.
/// Freezes the game (Tree.Paused), shows a semi-transparent menu
/// with Resume / Retry / Return-to-MainMenu options.
/// Uses ProcessMode.Always so it keeps receiving input while paused.
/// </summary>
public partial class PauseMenuController : Control
{
	[Export] public string MainMenuScenePath = "res://Scenes/UI/Menu/MainMenu/main_menu.tscn";
	[Export] public string CurrentBattleScenePath = "res://Scenes/Battle/battle_scene.tscn";

	private bool _isPaused;

	public override void _Ready()
	{
		Visible = false;
	}

	/// <summary>
	/// ESC key toggles pause state.
	/// Consumes the event so it doesn't propagate to other handlers.
	/// </summary>
	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
		{
			if (_isPaused) Resume();
			else Pause();
			GetViewport().SetInputAsHandled();
		}
	}

	private void Pause()
	{
		_isPaused = true;
		Visible = true;
		GetTree().Paused = true;
	}

	private void Resume()
	{
		_isPaused = false;
		Visible = false;
		GetTree().Paused = false;
	}

	/// <summary> Resume button: unpause and hide menu. </summary>
	public void _OnResumePressed() => Resume();

	/// <summary> Retry button: force unpause then reload the battle scene. </summary>
	public void _OnRetryPressed()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(CurrentBattleScenePath);
	}

	/// <summary> Main menu button: force unpause then return to hub. </summary>
	public void _OnMainMenuPressed()
	{
		GetTree().Paused = false;
		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}
}

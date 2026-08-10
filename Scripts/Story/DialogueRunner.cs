using Godot;
using System.Collections.Generic;

namespace DreadnoughtDeparture.Story;

/// <summary>对话脚本驱动：以 NarrativeState 为状态机，只负责把状态推进到 UI。</summary>
public partial class DialogueRunner : Node
{
	private DialogueUIController _ui;
	private NarrativeState _state;
	private bool _running;
	private Timer _waitTimer;
	private NarrativeStep _waitStep;
	private string _background = "";
	private float _backgroundAlpha = 1f;
	private string _backgroundImage = "";
	private string _backgroundOverlay = "";

	public NarrativeState State => _state;

	public override void _Ready()
	{
		var scene = ResourceLoader.Load<PackedScene>("res://Scenes/UI/Dialogue/dialogue_ui.tscn");
		if (scene == null) return;
		_ui = scene.Instantiate<DialogueUIController>();
		AddChild(_ui);
		_ui.ContinuePressed += OnContinuePressed;
		_ui.OptionSelected += OnOptionSelected;
	}

	public void Play(NarrativeState state)
	{
		if (_running || _ui == null || state == null || state.Status != NarrativeStatus.Playing)
		{
			return;
		}
		_state = state;
		_running = true;
		_background = state.Background;
		_backgroundAlpha = state.BackgroundAlpha;
		_backgroundImage = state.BackgroundImage;
		_backgroundOverlay = state.BackgroundOverlay;
		RenderCurrent();
	}

	public bool DebugBack() => Mutate(() => _state?.Back());

	public bool DebugNext() => Mutate(() => _state?.Advance());

	public bool DebugJump(int index) => Mutate(() => _state?.Jump(index));

	public void RestoreSnapshot(NarrativeSnapshot snapshot)
	{
		if (!_running || _state == null || snapshot == null) return;
		_state.Restore(snapshot);
		_background = _state.Background;
		_backgroundAlpha = _state.BackgroundAlpha;
		_backgroundImage = _state.BackgroundImage;
		_backgroundOverlay = _state.BackgroundOverlay;
		StopWaitTimer();
		RenderCurrent();
	}

	private bool Mutate(System.Action mutate)
	{
		if (!_running || _state == null) return false;
		StopWaitTimer();
		mutate();
		if (_state.Status == NarrativeStatus.Completed)
		{
			Finish();
		}
		else
		{
			RenderCurrent();
		}
		return true;
	}

	private void RenderCurrent()
	{
		if (!_running || _state == null) return;
		if (_state.Status == NarrativeStatus.Completed)
		{
			Finish();
			return;
		}
		NarrativeStep step = _state.Current;
		if (step == null)
		{
			Finish();
			return;
		}
		if (!string.IsNullOrEmpty(step.Background) || !string.IsNullOrEmpty(step.BackgroundImage))
		{
			_background = step.Background;
			_backgroundAlpha = step.BackgroundAlpha;
			_backgroundImage = step.BackgroundImage;
			_backgroundOverlay = step.BackgroundOverlay;
		}
		_ui.ApplyBackground(_background, _backgroundAlpha,
			_backgroundImage, _backgroundOverlay);
		_ui.ApplyAvatar(step.Avatar, step.AvatarPosition, step.AvatarScale);
		_ui.SetHistory(_state.History);
		_ui.ShowDebugState(_state);
		switch (step.Type)
		{
			case "say":
				_ui.ShowSay(step.Speaker, step.Text);
				break;
			case "choice":
				var texts = new List<string>();
				foreach (NarrativeOption option in step.Options)
				{
					texts.Add(option.Text);
				}
				_ui.ShowOptions(step.Text, texts);
				break;
			case "wait":
				StartWaitTimer(step);
				break;
			case "flag":
				StoryDirector.Instance?.SetFlag(step.Key, step.Value);
				_state.Flags[step.Key] = step.Value;
				AdvanceAndRender();
				break;
			case "background":
				_ui.ApplyBackground(step.Background, step.BackgroundAlpha,
					step.BackgroundImage, step.BackgroundOverlay);
				AdvanceAndRender();
				break;
			default:
				AdvanceAndRender();
				break;
		}
	}

	private void AdvanceAndRender()
	{
		if (!_running || _state == null) return;
		_state.Advance();
		RenderCurrent();
	}

	private void OnContinuePressed()
	{
		if (!_running || _state == null) return;
		NarrativeStep step = _state.Current;
		if (step == null || step.Type != "say") return;
		_state.RecordHistory(step);
		_state.Advance();
		RenderCurrent();
	}

	private void OnOptionSelected(int index)
	{
		if (!_running || _state == null) return;
		NarrativeStep step = _state.Current;
		if (step == null || step.Type != "choice") return;
		if (index < 0 || index >= step.Options.Count) return;
		_state.RecordHistory(step);
		_state.SelectChoice(index);
		RenderCurrent();
	}

	private void StartWaitTimer(NarrativeStep step)
	{
		StopWaitTimer();
		_waitStep = step;
		_waitTimer = new Timer
		{
			WaitTime = System.Math.Max(0f, step.Seconds),
			OneShot = true,
		};
		AddChild(_waitTimer);
		_waitTimer.Timeout += OnWaitTimeout;
		_waitTimer.Start();
	}

	private void OnWaitTimeout()
	{
		Timer timer = _waitTimer;
		_waitTimer = null;
		NarrativeStep step = _waitStep;
		_waitStep = null;
		if (timer != null)
		{
			timer.Stop();
			timer.QueueFree();
		}
		if (!_running || _state == null || !ReferenceEquals(_state.Current, step)) return;
		_state.Advance();
		RenderCurrent();
	}

	private void StopWaitTimer()
	{
		if (_waitTimer != null)
		{
			_waitTimer.Stop();
			_waitTimer.QueueFree();
			_waitTimer = null;
		}
		_waitStep = null;
	}

	private void Finish()
	{
		if (!_running) return;
		_running = false;
		StopWaitTimer();
		_state?.Complete();
		_ui?.HideUi();
		StoryDirector.Instance?.NotifyFinished();
	}
}

using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// A full-screen loading overlay shown during a hard region transition (Phase 25C). It reacts to
/// <see cref="GameStateChangedEvent"/> and is visible only while the game is in
/// <see cref="GameState.Loading"/>, covering the screen while the bootstrap swaps regions and the
/// streamer pulls in the destination cells. Runs with <see cref="Node.ProcessModeEnum.Always"/> and
/// sits above the rest of the UI. Built via <see cref="UiTheme"/>, mirroring <see cref="PauseMenu"/>.
/// </summary>
public partial class LoadingScreen : CanvasLayer
{
	private ColorRect _backdrop = null!;
	private PanelContainer _panel = null!;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 20; // above the pause menu (10) so a transition is never occluded
		Build();
		SetShown(GameManager.Instance?.State == GameState.Loading);
		EventBus.Instance?.Subscribe<GameStateChangedEvent>(OnGameStateChanged);
	}

	public override void _ExitTree()
	{
		EventBus.Instance?.Unsubscribe<GameStateChangedEvent>(OnGameStateChanged);
	}

	private void Build()
	{
		_backdrop = UiTheme.Scrim(1f);
		_backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(_backdrop);

		_panel = UiTheme.Panel();
		_panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		_panel.GrowHorizontal = Control.GrowDirection.Both;
		_panel.GrowVertical = Control.GrowDirection.Both;
		_panel.CustomMinimumSize = new Vector2(280, 0);
		AddChild(_panel);

		MarginContainer pad = UiTheme.Padding(16);
		_panel.AddChild(pad);

		Label header = UiTheme.Header(Loc.T("loading.title"));
		header.HorizontalAlignment = HorizontalAlignment.Center;
		pad.AddChild(header);
	}

	private void OnGameStateChanged(GameStateChangedEvent e) => SetShown(e.Current == GameState.Loading);

	// Dismissal fade (30.5I): elapsed fade-out time; <0 while idle.
	private float _fadeAge = -1f;

	private void SetShown(bool visible)
	{
		if (!visible && _backdrop.Visible && _fadeAge < 0f &&
			UiTheme.Duration(UiTheme.DurationSlow) > 0f)
		{
			// Fade the cover away to reveal the arrival (ease-in exit); showing stays instant
			// so the screen is covered the moment a transition starts.
			_fadeAge = 0f;
			return;
		}

		_fadeAge = -1f;
		_backdrop.Modulate = Colors.White;
		_panel.Modulate = Colors.White;
		_backdrop.Visible = visible;
		_panel.Visible = visible;
	}

	public override void _Process(double delta)
	{
		if (_fadeAge < 0f)
		{
			return;
		}

		_fadeAge += (float)delta;
		float alpha = 1f - UiMotion.EaseIn(UiMotion.Progress(_fadeAge, UiTheme.Duration(UiTheme.DurationSlow)));
		var faded = new Color(1f, 1f, 1f, alpha);
		_backdrop.Modulate = faded;
		_panel.Modulate = faded;

		if (alpha <= 0f)
		{
			SetShown(false);
		}
	}
}

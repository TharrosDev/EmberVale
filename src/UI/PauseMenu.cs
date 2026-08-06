using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The pause menu (Phase 18): a real modal menu on the <c>pause</c> action (Esc) — Resume,
/// quick Save / Load, and Quit — replacing the bare pause toggle. It runs with
/// <see cref="Node.ProcessModeEnum.Always"/> so its buttons work while the tree is paused,
/// dims the scene behind a backdrop, and drives the <see cref="GameManager"/> pause state
/// (which frees/recaptures the mouse through the player controller). Built via
/// <see cref="UiTheme"/>.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private ColorRect _backdrop = null!;
	private PanelContainer _panel = null!;
	private bool _open;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 10; // above the rest of the UI
		Build();
		SetPanelVisible(false);
	}

	public override void _Process(double delta)
	{
		// Gamepad B (ui_cancel) resumes like Esc while open (30.5J). Esc raises both actions
		// on one press; the OR evaluates once, so it still toggles exactly once.
		bool pressed = Godot.Input.IsActionJustPressed(GameInput.Pause) ||
			(_open && Godot.Input.IsActionJustPressed("ui_cancel"));
		if (!pressed)
		{
			return;
		}

		// While a higher modal (the settings panel) owns the screen it sets UiState.MenuOpen and
		// consumes Esc to close itself — don't also resume the game on that same press. A UiPanel
		// closing on this same frame's cancel press already consumed it too (30.5J).
		if (UiState.MenuOpen || UiPanel.LastCancelCloseFrame == Engine.GetProcessFrames())
		{
			return;
		}

		if (_open)
		{
			Resume();
		}
		else if (GameManager.Instance is { IsPlaying: true })
		{
			Open();
		}
	}

	private void Build()
	{
		_backdrop = UiTheme.Scrim(0.55f);
		_backdrop.MouseFilter = Control.MouseFilterEnum.Stop;
		AddChild(_backdrop);

		_panel = UiTheme.Panel();
		_panel.SetAnchorsPreset(Control.LayoutPreset.Center);
		// Grow from the centre anchor in both directions so the panel is truly centred
		// (the default End grow would push it toward the bottom-right of centre).
		_panel.GrowHorizontal = Control.GrowDirection.Both;
		_panel.GrowVertical = Control.GrowDirection.Both;
		_panel.CustomMinimumSize = new Vector2(280, 0);
		AddChild(_panel);

		MarginContainer pad = UiTheme.Padding(16);
		_panel.AddChild(pad);

		var col = new VBoxContainer();
		col.AddThemeConstantOverride("separation", 8);
		pad.AddChild(col);

		Label header = UiTheme.Title(Loc.T("pause.title"));
		header.HorizontalAlignment = HorizontalAlignment.Center;
		col.AddChild(header);
		col.AddChild(new HSeparator());

		col.AddChild(MenuButton(Loc.T("pause.resume"), Resume));
		col.AddChild(MenuButton(Loc.T("pause.save"), () => { if (SaveManager.Instance is { } s) { s.SaveGame(s.ActiveSlot); } }));
		col.AddChild(MenuButton(Loc.T("pause.load"), () => { if (SaveManager.Instance is { } s) { s.LoadGame(s.ActiveSlot); } }));
		col.AddChild(MenuButton(Loc.T("pause.settings"), OpenSettings));
		col.AddChild(MenuButton(Loc.T("pause.main_menu"), ReturnToMainMenu));
		col.AddChild(MenuButton(Loc.T("pause.quit"), () => GetTree().Quit()));
	}

	/// <summary>
	/// Leaves the session and returns to the title screen (37.5H). Until now the only way out of a
	/// game was closing the process.
	///
	/// It **reloads the whole scene** rather than tearing the world down in place, and that is the
	/// safe choice rather than the lazy one. `GameBootstrap` builds the world as its own children
	/// behind a one-shot `_sandboxBuilt` guard with no teardown path, and CLAUDE.md §7 records why
	/// unpicking that by hand is dangerous: several services register with `ServiceLocator` and
	/// never unregister, and dereferencing a freed registrant is a hard `gchandle.is_released`
	/// crash rather than something a null check catches. A reload rebuilds from boot, and the
	/// autoloads (EventBus, ServiceLocator, GameManager, SaveManager) survive it by design.
	///
	/// ⚠️ **The two process-lifetime statics must be cleared first.** A scene reload does not touch
	/// them, so the pause menu that triggered this would still be registered in `UiState` — and a
	/// registered owner is also a world-pauser, so the title screen would come back with the tree
	/// paused and nothing on screen able to unpause it. Ordering matters too: state goes back to
	/// `MainMenu` *before* the reload so `GameManager.RefreshPause` settles on an unpaused tree.
	///
	/// The reload is deferred because this runs inside a button signal, and freeing the node that
	/// owns the running signal handler mid-emit is exactly the crash `call_deferred` exists for.
	///
	/// Note this makes `UiPanel._Notification`'s modal-release guard reachable for the first time.
	/// It was written as "nothing reaches this today — the game has no quit-to-menu path", and
	/// documented as correct regardless. It now earns its place.
	/// </summary>
	private void ReturnToMainMenu()
	{
		SetPanelVisible(false);

		UiState.ClearAll();
		Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
		GameManager.Instance?.ChangeState(GameState.MainMenu);

		Callable.From(() => GetTree().ReloadCurrentScene()).CallDeferred();
	}

	private static Button MenuButton(string text, System.Action onPressed)
	{
		Button button = UiTheme.Action(text);
		button.CustomMinimumSize = new Vector2(0, 34);
		button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		button.Pressed += () => onPressed();
		return button;
	}

	private void OpenSettings()
	{
		// Hide the pause panel behind the settings overlay; restore it when the player backs out.
		// The game stays paused throughout, and UiState.MenuOpen (set by the panel) keeps Esc from
		// resuming until the panel is closed.
		SetPanelVisible(false);
		SettingsPanel.Open(this, () => SetPanelVisible(true));
	}

	private void Open()
	{
		_open = true;
		SetPanelVisible(true);
		GameManager.Instance?.ChangeState(GameState.Paused);
	}

	private void Resume()
	{
		_open = false;
		SetPanelVisible(false);
		GameManager.Instance?.ChangeState(GameState.Playing);
	}

	private void SetPanelVisible(bool visible)
	{
		_backdrop.Visible = visible;
		_panel.Visible = visible;

		// Fade in on show (30.5I; instant under reduced motion — Duration collapses to 0);
		// hiding stays instant so resume never lags input. Tween pause mode Process because
		// the tree is paused while this menu is up.
		if (visible)
		{
			_backdrop.Modulate = new Color(1f, 1f, 1f, 0f);
			_panel.Modulate = new Color(1f, 1f, 1f, 0f);
			UiTheme.AnimateModulate(_backdrop, Colors.White, UiTheme.DurationBase);
			UiTheme.AnimateModulate(_panel, Colors.White, UiTheme.DurationBase);
			UiFocus.GrabFirst(_panel); // gamepad/keyboard start on Resume (30.5J)
		}
	}
}

using Embervale.Core;
using Embervale.Localization;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The pause menu (Phase 18): a real modal menu on the <c>pause</c> action (Esc) — Resume, Save,
/// Load, Settings and the two ways out — replacing the bare pause toggle. It runs with
/// <see cref="Node.ProcessModeEnum.Always"/> so its buttons work while the tree is paused,
/// dims the scene behind a backdrop, and drives the <see cref="GameManager"/> pause state
/// (which frees/recaptures the mouse through the player controller). Built via
/// <see cref="UiTheme"/>.
///
/// <para><b>Saving and loading (ics save-ui).</b> <i>Save</i> writes the session's manual slot in
/// one press, or opens the slot browser when the session has none yet (a game loaded from an
/// autosave or the quick slot). <i>Save to Slot</i> and <i>Load</i> open the
/// <see cref="SaveSlotPanel"/> over this menu; Esc / B there returns here. Both ways out write an
/// autosave first, and ask before leaving when that save is refused or fails. The outcome of a save
/// is written under the title, because toasts are held back while the game is paused.</para>
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	private ColorRect _backdrop = null!;
	private PanelContainer _panel = null!;
	private VBoxContainer _menu = null!;
	private VBoxContainer _confirm = null!;
	private Label _confirmText = null!;
	private Button _confirmYes = null!;
	private Label _status = null!;
	private System.Action? _onConfirm;
	private SaveSlotPanel? _browser;
	private ulong _browserClosedFrame = ulong.MaxValue;
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
		// The slot browser owns Esc / B while it is up, and the press that closed it must not also
		// resume the game.
		if ((_browser != null && IsInstanceValid(_browser)) || _browserClosedFrame == Engine.GetProcessFrames())
		{
			return;
		}

		// Gamepad B (ui_cancel) resumes like Esc while open (30.5J). Esc raises both actions
		// on one press; the OR evaluates once, so it still toggles exactly once.
		bool pressed = Godot.Input.IsActionJustPressed(UiLive.Pause) ||
			(_open && Godot.Input.IsActionJustPressed(UiLive.UiCancel));
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

		if (_open && _confirm.Visible)
		{
			HideConfirm(); // cancel answers the question with "no" before it resumes anything
		}
		else if (_open)
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
		_panel.CustomMinimumSize = new Vector2(320, 0);
		AddChild(_panel);

		MarginContainer pad = UiTheme.Padding(UiTheme.SpaceLg);
		_panel.AddChild(pad);

		var col = new VBoxContainer();
		col.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
		pad.AddChild(col);

		Label header = UiTheme.Title(Loc.T("pause.title"));
		header.HorizontalAlignment = HorizontalAlignment.Center;
		col.AddChild(header);

		_status = UiTheme.Caption(string.Empty);
		_status.HorizontalAlignment = HorizontalAlignment.Center;
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_status.CustomMinimumSize = new Vector2(280, 0);
		_status.Visible = false;
		col.AddChild(_status);
		col.AddChild(UiTheme.Divider());

		_menu = new VBoxContainer();
		_menu.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
		col.AddChild(_menu);

		_menu.AddChild(MenuButton(Loc.T("pause.resume"), Resume));
		_menu.AddChild(MenuButton(Loc.T("pause.save"), Save));
		_menu.AddChild(MenuButton(Loc.T("pause.save_as"), () => OpenBrowser(SaveSlotPanel.Intent.Save)));
		_menu.AddChild(MenuButton(Loc.T("pause.load"), () => OpenBrowser(SaveSlotPanel.Intent.Load)));
		_menu.AddChild(MenuButton(Loc.T("pause.settings"), OpenSettings));
		_menu.AddChild(MenuButton(Loc.T("pause.main_menu"), () => RequestQuit(ReturnToMainMenu)));
		_menu.AddChild(MenuButton(Loc.T("pause.quit"), () => RequestQuit(() => GetTree().Quit())));

		// The one question this menu asks ("leave with unsaved progress?") replaces the buttons
		// rather than stacking a dialog on top: one panel, one focus chain, Esc / B means no.
		_confirm = new VBoxContainer { Visible = false };
		_confirm.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
		col.AddChild(_confirm);

		_confirmText = UiTheme.Prose(string.Empty, UiTheme.Text);
		_confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_confirmText.CustomMinimumSize = new Vector2(280, 0);
		_confirm.AddChild(_confirmText);

		// Cancel first, so the default focus is the answer that loses nothing.
		_confirm.AddChild(MenuButton(Loc.T("common.cancel"), HideConfirm));
		_confirmYes = MenuButton(string.Empty, () =>
		{
			System.Action? confirmed = _onConfirm;
			HideConfirm();
			confirmed?.Invoke();
		});
		_confirmYes.AddThemeColorOverride("font_color", UiTheme.Bad);
		_confirm.AddChild(_confirmYes);
	}

	// --- Save -----------------------------------------------------------------------------

	/// <summary>One-press save into the session's manual slot. A session with no manual slot of its
	/// own (it was loaded from an autosave or the quick slot) is asked to pick one in the browser,
	/// which confirms an overwrite, instead of having one guessed for it.</summary>
	private void Save()
	{
		if (SaveManager.Instance is not { } saves || SessionHost() is not { Session: not null })
		{
			return;
		}

		string? target = SaveSlotPolicy.ManualSaveTarget(saves.ActiveSlot);
		if (target == null)
		{
			OpenBrowser(SaveSlotPanel.Intent.Save);
			return;
		}

		SaveTo(target);
	}

	private void SaveTo(string slot)
	{
		if (SessionHost() is not { } lifecycle)
		{
			return;
		}

		bool saved = lifecycle.TrySave(slot, out string failureKey);
		SetStatus(
			saved ? Loc.TF("pause.saved_to", SaveSlotPanel.SlotLabel(slot)) : Loc.T(failureKey),
			saved ? UiTheme.Good : UiTheme.Bad);
	}

	// --- Slot browser ---------------------------------------------------------------------

	private void OpenBrowser(SaveSlotPanel.Intent intent)
	{
		if (SaveManager.Instance is not { } saves || SessionHost() is not { } lifecycle)
		{
			return;
		}

		// Loading throws away whatever has happened since the last save; past the threshold the
		// browser says how much and asks for a second click on the row.
		string? warning = intent == SaveSlotPanel.Intent.Load && SaveSlotPolicy.NeedsUnsavedConfirm(lifecycle.SecondsSinceLastSave)
			? Loc.TF("slots.load_unsaved", UnsavedAge(lifecycle.SecondsSinceLastSave))
			: null;

		var browser = new SaveSlotPanel();
		browser.Configure(intent, slot => OnBrowserChose(intent, slot), CloseBrowser, warning, saves.ActiveSlot);
		_browser = browser;
		SetPanelVisible(false);
		AddChild(browser);
	}

	private void CloseBrowser()
	{
		_browser = null;
		_browserClosedFrame = Engine.GetProcessFrames();
		SetPanelVisible(true);
	}

	private void OnBrowserChose(SaveSlotPanel.Intent intent, string slot)
	{
		CloseBrowser();
		if (intent == SaveSlotPanel.Intent.Save)
		{
			SaveTo(slot);
			return;
		}

		if (SessionHost() is { Session: { } session } lifecycle && lifecycle.RequestReload(session, slot))
		{
			SetPanelVisible(false);
			return;
		}

		SetStatus(Loc.T("pause.load_refused"), UiTheme.Bad);
	}

	// --- Leaving --------------------------------------------------------------------------

	/// <summary>
	/// Both ways out go through here. Leaving writes an autosave first, so quitting never costs
	/// progress on its own; when saving is blocked (a boss fight, a conversation) or the write fails,
	/// the player is told why and how much play is at stake, and chooses.
	/// </summary>
	private void RequestQuit(System.Action proceed)
	{
		Bootstrap.SessionLifecycleCoordinator? lifecycle = SessionHost();
		if (lifecycle == null || lifecycle.AutosaveBeforeQuit(out string failureKey))
		{
			proceed();
			return;
		}

		ShowConfirm(
			Loc.TF("pause.quit_unsaved", Loc.T(failureKey), UnsavedAge(lifecycle.SecondsSinceLastSave)),
			Loc.T("pause.quit_anyway"),
			proceed);
	}

	/// <summary>How long ago the game was last saved, as the player would say it.</summary>
	public static string UnsavedAge(double seconds)
	{
		int minutes = SaveSlotPolicy.WholeMinutes(seconds);
		return minutes < 1 ? Loc.T("save.age.under_minute")
			: minutes < 60 ? Loc.TF("save.age.minutes", minutes)
			: Loc.TF("save.age.hours", minutes / 60, minutes % 60);
	}

	private void ShowConfirm(string message, string confirmLabel, System.Action onConfirm)
	{
		_confirmText.Text = message;
		_confirmYes.Text = confirmLabel;
		_onConfirm = onConfirm;
		_menu.Visible = false;
		_confirm.Visible = true;
		UiFocus.GrabFirst(_confirm);
	}

	private void HideConfirm()
	{
		_onConfirm = null;
		_confirm.Visible = false;
		_menu.Visible = true;
		UiFocus.GrabFirst(_menu);
	}

	private void SetStatus(string text, Color color)
	{
		_status.Text = text;
		_status.AddThemeColorOverride("font_color", color);
		_status.Visible = text.Length > 0;
	}

	/// <summary>
	/// Leaves the session and returns to the title screen.
	///
	/// It used to reload the whole scene, and the comment here used to explain at length why that
	/// was the safe choice rather than the lazy one: the bootstrap built the world as its own
	/// children behind a one-shot guard with no teardown path, several services registered with the
	/// locator and never unregistered, and dereferencing a freed registrant is a hard
	/// `gchandle.is_released` crash rather than something a null check catches.
	///
	/// None of that is true any more. A session is a node; freeing it disposes the session and world
	/// scopes, which take every registration with them, and the coordinator resets the
	/// process-lifetime statics the reload used to clear as a side effect. So the pause menu now asks
	/// for what it actually wants -- end this session -- and the title screen comes back in the same
	/// process, with the next New Game able to start immediately.
	///
	/// It is still deferred: this runs inside a button signal, and this menu is a child of the
	/// session being destroyed. Freeing the node that owns the running signal handler mid-emit is
	/// exactly the crash `call_deferred` exists for.
	/// </summary>
	private void ReturnToMainMenu()
	{
		SetPanelVisible(false);
		Bootstrap.SessionLifecycleCoordinator? lifecycle = SessionHost();
		Bootstrap.GameSession? requestingSession = lifecycle?.Session;

		Callable.From(() =>
		{
			if (lifecycle != null)
			{
				if (IsInstanceValid(lifecycle) && ReferenceEquals(lifecycle.Session, requestingSession))
				{
					lifecycle.DestroySession();
				}
				return;
			}

			// No session above us: nothing to destroy, so just make sure the shell is usable.
			UiState.ClearAll();
			Godot.Input.MouseMode = Godot.Input.MouseModeEnum.Visible;
			GameManager.Instance?.ChangeState(GameState.MainMenu);
		}).CallDeferred();
	}

	/// <summary>Same checkpoint route as F9, with no questions asked: reloads the session's own slot.
	/// The menu's Load button goes through the browser instead; this stays for callers that already
	/// know the answer (the lifecycle gate drives it). The coordinator owns the deferred rebuild, so
	/// this menu can be destroyed without leaving a callback that walks its old parent chain.</summary>
	public bool RequestLoad()
	{
		if (SaveManager.Instance is not { } saves || SessionHost() is not { Session: { } session } lifecycle)
		{
			return false;
		}
		if (!lifecycle.RequestReload(session, saves.ActiveSlot)) { return false; }
		SetPanelVisible(false);
		return true;
	}

	/// <summary>The coordinator that owns the session this menu is inside, found by walking up the
	/// tree rather than through a global -- the menu belongs to exactly one session.</summary>
	private Bootstrap.SessionLifecycleCoordinator? SessionHost()
	{
		for (Node? node = GetParent(); node != null; node = node.GetParent())
		{
			if (node is Bootstrap.SessionLifecycleCoordinator host)
			{
				return host;
			}
		}

		return null;
	}

	private static Button MenuButton(string text, System.Action onPressed)
	{
		Button button = UiTheme.Action(text);
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

	/// <summary>Opens and closes the menu for the screenshot harnesses, which cannot press Esc.</summary>
	public void OpenForCapture() => Open();

	public void CloseForCapture() => Resume();

	private void Open()
	{
		_open = true;
		SetStatus(string.Empty, UiTheme.Dim);
		HideConfirm();
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
			UiFocus.GrabFirst(_confirm.Visible ? _confirm : _menu); // gamepad/keyboard start on Resume (30.5J)
		}
	}
}

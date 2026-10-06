using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Save;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The pause menu (Phase 18): a real modal menu on the <c>pause</c> action (Esc) — Resume, Save,
/// Load, Settings and the two ways out — replacing the bare pause toggle. It runs with
/// <see cref="Node.ProcessModeEnum.Always"/> so its buttons work while the tree is paused,
/// dims the scene behind a backdrop, and drives the <see cref="GameManager"/> pause state
/// (which frees/recaptures the mouse through the player controller). Built via
/// <see cref="UiTheme"/>: a frameless sheet over the paused frame, its entries text on the scrim,
/// with the tracked objective and the time played above them so a player coming back to the game
/// reads where they were before choosing anything.
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
	private Control _root = null!;
	private Control _wipe = null!;
	private ScrollContainer _menuScroll = null!;
	private VBoxContainer _menu = null!;
	private VBoxContainer _confirm = null!;
	private Label _confirmText = null!;
	private Button _confirmYes = null!;
	private Label _status = null!;
	private VBoxContainer _tracked = null!;
	private Label _trackedQuest = null!;
	private Label _trackedObjective = null!;
	private Label _played = null!;
	private UiLegend _legend = null!;
	private System.Action? _onConfirm;
	private SaveSlotPanel? _browser;
	private ulong _browserClosedFrame = ulong.MaxValue;
	private bool _open;

	// This save's play time, counted the way SaveManager counts the one it stamps into a header
	// (active play only, continued from the loaded save). The manager keeps its own private.
	private double _playtimeSeconds;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Layer = 10; // above the rest of the UI
		Build();
		SetPanelVisible(false);
	}

	public override void _EnterTree() => EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);

	public override void _ExitTree() => EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);

	private void OnGameLoaded(GameLoadedEvent e) =>
		_playtimeSeconds = SaveManager.Instance?.ReadHeader(e.Slot)?.PlaytimeSeconds ?? 0d;

	public override void _Process(double delta)
	{
		if (GameManager.Instance is { IsPlaying: true })
		{
			_playtimeSeconds += delta;
		}

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
		// A sheet, not a framed panel: the entries sit on the scrim behind one lit rule, and the
		// frame the player paused on still reads through it.
		Vector2 view = GetViewport().GetVisibleRect().Size;
		float width = Mathf.Min(UiTheme.PauseSheetWidth, view.X - (UiChromeRules.Gutter(view.X) * 2f));
		(Control root, VBoxContainer col) = UiTheme.Sheet(width, 0.55f);
		_root = root;
		AddChild(root);

		// The time played shares the title's line: on a 533 px handheld view a line of its own is
		// the one that pushes the last entry off the sheet.
		var head = new HBoxContainer();
		head.AddThemeConstantOverride("separation", UiTheme.SpaceMd);
		Label title = UiTheme.Title(Loc.T("pause.title"));
		title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		head.AddChild(title);
		_played = UiTheme.Caption(string.Empty);
		_played.SizeFlagsVertical = Control.SizeFlags.ShrinkEnd;
		head.AddChild(_played);
		col.AddChild(head);

		_wipe = UiOrnament.EmberWipe();
		col.AddChild(_wipe);

		// What the player was doing: the tracked quest and its current step, in the tracker's words.
		_tracked = new VBoxContainer();
		_tracked.AddThemeConstantOverride("separation", UiTheme.LineGap);
		_tracked.AddChild(UiTheme.Caption(Loc.T("session.pause.tracking")));
		_trackedQuest = UiTheme.Body(string.Empty);
		_trackedQuest.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_tracked.AddChild(_trackedQuest);
		_trackedObjective = UiTheme.Caption(string.Empty, UiTheme.Text);
		_trackedObjective.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		_tracked.AddChild(_trackedObjective);
		col.AddChild(_tracked);

		_status = UiTheme.Caption(string.Empty);
		_status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_status.Visible = false;
		col.AddChild(_status);

		// The entries scroll, and only when they must: on a short view, or at a large text size,
		// the sheet has less room than seven controls. Their height is known without a layout pass.
		(_menuScroll, _menu) = UiTheme.ScrollList();
		_menu.AddThemeConstantOverride("separation", 0);
		_menuScroll.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
		col.AddChild(_menuScroll);

		_menu.AddChild(MenuButton(Loc.T("pause.resume"), Resume, UiCue.Back));
		_menu.AddChild(MenuButton(Loc.T("pause.save"), Save, UiCue.Confirm));
		_menu.AddChild(MenuButton(Loc.T("session.pause.save_as"), () => OpenBrowser(SaveSlotPanel.Intent.Save)));
		_menu.AddChild(MenuButton(Loc.T("pause.load"), () => OpenBrowser(SaveSlotPanel.Intent.Load)));
		_menu.AddChild(MenuButton(Loc.T("pause.settings"), OpenSettings));
		_menu.AddChild(MenuButton(Loc.T("session.pause.main_menu"), () => RequestQuit(ReturnToMainMenu)));
		_menu.AddChild(MenuButton(Loc.T("session.pause.quit"), () => RequestQuit(() => GetTree().Quit())));

		float entries = _menu.GetChildCount() * UiTheme.ControlHeight;
		float room = view.Y - (UiTheme.SpaceXl * 2f) - UiTheme.PauseHeaderReserve;
		_menuScroll.CustomMinimumSize = new Vector2(0f, Mathf.Clamp(room, UiTheme.ControlHeight * 2f, entries));

		// The one question this menu asks ("leave with unsaved progress?") replaces the entries
		// rather than stacking a dialog on top: one sheet, one focus chain, Esc / B means no.
		_confirm = new VBoxContainer { Visible = false };
		_confirm.AddThemeConstantOverride("separation", UiTheme.SpaceSm);
		col.AddChild(_confirm);

		_confirmText = UiTheme.Prose(string.Empty, UiTheme.Text);
		_confirmText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_confirm.AddChild(_confirmText);

		// Cancel first, so the default focus is the answer that loses nothing.
		_confirm.AddChild(MenuButton(Loc.T("common.cancel"), HideConfirm, UiCue.Back));
		_confirmYes = MenuButton(string.Empty, () =>
		{
			System.Action? confirmed = _onConfirm;
			HideConfirm();
			confirmed?.Invoke();
		});
		_confirmYes.AddThemeColorOverride("font_color", UiTheme.Bad);
		_confirm.AddChild(_confirmYes);

		_legend = new UiLegend();
		AddChild(_legend);
	}

	/// <summary>Fills in what the sheet says about the game it paused. Read when the menu opens:
	/// nothing it shows can change while the world is held.</summary>
	private void RefreshSummary()
	{
		(int hours, int minutes) = ShellSessionRules.Playtime(_playtimeSeconds);
		_played.Text = Loc.TF("session.pause.played", Loc.TF("slots.playtime", hours, $"{minutes:00}"));

		QuestProgress? quest = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
			? player.GetComponent<QuestLogComponent>()?.Tracked
			: null;

		// A ledger quest is an umbrella record the tracker never shows; this does not either.
		_tracked.Visible = quest != null && !quest.Quest.IsLedger;
		if (quest == null || !_tracked.Visible)
		{
			return;
		}

		_trackedQuest.Text = Loc.T(quest.Quest.Title);
		_trackedQuest.AddThemeColorOverride("font_color", quest.Quest.IsMainQuest ? UiTheme.QuestMain : UiTheme.QuestSide);

		// No live step means every objective is met and the quest is waiting to be handed in.
		ObjectiveResource? objective = QuestProgressViews.CurrentObjective(quest, out int index);
		_trackedObjective.Visible = objective != null;
		if (objective != null)
		{
			string step = Loc.T(objective.ShortLabel());
			_trackedObjective.Text = objective.RequiredCount > 1
				? Loc.TF("session.pause.objective_count", step, quest.Counts[index], objective.RequiredCount)
				: step;
		}
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
		_menuScroll.Visible = false;
		_confirm.Visible = true;
		UiFocus.GrabFirst(_confirm);
		UpdateLegend();
	}

	private void HideConfirm()
	{
		_onConfirm = null;
		_confirm.Visible = false;
		_menuScroll.Visible = true;
		UiFocus.GrabFirst(_menu);
		UpdateLegend();
	}

	private void UpdateLegend() => _legend.Set(new[]
	{
		new LegendEntry("ui_accept", Loc.T("session.legend.select")),
		new LegendEntry("ui_cancel", Loc.T(_confirm.Visible ? "common.cancel" : "pause.resume")),
	});

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

	/// <summary>One entry of the sheet: text until it is focused (<see cref="UiTheme.SessionAction"/>),
	/// a full control tall.</summary>
	private static Button MenuButton(string text, System.Action onPressed, UiCue cue = UiCue.Click)
	{
		Button button = UiTheme.SessionAction(text, cue);
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

	/// <summary>Whether the sheet is up and drawn. Read by the screenshot harness.</summary>
	public bool ShownForCapture => _open && _root.Visible;

	private void Open()
	{
		_open = true;
		SetStatus(string.Empty, UiTheme.Dim);
		RefreshSummary();
		HideConfirm();
		SetPanelVisible(true);
		UiAudio.Play(UiCue.Open);
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
		_legend.Visible = visible;
		if (!visible)
		{
			// Hiding is instant so resume never lags input.
			_root.Visible = false;
			return;
		}

		// Fade in on show (instant under reduced motion); UiFx runs while the tree is paused, which
		// it is whenever this menu is up. The rule under the title is drawn again each time.
		UiFx.FadeIn(_root);
		UiOrnament.PlayEmberWipe(_wipe);
		UiFocus.GrabFirst(_confirm.Visible ? _confirm : _menu); // gamepad/keyboard start on Resume (30.5J)
	}
}

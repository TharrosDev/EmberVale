using System;
using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>Raised whenever a <see cref="UiPanel"/> opens or closes. Lets systems react to the
/// player's use of the UI (the onboarding teaches the inventory and journal this way) without
/// polling panels or guessing from raw keypresses.</summary>
public readonly record struct UiPanelToggledEvent(UiPanel Panel, bool Open) : IGameEvent;

/// <summary>
/// The reusable panel shell (Phase 30.5F) every UI panel/screen builds on. It owns the
/// boilerplate the panels each hand-rolled since Phase 18: the themed frame, an optional
/// toggle input action, the modal contract (register with <see cref="UiState"/> + release/
/// capture the mouse), and the rebuild-from-a-dirty-flag loop (never rebuild inside a button
/// signal — CLAUDE.md §8). Subclasses implement <see cref="BuildShell"/> once (static layout:
/// anchors, tabs, scroll areas) and <see cref="Rebuild"/> for the dynamic content, and call
/// <see cref="MarkDirty"/> whenever their data changes.
///
/// Modal panels block gameplay (the player controller holds position while
/// <see cref="UiState.MenuOpen"/>) and free the mouse; non-modal overlays (journal, map)
/// leave play untouched.
/// </summary>
public abstract partial class UiPanel : CanvasLayer
{
    /// <summary>The themed frame all content lives in. Hidden = closed.</summary>
    protected PanelContainer Shell { get; private set; } = null!;

    /// <summary>Whether opening blocks gameplay and frees the mouse.</summary>
    protected virtual bool Modal => true;

    /// <summary>Input action that toggles the panel (null = opened only via code).</summary>
    protected virtual string? ToggleAction => null;

    /// <summary>Whether ui_cancel (Esc / gamepad B) closes the panel (30.5J). Defaults to the
    /// modal contract; panels with their own lifecycle (dialogue) opt out.</summary>
    protected virtual bool CloseOnCancel => Modal;

    /// <summary>
    /// The footer legend: what the buttons do on this screen, as glyph and verb pairs, drawn in the
    /// bottom gutter beside the shell (<see cref="UiLegend"/>). Read after every rebuild, so a
    /// panel may answer differently per tab or selection. The default is the one thing every modal
    /// panel shares: cancel closes it. An override usually adds its own entries in front of
    /// <c>base.Legend</c>.
    /// </summary>
    protected virtual IReadOnlyList<LegendEntry> Legend => CloseOnCancel
        ? new[] { new LegendEntry("ui_cancel", Loc.T("ui.legend.close")) }
        : Array.Empty<LegendEntry>();

    private UiLegend _legend = null!;

    /// <summary>The process frame a panel last closed on cancel — the pause menu skips its Esc
    /// on this frame so one press never both closes a panel and opens the pause menu.</summary>
    internal static ulong LastCancelCloseFrame { get; private set; }

    /// <summary>Whether a closed panel still needs its tick. A panel with a toggle key polls for that
    /// key; one opened only from code has nothing to do while closed and stops processing entirely.
    /// A subclass that opens itself by polling (the placement HUD) must return true.</summary>
    protected virtual bool TicksWhileClosed => ToggleAction != null;

    // The toggle action as a StringName, converted once: a string passed to Input converts (and
    // allocates) on every call, and this is polled every frame.
    private StringName? _toggleName;

    // Mirrors Shell.Visible. SetOpen is its only writer, so the per-frame checks read a field
    // instead of crossing into the engine.
    private bool _open;

    private bool _dirty = true;

    // Open-transition fade (30.5I): elapsed time since the panel opened, reset per open.
    private float _openElapsed;
    private bool _fading;

    // Grab focus on the first rebuild after opening (30.5J) so gamepad/keyboard can navigate.
    private bool _focusPending;

    public bool IsOpen => _open;

    /// <summary>
    /// Releases this panel's <see cref="UiState"/> registration if it is freed while still open.
    /// <see cref="UiState"/> holds owners in a process-lifetime static, so a modal panel that leaves
    /// the tree open would sit in it forever — and because a modal is also a world-pauser, the
    /// result is a permanently paused game with no menu on screen and no way to clear it.
    ///
    /// Hooked on the notification rather than <c>_ExitTree</c> deliberately: six subclasses override
    /// <c>_ExitTree</c> to unsubscribe and would shadow a base implementation, whereas none override
    /// <c>_Notification</c>. Nothing reaches this today — the game has no quit-to-menu path, so a
    /// panel is never freed mid-session — but the guard is correct regardless of reachability: a
    /// panel that has left the tree is by definition not an open menu.
    /// </summary>
    public override void _Notification(int what)
    {
        if (what == NotificationExitTree && Modal)
        {
            UiState.Close(this);
        }
    }

    public sealed override void _Ready()
    {
        // A modal panel now pauses the tree (GameManager.RefreshPause), so the panel itself has to
        // be pause-immune or it would freeze the moment it opened — no rebuild, no input, no close.
        ProcessMode = ProcessModeEnum.Always;

        Shell = UiTheme.Panel();
        Shell.Visible = false;
        AddChild(Shell);
        BuildShell(Shell);

        // A sibling of the shell, not a child: it sits in the gutter under the frame and must not
        // take part in the frame's layout or its open fade.
        _legend = new UiLegend { Visible = false };
        AddChild(_legend);
        OnReady();

        if (ToggleAction is { } action)
        {
            _toggleName = action;
        }

        // A closed panel that nothing can open from its own tick does not tick (re-enabled in SetOpen).
        SetProcess(_open || TicksWhileClosed);
    }

    /// <summary>Builds the static layout once: anchors on <paramref name="shell"/>, padding,
    /// tab bars, scroll areas. Dynamic rows belong in <see cref="Rebuild"/>.</summary>
    protected abstract void BuildShell(PanelContainer shell);

    /// <summary>Rebuilds the dynamic content. Runs at most once per frame, only while open
    /// and dirty, and never inside a button signal.</summary>
    protected abstract void Rebuild();

    /// <summary>Post-shell setup (event subscriptions). Pair with <c>_ExitTree</c>.</summary>
    protected virtual void OnReady()
    {
    }

    /// <summary>Called after the open state changes (show/hide side effects).</summary>
    protected virtual void OnOpenChanged(bool open)
    {
    }

    public void MarkDirty() => _dirty = true;

    public void Toggle() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        if (_open == open)
        {
            return;
        }

        _open = open;
        Shell.Visible = open;
        if (open)
        {
            _legend.Set(Legend);
        }

        _legend.Visible = open;
        SetProcess(open || TicksWhileClosed);
        EventBus.Instance?.Publish(new UiPanelToggledEvent(this, open));
        if (Modal)
        {
            if (open)
            {
                UiState.Open(this);
            }
            else
            {
                UiState.Close(this);
            }

            // Free the mouse while any blocking menu is up (or outside play); recapture on close.
            bool playing = GameManager.Instance is { IsPlaying: true };
            Godot.Input.MouseMode = UiState.MenuOpen || !playing
                ? Godot.Input.MouseModeEnum.Visible
                : Godot.Input.MouseModeEnum.Captured;
        }

        if (Modal)
        {
            // Yields to the press or the cancel that caused it (UiAudioRules): one sound per action.
            UiAudio.Play(open ? UiCue.Open : UiCue.Close);
        }

        if (open)
        {
            MarkDirty();
            _focusPending = true;

            // Fade the shell in (ease-out, DurationBase); closing stays instant so dismissal
            // never lags input. Reduced motion collapses the duration to 0 (snaps opaque).
            _openElapsed = 0f;
            _fading = UiTheme.Duration(UiTheme.DurationBase) > 0f;
            Shell.Modulate = new Color(1f, 1f, 1f, _fading ? 0f : 1f);
        }

        OnOpenChanged(open);
    }

    public override void _Process(double delta)
    {
        if (_toggleName is { } action && Godot.Input.IsActionJustPressed(action))
        {
            Toggle();
        }

        if (!_open)
        {
            return;
        }

        if (CloseOnCancel && Godot.Input.IsActionJustPressed(UiLive.UiCancel))
        {
            LastCancelCloseFrame = Engine.GetProcessFrames();
            UiAudio.Play(UiCue.Back);
            SetOpen(false);
            return;
        }

        if (_dirty)
        {
            _dirty = false;

            // A rebuild frees every dynamic row; if focus was on one (controller/keyboard
            // navigation), restore it to the same spot in the new tree (30.5J).
            int[]? focusPath = UiFocus.PathOf(Shell);
            Rebuild();
            _legend.Set(Legend);
            if (_focusPending)
            {
                _focusPending = false;
                UiFocus.GrabFirst(Shell);
            }
            else
            {
                UiFocus.Restore(Shell, focusPath);
            }
        }

        if (_fading)
        {
            // Settles opaque even if reduced motion flips mid-fade (Duration collapses to 0).
            _openElapsed += (float)delta;
            float alpha = UiMotion.EaseOut(UiMotion.Progress(_openElapsed, UiTheme.Duration(UiTheme.DurationBase)));
            Shell.Modulate = new Color(1f, 1f, 1f, alpha);
            _fading = alpha < 1f;
        }
    }
}

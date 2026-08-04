using Embervale.Core;
using Embervale.Core.Events;
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

    /// <summary>The process frame a panel last closed on cancel — the pause menu skips its Esc
    /// on this frame so one press never both closes a panel and opens the pause menu.</summary>
    internal static ulong LastCancelCloseFrame { get; private set; }

    private bool _dirty = true;

    // Open-transition fade (30.5I): elapsed time since the panel opened, reset per open.
    private float _openElapsed;

    // Grab focus on the first rebuild after opening (30.5J) so gamepad/keyboard can navigate.
    private bool _focusPending;

    public bool IsOpen => Shell.Visible;

    public sealed override void _Ready()
    {
        // A modal panel now pauses the tree (GameManager.RefreshPause), so the panel itself has to
        // be pause-immune or it would freeze the moment it opened — no rebuild, no input, no close.
        ProcessMode = ProcessModeEnum.Always;

        Shell = UiTheme.Panel();
        Shell.Visible = false;
        AddChild(Shell);
        BuildShell(Shell);
        OnReady();
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
        if (Shell.Visible == open)
        {
            return;
        }

        Shell.Visible = open;
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

        if (open)
        {
            MarkDirty();
            _focusPending = true;

            // Fade the shell in (ease-out, DurationBase); closing stays instant so dismissal
            // never lags input. Reduced motion collapses the duration to 0 (snaps opaque).
            _openElapsed = 0f;
            Shell.Modulate = new Color(1f, 1f, 1f, UiTheme.Duration(UiTheme.DurationBase) > 0f ? 0f : 1f);
        }

        OnOpenChanged(open);
    }

    public override void _Process(double delta)
    {
        if (ToggleAction is { } action && Godot.Input.IsActionJustPressed(action))
        {
            Toggle();
        }

        if (IsOpen && CloseOnCancel && Godot.Input.IsActionJustPressed("ui_cancel"))
        {
            LastCancelCloseFrame = Engine.GetProcessFrames();
            SetOpen(false);
        }

        if (Shell.Visible && _dirty)
        {
            _dirty = false;

            // A rebuild frees every dynamic row; if focus was on one (controller/keyboard
            // navigation), restore it to the same spot in the new tree (30.5J).
            int[]? focusPath = UiFocus.PathOf(Shell);
            Rebuild();
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

        if (Shell.Visible && Shell.Modulate.A < 1f)
        {
            // Settles opaque even if reduced motion flips mid-fade (Duration collapses to 0).
            _openElapsed += (float)delta;
            float alpha = UiMotion.EaseOut(UiMotion.Progress(_openElapsed, UiTheme.Duration(UiTheme.DurationBase)));
            Shell.Modulate = new Color(1f, 1f, 1f, alpha);
        }
    }
}

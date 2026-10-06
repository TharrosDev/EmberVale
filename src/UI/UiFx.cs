using System;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The shared motion verbs: how a thing arrives (<see cref="FadeIn"/>, <see cref="Rise"/>,
/// <see cref="Stagger"/>), leaves (<see cref="FadeOut"/>), asks for a glance (<see cref="Pulse"/>)
/// and is confirmed by holding (<see cref="HoldRing"/>). One vocabulary, so a screen does not
/// invent its own timings.
///
/// Every helper follows the rules <see cref="UiTheme.AnimateModulate"/> set: a new run on a node
/// kills the one in flight (tracked in node meta), the tween keeps running while the tree is
/// paused and ignores <c>Engine.TimeScale</c> (hit-stop must not slow a menu), and every duration
/// goes through <see cref="UiTheme.Duration"/>, so reduced motion lands on the final state at once.
///
/// ⚠️ Call these <b>after</b> the node is in the tree. A tween cannot be created outside it, so a
/// detached node is simply put in its final state.
///
/// ⚠️ Fades ride <c>modulate</c> alpha. That is right for a container arriving and wrong for text
/// colour (UI_STYLE §2: modulate multiplies onto dim fonts); leave a fade at 1 when it is done,
/// which every helper here does.
///
/// Closing a menu stays instant (UI_STYLE §5). <see cref="FadeOut"/> is for exits that reveal
/// something, and for transient widgets leaving on their own.
/// </summary>
public static class UiFx
{
    private const string TweenMeta = "ui_fx_tween";
    private const string PulseMeta = "ui_fx_pulse";
    private const string RestMeta = "ui_fx_rest_y";

    /// <summary>How far below its place a rising control starts, in px.</summary>
    public const float RiseDistance = 12f;

    /// <summary>Delay between consecutive items of a <see cref="Stagger"/>.</summary>
    public const float StaggerStep = 0.035f;

    /// <summary>Items past this one arrive together, so a sixty-row list is not a two-second wait.</summary>
    public const int StaggerCap = 10;

    /// <summary>How long an irreversible action must be held.</summary>
    public const float HoldSeconds = 0.9f;

    /// <summary>Whether hold-to-confirm prompts fire on a single press (the accessibility setting).</summary>
    public static bool HoldsToPresses =>
        ServiceLocator.Instance is { } locator &&
        locator.TryGet(out SettingsService settings) &&
        settings.Current.HoldsToPresses;

    /// <summary>Shows the node and eases it from transparent to opaque.</summary>
    public static void FadeIn(CanvasItem node, float seconds = UiTheme.DurationBase, float delay = 0f)
    {
        Kill(node);
        node.Visible = true;

        float duration = UiTheme.Duration(seconds);
        if (duration <= 0f || !node.IsInsideTree())
        {
            SetAlpha(node, 1f);
            return;
        }

        SetAlpha(node, 0f);
        Tween tween = Begin(node, TweenMeta);
        Wait(tween, delay);
        tween.TweenProperty(node, "modulate:a", 1f, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
    }

    /// <summary>
    /// Eases the node to transparent, hides it, then calls <paramref name="then"/> (free it, swap
    /// the page, show what was underneath). Under reduced motion, or outside the tree, all three
    /// happen before this returns. The node keeps taking input while it fades; a caller that minds
    /// disables it first. If the node is freed mid-fade the tween dies with it and
    /// <paramref name="then"/> is not called.
    /// </summary>
    public static void FadeOut(CanvasItem node, Action? then = null, float seconds = UiTheme.DurationFast)
    {
        Kill(node);

        float duration = UiTheme.Duration(seconds);
        if (duration <= 0f || !node.IsInsideTree() || !node.Visible)
        {
            node.Visible = false;
            SetAlpha(node, 1f);
            then?.Invoke();
            return;
        }

        Tween tween = Begin(node, TweenMeta);
        tween.TweenProperty(node, "modulate:a", 0f, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.In);
        tween.TweenCallback(Callable.From(() =>
        {
            // Hidden, and opaque again: the next plain `Visible = true` must not show nothing.
            node.Visible = false;
            SetAlpha(node, 1f);
            then?.Invoke();
        }));
    }

    /// <summary>
    /// <see cref="FadeIn"/> with a short climb into place: the entrance for a sheet's column, a
    /// card, a banner line.
    ///
    /// The control's resting position is read on the tween's first step, not here, because a
    /// control added to a container this frame has not been laid out yet. A container that
    /// re-sorts mid-climb puts the control back at rest and the next step lifts it again; the
    /// container is asked to sort once more at the end so the final position is always its own.
    /// </summary>
    public static void Rise(Control node, float distance = RiseDistance, float seconds = UiTheme.DurationBase, float delay = 0f)
    {
        Kill(node);
        node.Visible = true;

        float duration = UiTheme.Duration(seconds);
        if (duration <= 0f || !node.IsInsideTree())
        {
            SetAlpha(node, 1f);
            return;
        }

        SetAlpha(node, 0f);
        Tween tween = Begin(node, TweenMeta);
        Wait(tween, delay);
        tween.TweenProperty(node, "modulate:a", 1f, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);

        bool captured = false;
        float rest = 0f;
        tween.Parallel().TweenMethod(
            Callable.From<float>(t =>
            {
                if (!captured)
                {
                    captured = true;
                    rest = node.Position.Y;
                    node.SetMeta(RestMeta, rest);
                }

                node.Position = new Vector2(node.Position.X, rest + (distance * (1f - t)));
            }),
            0f, 1f, duration)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenCallback(Callable.From(() => Settle(node)));
    }

    /// <summary>
    /// <see cref="Rise"/> for each visible <see cref="Control"/> child of
    /// <paramref name="container"/> in order, each a beat after the last. For a menu's entries or
    /// the cards of a page arriving; not for a list that rebuilds while the player works in it.
    /// </summary>
    public static void Stagger(Node container, float step = StaggerStep, float seconds = UiTheme.DurationBase, float distance = 8f)
    {
        int index = 0;
        foreach (Node child in container.GetChildren())
        {
            if (child is not Control { Visible: true } control)
            {
                continue;
            }

            Rise(control, distance, seconds, Math.Min(index, StaggerCap) * step);
            index++;
        }
    }

    /// <summary>
    /// One swell and settle about the control's centre: a count that just changed, a slot that
    /// just came off cooldown. Nothing at all under reduced motion, so it must never be the only
    /// sign that something happened. A container resets its children's scale when it sorts, which
    /// can cut a pulse short and never leaves one stuck.
    /// </summary>
    public static void Pulse(Control node, float peak = 1.08f, float seconds = UiTheme.DurationBase)
    {
        if (node.HasMeta(PulseMeta) && node.GetMeta(PulseMeta).As<Tween>() is { } previous && previous.IsValid())
        {
            previous.Kill();
        }

        node.Scale = Vector2.One;

        float duration = UiTheme.Duration(seconds);
        if (duration <= 0f || !node.IsInsideTree())
        {
            return;
        }

        node.PivotOffset = node.Size * 0.5f;
        Tween tween = Begin(node, PulseMeta);
        tween.TweenProperty(node, "scale", new Vector2(peak, peak), duration * 0.4f)
            .SetTrans(Tween.TransitionType.Cubic)
            .SetEase(Tween.EaseType.Out);
        tween.TweenProperty(node, "scale", Vector2.One, duration * 0.6f)
            .SetTrans(Tween.TransitionType.Sine)
            .SetEase(Tween.EaseType.InOut);
    }

    /// <summary>
    /// A hold-to-confirm ring <paramref name="size"/> px square that calls
    /// <paramref name="onComplete"/> once it has been held for <paramref name="seconds"/>. Drive it
    /// with <see cref="UI.HoldRing.Attach"/> (a button), <see cref="UI.HoldRing.HoldAction"/> (an
    /// input action) or <see cref="UI.HoldRing.Press"/>/<see cref="UI.HoldRing.Release"/>. With
    /// the <see cref="HoldsToPresses"/> setting on, one press completes it.
    /// </summary>
    public static HoldRing HoldRing(Action onComplete, float size = 22f, float seconds = HoldSeconds) => new()
    {
        Completed = onComplete,
        Seconds = seconds,
        CustomMinimumSize = new Vector2(size, size),
        SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
    };

    // --- Plumbing -----------------------------------------------------------

    private static Tween Begin(Node node, string meta)
    {
        Tween tween = node.CreateTween();
        tween.SetPauseMode(Tween.TweenPauseMode.Process);
        tween.SetIgnoreTimeScale(true);
        node.SetMeta(meta, tween);
        return tween;
    }

    private static void Wait(Tween tween, float delay)
    {
        float wait = UiTheme.Duration(delay);
        if (wait > 0f)
        {
            tween.TweenInterval(wait);
        }
    }

    /// <summary>Stops the fade or rise in flight on a node and puts a half-risen control back.</summary>
    private static void Kill(CanvasItem node)
    {
        if (node.HasMeta(TweenMeta) && node.GetMeta(TweenMeta).As<Tween>() is { } previous && previous.IsValid())
        {
            previous.Kill();
        }

        Settle(node);
    }

    /// <summary>Returns a risen control to the place it was lifted from. Inside a container the
    /// container's own layout is the authority, so it is asked to sort again.</summary>
    private static void Settle(CanvasItem node)
    {
        if (node is not Control control || !control.HasMeta(RestMeta))
        {
            return;
        }

        control.Position = new Vector2(control.Position.X, control.GetMeta(RestMeta).AsSingle());
        control.RemoveMeta(RestMeta);
        if (control.GetParent() is Container parent)
        {
            parent.QueueSort();
        }
    }

    private static void SetAlpha(CanvasItem node, float alpha) => node.Modulate = node.Modulate with { A = alpha };
}

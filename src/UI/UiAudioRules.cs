using System;

namespace Embervale.UI;

/// <summary>The sounds the interface makes. One cue per meaning, so a player learns what they
/// heard without looking: a press, a move, a yes, a no, a way back.</summary>
public enum UiCue
{
    /// <summary>A plain button press: what every <c>UiTheme.Action</c> plays unless told otherwise.</summary>
    Click,

    /// <summary>Focus moved under a stick, d-pad or arrow key.</summary>
    Focus,

    /// <summary>A choice was committed: start, save, buy, a hold completing.</summary>
    Confirm,

    /// <summary>A step back out: cancel, close on Esc or B.</summary>
    Back,

    /// <summary>A tab, sub-tab or hub screen changed.</summary>
    Tab,

    /// <summary>A screen opened.</summary>
    Open,

    /// <summary>A screen closed.</summary>
    Close,

    /// <summary>The press did nothing: cannot afford, locked, unavailable.</summary>
    Denied,

    /// <summary>One step of a hold-to-confirm filling.</summary>
    HoldTick,
}

/// <summary>
/// The decisions behind <see cref="UiAudio"/>, free of the engine so they are unit-tested: which
/// stream a cue plays, which of two cues asked for at once is heard, when a focus move is worth a
/// tick, and which voice takes the next sound.
/// </summary>
public static class UiAudioRules
{
    /// <summary>Voices in the pool. Four is a press, the screen it opened, a focus tick and a tail.</summary>
    public const int VoiceCount = 4;

    /// <summary>Cues asked for within this long of one another are one event (a press and the
    /// screen it opens, a cancel and the panel it closes), and only the one that means most sounds.</summary>
    public const double CoalesceSeconds = 0.05;

    /// <summary>The fastest focus ticks may repeat. A held stick walks a list at about ten rows a
    /// second; every row ticking is a rattle.</summary>
    public const double FocusIntervalSeconds = 0.07;

    /// <summary>Steps a hold-to-confirm ticks on its way to full.</summary>
    public const int HoldSteps = 4;

    /// <summary>The <c>AudioLibrary</c> cue a UI cue plays.</summary>
    public static string CueId(UiCue cue) => cue switch
    {
        UiCue.Focus => "ui.focus",
        UiCue.Confirm => "ui.confirm",
        UiCue.Back => "ui.back",
        UiCue.Tab => "ui.tab",
        UiCue.Open => "ui.open",
        UiCue.Close => "ui.close",
        UiCue.Denied => "ui.denied",
        UiCue.HoldTick => "ui.hold_tick",
        _ => "ui.click",
    };

    /// <summary>Which cue wins when several are asked for together. A refusal must never be
    /// masked; a deliberate press outranks the screen change it caused; a focus tick yields to
    /// everything.</summary>
    public static int Priority(UiCue cue) => cue switch
    {
        UiCue.Denied => 7,
        UiCue.Confirm => 6,
        UiCue.Tab => 5,
        UiCue.Back => 4,
        UiCue.Click => 3,
        UiCue.Open => 2,
        UiCue.Close => 1,
        _ => 0,
    };

    /// <summary>
    /// Whether <paramref name="cue"/> should sound at <paramref name="now"/> (seconds, any
    /// monotonic clock), given the last cue that did. Inside the coalesce window only a cue that
    /// outranks the last one is heard; a hold tick is its own gauge and always is.
    /// </summary>
    public static bool ShouldPlay(UiCue cue, double now, UiCue? last, double lastAt)
    {
        if (cue == UiCue.HoldTick || last is not { } previous)
        {
            return true;
        }

        double since = now - lastAt;
        if (since < 0d || since >= CoalesceSeconds)
        {
            return true;
        }

        return Priority(cue) > Priority(previous);
    }

    /// <summary>
    /// Whether a focus change earns a tick. Only when the player moved it (<paramref
    /// name="navigating"/>: a direction or tab action is down): a rebuild restoring focus, a screen
    /// grabbing its first control and a mouse click all move focus too, and none of them is a step.
    /// </summary>
    public static bool ShouldTickFocus(bool navigating, double now, double lastFocusAt)
    {
        if (!navigating)
        {
            return false;
        }

        double since = now - lastFocusAt;
        return since < 0d || since >= FocusIntervalSeconds;
    }

    /// <summary>The hold step <paramref name="progress"/> (0..1) has reached, 0 to <see cref="HoldSteps"/>.</summary>
    public static int HoldStep(float progress) =>
        (int)MathF.Floor(Math.Clamp(progress, 0f, 1f) * HoldSteps);

    /// <summary>Whether a hold moving from <paramref name="before"/> to <paramref name="after"/>
    /// crossed a step on the way up. The last step is the completion, which has its own cue.</summary>
    public static bool HoldTicks(float before, float after)
    {
        int step = HoldStep(after);
        return step > HoldStep(before) && step < HoldSteps;
    }

    /// <summary>Pitch of the tick at <paramref name="progress"/>: it climbs as the ring closes, so
    /// the hold can be heard arriving.</summary>
    public static float HoldPitch(float progress) => 1f + (0.12f * HoldStep(progress));

    /// <summary>
    /// The voice for the next sound: the first idle one after <paramref name="last"/>, or failing
    /// that the one after <paramref name="last"/> (the oldest), which is cut off.
    /// </summary>
    public static int NextVoice(ReadOnlySpan<bool> busy, int last)
    {
        int count = busy.Length;
        if (count == 0)
        {
            return -1;
        }

        int start = ((last % count) + count + 1) % count;
        for (int i = 0; i < count; i++)
        {
            int index = (start + i) % count;
            if (!busy[index])
            {
                return index;
            }
        }

        return start;
    }
}

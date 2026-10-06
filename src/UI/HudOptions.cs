using System;
using System.Collections.Generic;
using Embervale.Settings;

namespace Embervale.UI;

/// <summary>
/// One entry per HUD element the player can show, hide or leave to the game.
///
/// ⚠️ <b>Append-only.</b> The ordinal is the index into the saved <c>Settings.HudElementModes</c>
/// list, so a reorder or an insert silently moves every player's choices onto the wrong element
/// (<c>EnumStabilityTests</c> pins the numbers).
/// </summary>
public enum HudElement
{
    Vitals = 0,
    Hotbar = 1,
    Compass = 2,
    Minimap = 3,
    Clock = 4,
    QuestTracker = 5,
    Party = 6,
    TargetPlate = 7,
    EnemyPlates = 8,
    DamageNumbers = 9,
    Prompts = 10,
    Crosshair = 11,
    Toasts = 12,
    Subtitles = 13,
}

/// <summary>How one HUD element behaves. ⚠️ Append-only: the ordinal is what is saved.</summary>
public enum HudElementMode
{
    /// <summary>Shown whenever the HUD mode allows it. Today's behaviour, and the default.</summary>
    Always = 0,

    /// <summary>Shown while it has something to say (see <see cref="HudDynamicRules"/>), and while
    /// the recall input is held.</summary>
    Dynamic = 1,

    /// <summary>Never shown, recall included.</summary>
    Hidden = 2,
}

/// <summary>The named sets of element modes the settings screen offers. Not saved: the saved fact is
/// the per-element list, and the preset shown is whichever one that list matches.</summary>
public enum HudPreset
{
    Full = 0,
    Dynamic = 1,
    Minimal = 2,

    /// <summary>The saved list matches no preset.</summary>
    Custom = 3,
}

/// <summary>What the game is doing, as far as one Dynamic element is concerned.</summary>
/// <param name="InCombat">A blow involving the player landed recently, a target is locked, or a boss is up.</param>
/// <param name="BelowMax">Health, stamina or mana is not full.</param>
/// <param name="RecentlyChanged">This element's own content changed a moment ago.</param>
/// <param name="RecallHeld">The player is holding the recall input.</param>
/// <param name="MenuOpen">A blocking menu is open over the HUD.</param>
public readonly record struct HudSignals(
    bool InCombat, bool BelowMax, bool RecentlyChanged, bool RecallHeld, bool MenuOpen);

/// <summary>The preset tables and the conversion to and from the saved list. Pure.</summary>
public static class HudPresets
{
    public static readonly int ElementCount = Enum.GetValues<HudElement>().Length;

    /// <summary>The mode of every element under <paramref name="preset"/>, indexed by
    /// <see cref="HudElement"/>. <see cref="HudPreset.Custom"/> has no table and returns Full's.</summary>
    public static HudElementMode[] Modes(HudPreset preset)
    {
        var modes = new HudElementMode[ElementCount];
        for (int i = 0; i < modes.Length; i++)
        {
            modes[i] = Mode(preset, (HudElement)i);
        }

        return modes;
    }

    /// <summary>
    /// Dynamic steps back everything that reports a steady state and leaves alone everything that
    /// only appears when it has something to say. Minimal also removes the navigation aids and the
    /// numbers, and keeps what a player cannot play without: prompts, subtitles, notices and the
    /// plate of the thing being fought.
    /// </summary>
    public static HudElementMode Mode(HudPreset preset, HudElement element) => preset switch
    {
        HudPreset.Dynamic => element switch
        {
            HudElement.Vitals or HudElement.Hotbar or HudElement.Compass or HudElement.Minimap
                or HudElement.Clock or HudElement.QuestTracker or HudElement.Party
                or HudElement.Crosshair => HudElementMode.Dynamic,
            _ => HudElementMode.Always,
        },
        HudPreset.Minimal => element switch
        {
            HudElement.Vitals or HudElement.Hotbar or HudElement.Crosshair => HudElementMode.Dynamic,
            HudElement.TargetPlate or HudElement.Prompts or HudElement.Toasts
                or HudElement.Subtitles => HudElementMode.Always,
            _ => HudElementMode.Hidden,
        },
        _ => HudElementMode.Always,
    };

    /// <summary>The modes a saved list means. A short list, a missing one and a number that is not a
    /// mode all read as Always, so a file written before an element existed shows that element.</summary>
    public static HudElementMode[] FromSaved(int[]? saved)
    {
        var modes = new HudElementMode[ElementCount];
        for (int i = 0; i < modes.Length; i++)
        {
            modes[i] = (HudElementMode)SettingsMath.HudElementMode(saved, i);
        }

        return modes;
    }

    /// <summary>The list to save for <paramref name="modes"/>.</summary>
    public static int[] ToSaved(IReadOnlyList<HudElementMode> modes)
    {
        var saved = new int[ElementCount];
        for (int i = 0; i < saved.Length && i < modes.Count; i++)
        {
            saved[i] = (int)modes[i];
        }

        return saved;
    }

    /// <summary>The preset <paramref name="modes"/> matches, or Custom.</summary>
    public static HudPreset Detect(IReadOnlyList<HudElementMode> modes)
    {
        foreach (HudPreset preset in new[] { HudPreset.Full, HudPreset.Dynamic, HudPreset.Minimal })
        {
            bool match = modes.Count == ElementCount;
            for (int i = 0; match && i < ElementCount; i++)
            {
                match = modes[i] == Mode(preset, (HudElement)i);
            }

            if (match)
            {
                return preset;
            }
        }

        return HudPreset.Custom;
    }

    public static HudPreset Detect(int[]? saved) => Detect(FromSaved(saved));
}

/// <summary>
/// Whether one HUD element is showing, given its mode and what the game is doing. Pure.
///
/// This is one of two gates. The other is <see cref="HudVisibility"/>, which says what a HUD mode
/// (exploration, menu, cinematic) allows; an element shows only when both say yes.
/// </summary>
public static class HudDynamicRules
{
    /// <summary>How long a blow keeps the HUD in its combat reading after it lands.</summary>
    public const double CombatLingerSeconds = 6.0;

    /// <summary>How long an element stays up after its own content changed.</summary>
    public const double ChangeLingerSeconds = 4.0;

    public static bool Visible(HudElement element, HudElementMode mode, HudSignals signals) => mode switch
    {
        HudElementMode.Hidden => false,
        HudElementMode.Dynamic => signals.RecallHeld || Wanted(element, signals),
        _ => true,
    };

    /// <summary>Whether something stamped at <paramref name="stampedAt"/> still counts at
    /// <paramref name="now"/> (both in seconds on one clock).</summary>
    public static bool Lingering(double now, double stampedAt, double seconds) =>
        now >= stampedAt && now - stampedAt < seconds;

    private static bool Wanted(HudElement element, HudSignals s) => element switch
    {
        // An inventory is where a potion gets drunk, so the bars come back under a menu.
        HudElement.Vitals => s.InCombat || s.BelowMax || s.RecentlyChanged || s.MenuOpen,
        HudElement.Hotbar => s.InCombat || s.RecentlyChanged || s.MenuOpen,
        HudElement.Party => s.InCombat || s.BelowMax || s.RecentlyChanged,
        HudElement.Crosshair or HudElement.TargetPlate or HudElement.EnemyPlates =>
            s.InCombat || s.RecentlyChanged,

        // Navigation and the clock report a steady state: they appear when they change, and on recall.
        HudElement.Compass or HudElement.Minimap or HudElement.Clock or HudElement.QuestTracker =>
            s.RecentlyChanged,

        // Already transient: each of these is only ever on screen because something just happened.
        _ => true,
    };
}

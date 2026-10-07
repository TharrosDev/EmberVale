using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Stats;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The caster the spell-wheel and spellbook screenshots are taken of (<c>--hudshots</c>,
/// <c>--panelshots</c>): the player, taught every spell a player can learn, with a prepared spell
/// and a previous one. Everything goes through <see cref="SpellcastingComponent"/>'s own API, and
/// <see cref="Restore"/> puts back what the save held through its own <c>Load</c>, so the shots
/// after these are taken of the save's caster and not this one.
/// Lives in a <c>*Shots.cs</c> file because the shipping build excludes that pattern.
/// </summary>
internal static class WheelShotFixtures
{
    // The player's spells as the save had them, while the shots hold the staged roster.
    private static Godot.Collections.Dictionary? _held;

    public static SpellcastingComponent? Caster() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<SpellcastingComponent>()
            : null;

    /// <summary>How many spells a player can learn: what "knows them all" means.</summary>
    public static int LearnableCount()
    {
        int count = 0;
        foreach (SpellResource spell in SpellDatabase.All)
        {
            if (spell.PlayerLearnable)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Teaches the player every learnable spell, then prepares the first favourite with
    /// the second as the spell a tap swaps back to. <see cref="SpellcastingComponent.Teach"/> skips
    /// the corruption gate on learning, so the corrupted spells are known and show their padlock.</summary>
    public static void TeachEverything()
    {
        if (Caster() is not { } caster)
        {
            return;
        }

        _held ??= caster.Save();
        foreach (SpellResource spell in SpellDatabase.All)
        {
            if (spell.PlayerLearnable)
            {
                caster.Teach(spell);
            }
        }

        string first = string.Empty;
        string second = string.Empty;
        foreach (string id in caster.Favourites)
        {
            if (id.Length == 0)
            {
                continue;
            }

            if (first.Length == 0)
            {
                first = id;
            }
            else if (second.Length == 0)
            {
                second = id;
            }
        }

        if (second.Length > 0)
        {
            caster.Select(second);
        }

        if (first.Length > 0)
        {
            caster.Select(first);
        }

        Announce(caster);
    }

    /// <summary>The cursor, in wheel units, that sits on favourite <paramref name="slot"/>.</summary>
    public static Vector2 FavouritePoint(int slot) =>
        SpellWheelMetrics.Direction(SpellWheelMetrics.WedgeCentre(slot, SpellFavouritesRules.SlotCount))
        * SpellWheelMetrics.FavouriteRadius;

    /// <summary>
    /// Puts the favourite with the longest cooldown part-way through it and returns its slot, or -1
    /// when no favourite has one. There is no public way to start a cooldown without a cast, so it
    /// is written the way a save restores one: the component's own state, loaded back.
    /// </summary>
    public static int Cool()
    {
        if (Caster() is not { } caster)
        {
            return -1;
        }

        int slot = -1;
        SpellResource? cooling = null;
        for (int i = 0; i < caster.Favourites.Count; i++)
        {
            if (caster.Favourites[i].Length > 0 && SpellDatabase.Get(caster.Favourites[i]) is { } spell &&
                spell.Cooldown > (cooling?.Cooldown ?? 0f))
            {
                slot = i;
                cooling = spell;
            }
        }

        if (cooling == null)
        {
            return -1;
        }

        // Past the half-second the harness holds a state before it photographs it.
        Godot.Collections.Dictionary state = caster.Save();
        state["cooldowns"] = new Godot.Collections.Dictionary
        {
            [cooling.Id] = (double)Mathf.Max(cooling.Cooldown * 0.6f, 3f),
        };
        caster.Load(state);
        return slot;
    }

    /// <summary>Empties the player's mana and returns a favourite slot, other than
    /// <paramref name="not"/>, whose spell then cannot be afforded; -1 when none costs mana.</summary>
    public static int Unaffordable(int not)
    {
        if (Caster() is not { } caster || caster.Entity?.GetComponent<StatsComponent>() is not { } stats)
        {
            return -1;
        }

        stats.SetCurrent(StatType.Mana, 0f);
        for (int i = 0; i < caster.Favourites.Count; i++)
        {
            if (i != not && caster.Favourites[i].Length > 0 && SpellDatabase.Get(caster.Favourites[i]) is { } spell &&
                caster.EffectiveManaCost(spell) > 0f)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Puts the player's spells back to what the save held.</summary>
    public static void Restore()
    {
        if (_held != null && Caster() is { } caster)
        {
            caster.Load(_held);
            Announce(caster);
        }

        _held = null;
    }

    // Teach and Load change the roster without saying so; the open spellbook listens for this.
    private static void Announce(SpellcastingComponent caster)
    {
        if (caster.Entity is { } entity)
        {
            EventBus.Instance?.Publish(new SpellsChangedEvent(entity));
        }
    }
}

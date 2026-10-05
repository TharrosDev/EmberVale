using System;
using Embervale.Corruption;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The magic half of the perks probe in <c>--lifecycle</c>: the four authored Mage perks reach
/// <see cref="PerkQuery"/> through the real <c>.tres</c> parse (kind, school qualifier, rank scaling), the
/// live <see cref="SpellcastingComponent"/> prices a real spell with them and lets a cast through at a mana
/// level it refuses without them, and an empty-save <c>Load</c> takes it all away again. Unit tests cannot
/// reach this: the components are Godot nodes.
/// </summary>
internal static class MagicPerksProbe
{
    private static readonly string[] PerkIds = { "perk.thrift", "perk.elementalist", "perk.channeler", "perk.archmage" };

    public static void Verify(PlayerCharacter player, PerksComponent perks, Action<bool, string> check)
    {
        foreach (string id in PerkIds)
        {
            if (PerkDatabase.Get(id) is not { } perk || !perks.GrantFree(perk))
            {
                check(false, $"magic perks probe: perk '{id}' is unauthored or could not be granted.");
                perks.Load(new Godot.Collections.Dictionary());
                return;
            }

            while (perks.GrantFree(perk))
            {
            }
        }

        check(Near(PerkQuery.Factor(player, PerkEffectKind.ManaCostMult), 0.85f), "magic perks probe: thrift did not give 0.85 mana cost.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.SchoolPowerBonus, nameof(Combat.DamageType.Fire)), 0.22f),
            "magic perks probe: elementalist and archmage did not give +22% fire power.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.SchoolPowerBonus, nameof(Combat.DamageType.Arcane)), 0.10f),
            "magic perks probe: archmage alone did not give +10% arcane power.");
        check(Near(PerkQuery.Of(player, PerkEffectKind.SpellCritBonus), 0.09f), "magic perks probe: channeler did not give +9% spell crit.");

        VerifyCost(player, check);

        perks.Load(new Godot.Collections.Dictionary());
        check(Near(PerkQuery.Factor(player, PerkEffectKind.ManaCostMult), 1f)
            && Near(PerkQuery.Of(player, PerkEffectKind.SchoolPowerBonus, nameof(Combat.DamageType.Fire)), 0f)
            && perks.Effects.IsEmpty,
            "magic perks probe: Load of an empty save kept magic perk effects.");
    }

    /// <summary>The live component's cost for a real fire spell is the sheet cost times the Weave times 0.85,
    /// and a mana level between the perked and the sheet cost lets a cast begin only with the perks.</summary>
    private static void VerifyCost(PlayerCharacter player, Action<bool, string> check)
    {
        SpellResource? spell = null;
        foreach (SpellResource candidate in SpellDatabase.All)
        {
            if (candidate.School == Combat.DamageType.Fire && candidate.ManaCost >= 10f
                && candidate.MinCorruptionTier == CorruptionTier.Untainted && candidate.HealthCost <= 0f
                && candidate.BlinkDistance <= 0f)
            {
                spell = candidate;
                break;
            }
        }

        if (spell == null || player.GetComponent<SpellcastingComponent>() is not { } casting
            || player.GetComponent<StatsComponent>() is not { } stats)
        {
            check(false, "magic perks probe: no plain fire spell, or the player has no spellcasting or stats.");
            return;
        }

        float expected = spell.ManaCost * Weave.CostMultiplier(false) * 0.85f;
        check(Near(casting.EffectiveManaCost(spell), expected),
            $"magic perks probe: {spell.Id} costs {casting.EffectiveManaCost(spell):0.###}, not {expected:0.###} with thrift.");

        float mana = stats.GetCurrent(StatType.Mana);
        stats.SetCurrent(StatType.Mana, stats.GetMax(StatType.Mana));
        bool baseline = casting.CanCast(spell);
        float between = expected + ((spell.ManaCost * Weave.CostMultiplier(false) - expected) / 2f);
        stats.SetCurrent(StatType.Mana, between);
        bool perked = casting.CanCast(spell);
        stats.SetCurrent(StatType.Mana, mana);
        if (!baseline)
        {
            check(false, $"magic perks probe: {spell.Id} cannot be cast even at full mana, so the cost check is void.");
            return;
        }

        check(perked, "magic perks probe: a cast the perked cost affords was refused.");
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
}

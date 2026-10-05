using System;
using Embervale.Core;
using Embervale.Items;
using Embervale.Player;
using Embervale.Progression;
using Embervale.Stats;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The perk half of <c>--lifecycle</c>: drives the real <see cref="PerksComponent"/> in a live session
/// (learn, a refused respec, a paid respec, relearn, a free rank), lets the probe save it, and after the
/// reload checks every v2 key came back and that <c>Load</c> replaces live state when the saved keys are
/// absent. Unit tests cannot reach this: the component is a Godot node.
/// </summary>
internal static class PerksLifecycleProbe
{
    private const string Might = "perk.might";
    private const string Toughness = "perk.toughness";
    private const string Endurance = "perk.endurance_training";

    private static float _expectedPower;

    /// <summary>Learns, respecs and relearns on the live player, leaving 3 Might, 1 Toughness and 1 free
    /// Endurance Training, 4 points spent and one respec taken.</summary>
    public static void Drive(PlayerCharacter? player, Action<bool, string> check)
    {
        if (player?.GetComponent<PerksComponent>() is not { } perks
            || player.GetComponent<ProgressionComponent>() is not { } progression
            || player.GetComponent<InventoryComponent>() is not { } pack
            || player.GetComponent<StatsComponent>() is not { } stats
            || PerkDatabase.Get(Might) is not { } might
            || PerkDatabase.Get(Toughness) is not { } toughness
            || PerkDatabase.Get(Endurance) is not { } endurance
            || ItemDatabase.Get(GameIds.Currency.Gold) is not { } gold)
        {
            check(false, "perks probe: the player lacks perks, progression, inventory or stats, or a legacy perk or gold is unauthored.");
            return;
        }

        float basePower = stats.GetValue(StatType.PhysicalPower);
        int pointsBefore = progression.SkillPoints;
        int goldBefore = pack.CountOf(gold);
        if (goldBefore > 0)
        {
            pack.RemoveItem(gold, goldBefore); // so the first respec below really is unaffordable
        }

        progression.RefundSkillPoints(6);

        check(perks.Learn(might) && perks.Learn(might) && perks.Learn(toughness), "perks probe: could not learn with points in hand.");
        check(perks.PointsSpent == 3 && progression.SkillPoints == pointsBefore + 3, "perks probe: points spent were not recorded.");
        check(Near(stats.GetValue(StatType.PhysicalPower), basePower + (2f * might.ValuePerRank)), "perks probe: learned ranks did not reach the stat.");

        int cost = perks.RespecCost;
        check(cost == RespecRules.Cost(3, 0) && cost > 0, "perks probe: respec cost is not the rule's.");
        check(!perks.Respec(pack) && perks.PointsSpent == 3 && perks.RespecCount == 0,
            "perks probe: a respec went through without the gold.");

        pack.AddItem(gold, cost);
        check(perks.Respec(pack), "perks probe: a paid respec was refused.");
        check(pack.CountOf(gold) == 0, "perks probe: respec did not charge exactly its cost.");
        if (goldBefore > 0)
        {
            pack.AddItem(gold, goldBefore);
        }

        check(perks.PointsSpent == 0 && perks.RespecCount == 1 && perks.RankOf(Might) == 0 && progression.SkillPoints == pointsBefore + 6,
            "perks probe: respec did not refund every point.");
        check(Near(stats.GetValue(StatType.PhysicalPower), basePower), "perks probe: respec left a stat modifier behind.");

        check(perks.Learn(might) && perks.Learn(might) && perks.Learn(might) && perks.Learn(toughness), "perks probe: could not relearn after a respec.");
        check(perks.GrantFree(endurance) && perks.FreeRankOf(Endurance) == 1, "perks probe: a free rank was refused.");
        check(perks.PointsSpent == 4 && perks.RespecCount == 1, "perks probe: state before save is not what the probe set up.");
        _expectedPower = stats.GetValue(StatType.PhysicalPower);
    }

    /// <summary>After a load: the saved ranks, free ranks, points and respecs are back and the stat
    /// bonuses are re-applied; then an empty save replaces the lot.</summary>
    public static void Verify(PlayerCharacter? player, Action<bool, string> check)
    {
        if (player?.GetComponent<PerksComponent>() is not { } perks
            || player.GetComponent<StatsComponent>() is not { } stats)
        {
            check(false, "perks probe: the loaded player has no perks or stats.");
            return;
        }

        check(perks.RankOf(Might) == 3 && perks.RankOf(Toughness) == 1 && perks.RankOf(Endurance) == 1,
            "perks probe: loaded ranks differ from the saved ones.");
        check(perks.FreeRankOf(Endurance) == 1 && perks.FreeRankOf(Might) == 0, "perks probe: loaded free ranks differ.");
        check(perks.PointsSpent == 4 && perks.RespecCount == 1, "perks probe: loaded points spent or respecs differ.");
        check(Near(stats.GetValue(StatType.PhysicalPower), _expectedPower), "perks probe: loaded ranks did not re-apply their stat bonus.");

        // A save with none of the perk keys must wipe what is live, not merge over it.
        perks.Load(new Godot.Collections.Dictionary());
        check(perks.RankOf(Might) == 0 && perks.RankOf(Endurance) == 0 && perks.PointsSpent == 0 && perks.RespecCount == 0,
            "perks probe: Load of an empty save kept live perk state.");
        check(perks.FreeRankOf(Endurance) == 0 && perks.Effects.IsEmpty, "perks probe: Load of an empty save kept free ranks or effect totals.");
        check(Near(stats.GetValue(StatType.PhysicalPower), _expectedPower - (3f * (PerkDatabase.Get(Might)?.ValuePerRank ?? 0f))),
            "perks probe: Load of an empty save left a perk stat modifier applied.");

        VerifyCombatEffects(player, perks, check);
        EconomyPerksProbe.Verify(player, perks, check);
        MagicPerksProbe.Verify(player, perks, check);
    }

    /// <summary>The authored combat-stamina perks reach <see cref="PerkQuery"/> through the real
    /// <c>.tres</c> parse (kind, qualifier, rank scaling), an entity without perks stays neutral, and an
    /// empty-save <c>Load</c> takes the effects away again. Leaves the component empty.</summary>
    private static void VerifyCombatEffects(PlayerCharacter player, PerksComponent perks, Action<bool, string> check)
    {
        string[] ids = { "perk.iron_stance", "perk.brawler", "perk.riposte", "perk.steady_aim", "perk.evasion",
            "perk.light_feet", "perk.backstep_adept" };
        foreach (string id in ids)
        {
            if (PerkDatabase.Get(id) is not { } perk || !perks.GrantFree(perk))
            {
                check(false, $"perks probe: combat perk '{id}' is unauthored or could not be granted.");
                perks.Load(new Godot.Collections.Dictionary());
                return;
            }

            while (perks.GrantFree(perk))
            {
            }
        }

        check(Near(PerkQuery.Factor(player, PerkEffectKind.BlockStaminaMult), 0.76f), "perks probe: iron stance did not give 0.76 block cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.AttackStaminaMult), 0.85f), "perks probe: brawler did not give 0.85 attack cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.ParryStaminaMult), 0.70f), "perks probe: riposte did not give 0.70 parry cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.BowDrawStaminaMult), 0.76f), "perks probe: steady aim did not give 0.76 draw cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.SprintStaminaMult), 0.76f), "perks probe: light feet did not give 0.76 sprint cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.DodgeStaminaMult, "roll"), 0.82f), "perks probe: evasion did not give 0.82 roll cost.");
        check(Near(PerkQuery.Factor(player, PerkEffectKind.DodgeStaminaMult, "backstep"), 0.62f), "perks probe: backstep adept did not stack to 0.62 backstep cost.");
        check(Near(PerkQuery.Factor(null, PerkEffectKind.BlockStaminaMult), 1f), "perks probe: an entity without perks is not neutral.");

        perks.Load(new Godot.Collections.Dictionary());
        check(Near(PerkQuery.Factor(player, PerkEffectKind.DodgeStaminaMult, "backstep"), 1f) && perks.Effects.IsEmpty,
            "perks probe: Load of an empty save kept combat perk effects.");
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 0.001f;
}

using System.Collections.Generic;
using Embervale.Localization;

namespace Embervale.Magic;

/// <summary>The contract fields of a spell that decide its special-rule lines, as primitives so the
/// rule is testable off the engine. Built from a <see cref="SpellResource"/> by
/// <see cref="SpellBookRules.FactsOf"/>.</summary>
public readonly record struct SpellFacts(
    SpellDelivery Delivery,
    CastMode CastMode,
    float GroundDelay = 0f,
    float PullStrength = 0f,
    float BarrierDuration = 0f,
    bool BarrierBlocksBodies = false,
    bool BarrierBlocksProjectiles = false,
    float BarrierHealth = 0f,
    float DashDistance = 0f,
    float BlinkDistance = 0f,
    float HomingRange = 0f,
    float ZoneDuration = 0f,
    float SummonDuration = 0f,
    float HealthCost = 0f,
    bool Blockable = true,
    bool Interruptible = true,
    bool Consumes = false,
    StatusControl AppliedControls = StatusControl.None);

/// <summary>One plain-language line: a locale key under <c>magic.book.rule.*</c> and up to one number.</summary>
public readonly record struct SpellRuleLine(string Key, float Value);

/// <summary>
/// The spellbook's pure rules: which special-rule lines a spell earns (ground telegraph, barrier, dash,
/// mark and the rest), and the plain lock reason for a corrupted spell the reader cannot learn yet.
/// </summary>
public static class SpellBookRules
{
    /// <summary>The rule lines for a spell, most identity-defining first.</summary>
    public static List<SpellRuleLine> Rules(SpellFacts f)
    {
        var lines = new List<SpellRuleLine>();

        if (f.Delivery == SpellDelivery.Ground)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.ground", f.GroundDelay));
            if (f.PullStrength > 0f)
            {
                lines.Add(new SpellRuleLine("magic.book.rule.pull", 0f));
            }
        }

        if (f.Delivery == SpellDelivery.Barrier)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.barrier", f.BarrierDuration));
            lines.Add(new SpellRuleLine(
                f.BarrierBlocksBodies ? "magic.book.rule.barrier_solid" : "magic.book.rule.barrier_hazard", 0f));
            if (f.BarrierBlocksProjectiles)
            {
                lines.Add(new SpellRuleLine("magic.book.rule.barrier_shots", 0f));
            }

            if (f.BarrierHealth > 0f)
            {
                lines.Add(new SpellRuleLine("magic.book.rule.barrier_breakable", f.BarrierHealth));
            }
        }

        if (f.Delivery == SpellDelivery.Dash && f.DashDistance > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.dash", f.DashDistance));
        }

        if (f.BlinkDistance > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.blink", f.BlinkDistance));
        }

        if (f.HomingRange > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.homing", f.HomingRange));
        }

        if (f.ZoneDuration > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.zone", f.ZoneDuration));
        }

        if (f.SummonDuration > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.totem", f.SummonDuration));
        }

        if ((f.AppliedControls & StatusControl.Mark) != 0)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.mark", 0f));
        }

        if ((f.AppliedControls & StatusControl.Silence) != 0)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.silence", 0f));
        }

        if ((f.AppliedControls & StatusControl.Root) != 0)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.root", 0f));
        }

        if ((f.AppliedControls & StatusControl.Stun) != 0)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.stun", 0f));
        }

        if (f.Consumes)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.consumes", 0f));
        }

        if (f.HealthCost > 0f)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.health_cost", f.HealthCost));
        }

        if (!f.Blockable)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.unblockable", 0f));
        }

        if (!f.Interruptible)
        {
            lines.Add(new SpellRuleLine("magic.book.rule.hyperarmour", 0f));
        }

        return lines;
    }

    /// <summary>Whether a spell is locked to the reader: not known, and their corruption tier is below
    /// the spell's. Nothing else locks a spell (points are a price, not a lock).</summary>
    public static bool IsTierLocked(bool known, int tierNow, int tierRequired) =>
        !known && tierNow < tierRequired;

    /// <summary>Reads a spell resource into <see cref="SpellFacts"/>, resolving the status it applies
    /// through the status database so a mark, silence, root or stun is named by its control flag rather
    /// than by an id.</summary>
    public static SpellFacts FactsOf(SpellResource spell)
    {
        StatusControl controls = StatusControl.None;
        if (spell.HasStatusEffect && StatusEffectDatabase.Get(spell.StatusEffectId) is { } status)
        {
            controls = status.Controls;
        }

        return new SpellFacts(
            spell.Delivery, spell.CastMode, spell.GroundDelay, spell.PullStrength, spell.BarrierDuration,
            spell.BarrierBlocksBodies, spell.BarrierBlocksProjectiles, spell.BarrierHealth, spell.DashDistance,
            spell.BlinkDistance, spell.HomingRange, spell.ZoneDuration, spell.SummonDuration, spell.HealthCost,
            spell.Blockable, spell.Interruptible, !string.IsNullOrEmpty(spell.ConsumesStatusId), controls);
    }

    /// <summary>The rule lines for a spell, as player text.</summary>
    public static IEnumerable<string> RuleText(SpellResource spell)
    {
        foreach (SpellRuleLine line in Rules(FactsOf(spell)))
        {
            yield return Loc.TF(line.Key, line.Value.ToString("0.#"));
        }
    }
}

/// <summary>Player-facing spell and status names: the locale key <c>&lt;id&gt;.name</c> / <c>&lt;id&gt;.desc</c>
/// wins, and the resource's own <c>DisplayName</c>/<c>Description</c> is the fallback for a key not yet
/// authored, so the book never shows a raw key.</summary>
public static class SpellText
{
    public static string Name(SpellResource spell) => Pick(spell.Id + ".name", spell.DisplayName);

    public static string Description(SpellResource spell) => Pick(spell.Id + ".desc", spell.Description);

    /// <summary>A status's player name: <c>magic.status.&lt;id without "status."&gt;.name</c> first, the
    /// resource's <c>DisplayName</c> after. Same key stem as the status agent's <c>LocalName</c>.</summary>
    public static string Name(StatusEffectResource status) => Pick(StatusKey(status) + ".name", status.DisplayName);

    /// <summary>A status's one-line description, empty when none is authored.</summary>
    public static string Description(StatusEffectResource status) => Pick(StatusKey(status) + ".desc", string.Empty);

    private static string StatusKey(StatusEffectResource status) =>
        "magic.status." + (status.Id.StartsWith("status.", System.StringComparison.Ordinal) ? status.Id[7..] : status.Id);

    private static string Pick(string key, string fallback) => Loc.Has(key) ? Loc.T(key) : fallback;
}

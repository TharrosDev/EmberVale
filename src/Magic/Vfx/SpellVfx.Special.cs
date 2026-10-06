using System.Collections.Generic;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// One spell's special cases: code that runs where a recipe is not enough (Kindle pips on a target,
/// small bolts from a ball of lightning to the ground, a mana wisp flying home on a kill).
///
/// <para>Each hook runs at the top of one <see cref="SpellVfx"/> call, after the effect has been
/// opened (so it is not called at all when nothing is drawn: headless, too far, over budget). It is
/// handed the same <see cref="VfxCast"/> the generic interpreter gets and builds blocks through
/// <c>cast.Fx</c>. <b>Return true to replace the generic effect, false to add to it.</b></para>
///
/// <para>A hook for something that lasts (an aura, a bolt in flight, a zone, a wall, a totem) is also
/// handed the <see cref="VfxRig"/> of that thing. <b>Add every lasting block to it</b>
/// (<c>rig.Add(cast.Fx.Flare(...))</c>): the rig is what the matching end call stops. A block that
/// is not added to the rig is never stopped, which for a sustained block means it never ends.</para>
///
/// <para>A hook must not keep the <see cref="VfxCast"/>, touch a node it was not handed, or read
/// anything back into gameplay.</para>
/// </summary>
internal sealed class SpellVfxSpecial
{
    /// <summary>A wind-up, charge or channel began. <paramref name="hand"/> follows the casting hand.</summary>
    public delegate bool WindupHook(
        in VfxCast cast, VfxRig rig, VfxAnchor hand, SpellWindupKind kind, float seconds, float charge);

    /// <summary>The spell left the hand (not called for the ticks of a channel).</summary>
    public delegate bool ReleaseHook(in VfxCast cast, Vector3 hand, Vector3 direction, float charge);

    /// <summary>A bolt was launched. <paramref name="handOffset"/> is from the bolt to the casting
    /// hand: pass it to <c>SettleFrom</c> on anything that follows the bolt.</summary>
    public delegate bool ProjectileHook(
        in VfxCast cast, VfxRig rig, Node3D projectile, Vector3 handOffset, Vector3 direction, float charge);

    /// <summary>A channelled beam began (later ticks only re-aim the generic beam).</summary>
    public delegate bool BeamHook(in VfxCast cast, VfxRig rig, VfxAnchor hand, Vector3 from, Vector3 to);

    public delegate bool ImpactHook(in VfxCast cast, in SpellImpactInfo hit);

    public delegate bool BurstHook(
        in VfxCast cast, Vector3 position, float radius, SpellBurstSource source, float charge);

    public delegate bool ConeHook(in VfxCast cast, Vector3 origin, Vector3 direction, float range, float angleDegrees);

    public delegate bool ArcHook(in VfxCast cast, Vector3 from, Vector3 to, SpellArcKind kind);

    /// <summary>Something was placed: a ground telegraph (<paramref name="size"/> = radius,
    /// <paramref name="seconds"/> = delay), a zone (radius, duration), a wall telegraph (width,
    /// delay), a wall (width, height) or a totem (0, 0). <paramref name="node"/> is positioned just
    /// after this call: follow it, never read where it is now.</summary>
    public delegate bool PlacedHook(in VfxCast cast, VfxRig rig, Node3D node, float size, float seconds);

    /// <summary>A dash or a blink, between two points at the feet.</summary>
    public delegate bool MoveHook(in VfxCast cast, Vector3 from, Vector3 to);

    /// <summary>A status or school effect this spell set off (a fed Kindle, a freeze, a dispel).</summary>
    public delegate bool ProcHook(
        in VfxCast cast, SpellProcKind kind, IEntity? target, Vector3 position, float radius);

    public WindupHook? Windup { get; set; }

    public ReleaseHook? Release { get; set; }

    public ProjectileHook? Projectile { get; set; }

    public BeamHook? Beam { get; set; }

    public ImpactHook? Impact { get; set; }

    public BurstHook? Burst { get; set; }

    public ConeHook? Cone { get; set; }

    public ArcHook? Arc { get; set; }

    public PlacedHook? GroundTelegraph { get; set; }

    public PlacedHook? Zone { get; set; }

    public PlacedHook? BarrierTelegraph { get; set; }

    public PlacedHook? Barrier { get; set; }

    public PlacedHook? Totem { get; set; }

    public MoveHook? Dash { get; set; }

    public MoveHook? Blink { get; set; }

    public ProcHook? Proc { get; set; }
}

/// <summary>What the two <c>SpellVfx.Special.*.cs</c> files register into.</summary>
internal sealed class SpellVfxSpecialTable
{
    /// <summary>A combo going off: <paramref name="position"/> is the struck volume's centre.</summary>
    public delegate bool ComboHook(in VfxCast cast, IEntity target, Vector3 position);

    public Dictionary<string, SpellVfxSpecial> Spells { get; } = new();

    public Dictionary<string, ComboHook> Combos { get; } = new();

    /// <summary>The special for a spell id (the full <c>spell.name</c> id), created on first use.</summary>
    public SpellVfxSpecial For(string spellId)
    {
        if (!Spells.TryGetValue(spellId, out SpellVfxSpecial? special))
        {
            special = new SpellVfxSpecial();
            Spells[spellId] = special;
        }

        return special;
    }

    /// <summary>Registers what a combo (<c>combo.*</c>) looks like when it goes off.</summary>
    public void Combo(string comboId, ComboHook hook) => Combos[comboId] = hook;
}

public static partial class SpellVfx
{
    private static readonly SpellVfxSpecialTable Specials = BuildSpecials();

    /// <summary>The spell ids that have a special case, for a test and a probe.</summary>
    internal static IReadOnlyCollection<string> SpecialIds => Specials.Spells.Keys;

    private static SpellVfxSpecialTable BuildSpecials()
    {
        var table = new SpellVfxSpecialTable();
        RegisterElementalSpecials(table);
        RegisterArcanaSpecials(table);
        return table;
    }

    /// <summary>The special for a spell, or null. A spell with only a school (null) has none.</summary>
    private static SpellVfxSpecial? SpecialOf(SpellResource? spell) =>
        spell != null && Specials.Spells.TryGetValue(spell.Id, out SpellVfxSpecial? special) ? special : null;
}

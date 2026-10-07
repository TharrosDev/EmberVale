using System.Collections.Generic;
using Embervale.Combat;

namespace Embervale.Magic.Vfx;

/// <summary>The particle presets a recipe can ask for.</summary>
public enum VfxParticles
{
    None,
    Sparks,
    Embers,
    Shards,
    Motes,
    Smoke,
    Leaves,
    Wisps,
}

/// <summary>The marks an effect can leave on the ground.</summary>
public enum VfxMark
{
    None,
    Scorch,
    Frost,
    Rune,
    Roots,
}

/// <summary>How a ground spell shows its delay.</summary>
public enum VfxGroundStyle
{
    /// <summary>A mark on the ground that fills for the delay.</summary>
    Telegraph,

    /// <summary>Something falls for the delay and lands as it ends.</summary>
    Meteor,
}

/// <summary>
/// One beat of a spell's effect (its cast, its travel, its impact or what lingers), as the building
/// blocks it is made of. Which block draws what is the director's business; a recipe only says which
/// ones a beat has.
/// </summary>
public sealed record VfxStage
{
    /// <summary>A beat with nothing in it.</summary>
    public static readonly VfxStage None = new();

    /// <summary>A core flash with its halo.</summary>
    public bool Flare { get; init; }

    public VfxParticles Particles { get; init; } = VfxParticles.None;

    /// <summary>A second particle layer under the first (smoke under embers), where the tier allows
    /// secondary debris.</summary>
    public VfxParticles Secondary { get; init; } = VfxParticles.None;

    /// <summary>The ring and the particles converge on the centre instead of leaving it.</summary>
    public bool Inward { get; init; }

    /// <summary>A fresnel shell around the bearer or along the wall (wards, ice, a frozen target).</summary>
    public bool Shell { get; init; }

    /// <summary>A flat sigil: at the hand, stamped on a target, or hanging over it.</summary>
    public bool Sigil { get; init; }

    /// <summary>Something returns from the target to the caster (a life tether, a mana wisp).</summary>
    public bool Tether { get; init; }

    /// <summary>A lightning ribbon (also beams, chains and tethers).</summary>
    public bool Bolt { get; init; }

    /// <summary>An expanding shock ring.</summary>
    public bool Ring { get; init; }

    /// <summary>A refraction shell, where the tier allows one.</summary>
    public bool Distortion { get; init; }

    public VfxMark Mark { get; init; } = VfxMark.None;

    /// <summary>A screen flash, for the player's own heaviest spells.</summary>
    public bool ScreenFlash { get; init; }

    /// <summary>Size of the beat against the school's ordinary one.</summary>
    public float Scale { get; init; } = 1f;

    public bool IsEmpty => this == None;
}

/// <summary>What one spell looks like, beat by beat.</summary>
public sealed record SpellVfxRecipe(
    VfxStage Cast,
    VfxStage Travel,
    VfxStage Impact,
    VfxStage Linger,
    VfxGroundStyle Ground = VfxGroundStyle.Telegraph);

/// <summary>
/// The recipe for every spell's effect. A spell with an authored recipe gets it; any other spell
/// gets the fallback for its school and delivery, so a spell added to <c>data/spells</c> is never
/// invisible. Pure: ids, schools and deliveries in, a description out.
///
/// <para>The authored recipes live in two partial files, <c>SpellVfxCatalog.Elemental.cs</c> (fire,
/// frost, lightning) and <c>SpellVfxCatalog.Arcana.cs</c> (arcane, nature, necrotic and the enemy
/// spells), each filling the table through its own hook. The enemy-only spells belong to the second
/// file whatever their school (<see cref="ArcanaIdsOfElementalSchools"/>).</para>
/// </summary>
public static partial class SpellVfxCatalog
{
    private static readonly Dictionary<string, SpellVfxRecipe> BySpell = new();
    private static readonly Dictionary<(DamageType, SpellDelivery), SpellVfxRecipe> Fallbacks = new();

    static SpellVfxCatalog()
    {
        RegisterElemental(BySpell);
        RegisterArcana(BySpell);
    }

    /// <summary>The fire and frost spells whose recipes live in the arcana file with the other
    /// enemy-only spells, not in the elemental one.</summary>
    public static readonly IReadOnlyCollection<string> ArcanaIdsOfElementalSchools =
        new[] { "spell.dragon_breath", "spell.drake_breath" };

    /// <summary>The spell ids with a recipe of their own.</summary>
    public static IReadOnlyCollection<string> AuthoredIds => BySpell.Keys;

    /// <summary>Whether <paramref name="spellId"/> has a recipe of its own rather than a fallback.</summary>
    public static bool Has(string spellId) => BySpell.ContainsKey(spellId);

    /// <summary>The spell's own recipe, else the fallback for its school and delivery.</summary>
    public static SpellVfxRecipe For(string spellId, DamageType school, SpellDelivery delivery) =>
        BySpell.TryGetValue(spellId, out SpellVfxRecipe? recipe) ? recipe : Fallback(school, delivery);

    /// <inheritdoc cref="For(string, DamageType, SpellDelivery)"/>
    public static SpellVfxRecipe For(SpellResource spell) => For(spell.Id, spell.School, spell.Delivery);

    /// <summary>What a spell of this school and shape looks like when nobody authored it.</summary>
    public static SpellVfxRecipe Fallback(DamageType school, SpellDelivery delivery)
    {
        if (!Fallbacks.TryGetValue((school, delivery), out SpellVfxRecipe? recipe))
        {
            recipe = BuildFallback(school, delivery);
            Fallbacks[(school, delivery)] = recipe;
        }

        return recipe;
    }

    /// <summary>The particles a school throws.</summary>
    public static VfxParticles SchoolParticles(DamageType school) => school switch
    {
        DamageType.Fire => VfxParticles.Embers,
        DamageType.Frost => VfxParticles.Shards,
        DamageType.Lightning => VfxParticles.Sparks,
        DamageType.Arcane => VfxParticles.Motes,
        DamageType.Nature => VfxParticles.Leaves,
        DamageType.Necrotic => VfxParticles.Wisps,
        _ => VfxParticles.Smoke,
    };

    /// <summary>The mark a school leaves where it lands.</summary>
    public static VfxMark SchoolMark(DamageType school) => school switch
    {
        DamageType.Fire or DamageType.Lightning => VfxMark.Scorch,
        DamageType.Frost => VfxMark.Frost,
        DamageType.Arcane => VfxMark.Rune,
        DamageType.Nature => VfxMark.Roots,
        _ => VfxMark.None,
    };

    private static SpellVfxRecipe BuildFallback(DamageType school, SpellDelivery delivery)
    {
        VfxParticles particles = SchoolParticles(school);
        var aura = new VfxStage { Particles = particles };
        var snap = new VfxStage { Flare = true };
        var hit = new VfxStage { Flare = true, Particles = particles };
        var blast = new VfxStage { Flare = true, Ring = true, Particles = particles, Mark = SchoolMark(school) };

        return delivery switch
        {
            SpellDelivery.Projectile => new SpellVfxRecipe(aura, hit, hit, VfxStage.None),
            SpellDelivery.Area => new SpellVfxRecipe(aura, VfxStage.None, blast, VfxStage.None),
            SpellDelivery.Self => new SpellVfxRecipe(aura, VfxStage.None, snap, VfxStage.None),
            SpellDelivery.Cone => new SpellVfxRecipe(snap, aura, hit, VfxStage.None),
            SpellDelivery.Ground => new SpellVfxRecipe(aura, VfxStage.None, blast, VfxStage.None),
            SpellDelivery.Barrier => new SpellVfxRecipe(aura, VfxStage.None, hit, aura),
            SpellDelivery.Dash => new SpellVfxRecipe(snap, hit, hit, VfxStage.None),
            _ => new SpellVfxRecipe(aura, VfxStage.None, hit, VfxStage.None),
        };
    }
}

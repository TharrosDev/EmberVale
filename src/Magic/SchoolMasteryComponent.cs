using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A caster's persistent <b>school mastery</b>: the more you cast a magic school
/// (<see cref="DamageType"/>), the better you get at it. Each cast banks a point for that school
/// (driven off <see cref="SpellCastEvent"/>), points convert to a rank via <see cref="SchoolMasteryMath"/>,
/// and the rank does three things: it empowers every spell of the school (read by
/// <see cref="SpellcastingComponent"/> through <see cref="PowerMultiplier"/>), it shortens the school's
/// cooldowns (<see cref="CooldownMultiplier"/>) and, at rank 3, it attunes the caster against the school's
/// own damage (a flat resistance modifier this component owns).
///
/// Points persist (<see cref="ISaveable"/>). A load <b>replaces</b> the banked points and re-derives the
/// attunement, and it does not announce: a rank restored from a save is not a rank gained.
/// </summary>
[GlobalClass]
public partial class SchoolMasteryComponent : EntityComponent, ISaveable
{
    // Casting points banked per school. Rank is derived, not stored, so the curve can be retuned freely.
    private readonly Dictionary<DamageType, int> _points = new();

    // When each channelled spell last banked a point (ms since engine start), so a held beam is paced.
    private readonly Dictionary<string, ulong> _lastChannelPoint = new();

    // One stable source object per school, so an attunement modifier can be stripped without touching
    // anyone else's modifiers on the same stat.
    private readonly Dictionary<DamageType, object> _attunementSources = new();

    private StatsComponent? _stats;

    public string SaveId => SaveKey("mastery");

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        EventBus.Instance?.Subscribe<SpellCastEvent>(OnSpellCast);
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<SpellCastEvent>(OnSpellCast);
        SaveManager.Instance?.Unregister(this);
    }

    private void OnSpellCast(SpellCastEvent cast)
    {
        if (!ReferenceEquals(cast.Caster, Entity) || SpellDatabase.Get(cast.SpellId) is not { } spell)
        {
            return;
        }

        bool channelled = spell.CastMode == CastMode.Channeled;
        if (channelled)
        {
            ulong now = Time.GetTicksMsec();
            double since = _lastChannelPoint.TryGetValue(spell.Id, out ulong last)
                ? (now - last) / 1000.0
                : double.MaxValue;
            if (!SchoolMasteryMath.BanksPoint(true, since))
            {
                return;
            }

            _lastChannelPoint[spell.Id] = now;
        }

        AddPoints(spell.School, 1);
    }

    /// <summary>Banks <paramref name="points"/> for a school and raises <see cref="SchoolRankedUpEvent"/>
    /// if that crossed a rank threshold.</summary>
    public void AddPoints(DamageType school, int points) =>
        SetPoints(school, PointsIn(school) + points);

    /// <summary>Sets a school's banked points (dev tooling and <see cref="AddPoints"/>). Raising the rank
    /// announces it; lowering it does not, and re-derives the attunement either way.</summary>
    public void SetPoints(DamageType school, int points)
    {
        int before = PointsIn(school);
        int after = System.Math.Max(0, points);
        _points[school] = after;
        ApplyAttunement(school);

        if (SchoolMasteryMath.RankGained(before, after) is int rank and > 0 && Entity != null)
        {
            EventBus.Instance?.Publish(new SchoolRankedUpEvent(Entity, school, rank));
        }
    }

    /// <summary>Casting points banked for a school.</summary>
    public int PointsIn(DamageType school) => _points.TryGetValue(school, out int p) ? p : 0;

    /// <summary>The caster's current mastery rank in a school (0..MaxRank).</summary>
    public int RankOf(DamageType school) => SchoolMasteryMath.RankForPoints(PointsIn(school));

    /// <summary>The damage/healing multiplier a school's spells get from the caster's mastery.</summary>
    public float PowerMultiplier(DamageType school) => SchoolMasteryMath.PowerMultiplier(RankOf(school));

    /// <summary>The multiplier on a school's spell cooldowns (1 = untouched). The casting component
    /// multiplies a spell's <c>Cooldown</c> by this when it starts the timer.</summary>
    public float CooldownMultiplier(DamageType school) => SchoolMasteryMath.CooldownMultiplier(RankOf(school));

    // --- attunement -----------------------------------------------------------

    private void ApplyAttunement(DamageType school)
    {
        _stats ??= Entity?.GetComponent<StatsComponent>();
        if (_stats == null || school is DamageType.Physical or DamageType.True)
        {
            return;
        }

        if (!_attunementSources.TryGetValue(school, out object? source))
        {
            source = new object();
            _attunementSources[school] = source;
        }

        Stat resist = _stats.GetStat(CombatMath.ResistanceStat(school));
        resist.RemoveModifiersFromSource(source);

        float amount = SchoolMasteryMath.AttunementResistFor(RankOf(school));
        if (amount > 0f)
        {
            resist.AddModifier(new StatModifier(amount, ModifierType.Flat, source));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var points = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<DamageType, int> pair in _points)
        {
            points[(int)pair.Key] = pair.Value;
        }

        return new Godot.Collections.Dictionary { ["points"] = points };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        // Replace, never merge: strip every attunement the abandoned timeline earned before rebuilding
        // from the save, so a quickload to an earlier point cannot keep a rank-3 resist it no longer has.
        var touched = new HashSet<DamageType>(_points.Keys);
        _points.Clear();
        _lastChannelPoint.Clear();

        if (data.TryGetValue("points", out Variant pointsVar))
        {
            Godot.Collections.Dictionary points = pointsVar.AsGodotDictionary();
            foreach (Variant key in points.Keys)
            {
                var school = (DamageType)key.AsInt32();
                _points[school] = System.Math.Max(0, points[key].AsInt32());
                touched.Add(school);
            }
        }

        foreach (DamageType school in touched)
        {
            ApplyAttunement(school);
        }
    }
}

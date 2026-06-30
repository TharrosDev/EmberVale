using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Magic;

/// <summary>
/// A caster's persistent <b>school mastery</b> (Phase 29.5C): the more you cast a magic school
/// (<see cref="DamageType"/>), the better you get at it. Each cast banks a point for that school
/// (driven off <see cref="SpellCastEvent"/>), points convert to a rank via <see cref="SchoolMasteryMath"/>,
/// and the rank empowers every spell of that school — read by <see cref="SpellcastingComponent"/> when it
/// builds a cast's damage/heal. Mastery is the "hard to master" magic ceiling, not just bigger numbers.
///
/// Points persist (<see cref="ISaveable"/>) so a build's invested schools survive save/load.
/// </summary>
[GlobalClass]
public partial class SchoolMasteryComponent : EntityComponent, ISaveable
{
    // Casting points banked per school. Rank is derived, not stored, so the curve can be retuned freely.
    private readonly Dictionary<DamageType, int> _points = new();

    public string SaveId => SaveKey("mastery");

    protected override void OnInitialize()
    {
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

        // ponytail: 1 point per cast event — a channeled spell publishes one per tick, so it ranks
        // faster than a fire-and-forget cast. Fine for the slice; weight by mana spent if it cheeses.
        _points[spell.School] = PointsIn(spell.School) + 1;
    }

    /// <summary>Casting points banked for a school.</summary>
    public int PointsIn(DamageType school) => _points.TryGetValue(school, out int p) ? p : 0;

    /// <summary>The caster's current mastery rank in a school (0..MaxRank).</summary>
    public int RankOf(DamageType school) => SchoolMasteryMath.RankForPoints(PointsIn(school));

    /// <summary>The damage/healing multiplier a school's spells get from the caster's mastery.</summary>
    public float PowerMultiplier(DamageType school) => SchoolMasteryMath.PowerMultiplier(RankOf(school));

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
        _points.Clear();
        if (data.TryGetValue("points", out Variant pointsVar))
        {
            Godot.Collections.Dictionary points = pointsVar.AsGodotDictionary();
            foreach (Variant key in points.Keys)
            {
                _points[(DamageType)key.AsInt32()] = points[key].AsInt32();
            }
        }
    }
}

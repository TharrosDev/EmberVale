using Embervale.Core.Diagnostics;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Embervale.Stats;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// Tracks an entity's experience and level and turns kills into growth. It listens
/// for <see cref="EntityDiedEvent"/>s it caused (the dead entity's
/// <see cref="ExperienceComponent"/> supplies the bounty), accumulates XP against a
/// <see cref="ProgressionResource"/> curve, and on each level-up applies that
/// level's flat stat gains (as <see cref="StatModifier"/>s sourced to this
/// component, so they persist and recompute cleanly), refills resources, and awards
/// skill points spent through a <see cref="PerksComponent"/>.
///
/// Persists level / current XP / unspent skill points via <see cref="ISaveable"/>;
/// stat growth is re-derived from the loaded level, never stored.
/// </summary>
[GlobalClass]
public partial class ProgressionComponent : EntityComponent, ISaveable
{
    [Export] public ProgressionResource? Curve { get; set; }

    /// <summary>Optional path to load the curve from when one isn't assigned.</summary>
    [Export] public string CurvePath { get; set; } = string.Empty;

    private readonly object _growthSource = new();
    private StatsComponent? _stats;

    public int Level { get; private set; } = 1;
    public int CurrentXp { get; private set; }
    public int SkillPoints { get; private set; }

    public string SaveId => SaveKey("progression");

    /// <summary>XP needed to advance from the current level; 0 at the cap.</summary>
    public int XpToNext => Curve?.XpToReach(Level) ?? 0;

    public bool IsMaxLevel => Curve != null && Level >= Curve.MaxLevel;

    protected override void OnInitialize()
    {
        if (Curve == null && !string.IsNullOrEmpty(CurvePath))
        {
            Curve = GD.Load<ProgressionResource>(CurvePath);
            if (Curve == null)
            {
                Log.Warn($"ProgressionComponent could not load curve '{CurvePath}'; using the default progression.");
            }
        }

        Curve ??= ProgressionResource.CreateDefault();
        _stats = Entity!.GetComponent<StatsComponent>();
        ApplyGrowth();

        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        SaveManager.Instance?.Unregister(this);
    }

    private void OnEntityDied(EntityDiedEvent e)
    {
        // Only react to kills this entity landed (and never to its own death).
        if (Entity == null || e.Killer == null || !ReferenceEquals(e.Killer, Entity))
        {
            return;
        }

        if (ReferenceEquals(e.Entity, Entity))
        {
            return;
        }

        ExperienceComponent? bounty = e.Entity.GetComponent<ExperienceComponent>();
        if (bounty != null && bounty.XpValue > 0)
        {
            AddXp(bounty.XpValue);
        }
    }

    /// <summary>Grants XP, resolving as many level-ups as it covers.</summary>
    public void AddXp(int amount)
    {
        if (amount <= 0 || Entity == null || Curve == null)
        {
            return;
        }

        if (IsMaxLevel)
        {
            // Still surface the event so UI can react, but cap progress at 0.
            CurrentXp = 0;
            EventBus.Instance?.Publish(new XpGainedEvent(Entity, 0, CurrentXp, 0));
            return;
        }

        (int newLevel, int newXp, int levelsGained) = ProgressionMath.Resolve(
            Level, CurrentXp, Curve.MaxLevel, amount, Curve.XpToReach);
        Level = newLevel;
        CurrentXp = newXp;

        if (levelsGained > 0)
        {
            int skillPointsGained = levelsGained * Curve.SkillPointsPerLevel;
            SkillPoints += skillPointsGained;
            ApplyGrowth();
            _stats?.RefillResources();
            EventBus.Instance?.Publish(new LeveledUpEvent(Entity, Level, skillPointsGained));
            Log.Info($"{Entity.DisplayName} reached level {Level} (+{skillPointsGained} skill point(s)).");
        }

        EventBus.Instance?.Publish(new XpGainedEvent(Entity, amount, CurrentXp, XpToNext));
    }

    /// <summary>Spends skill points (for perks). Returns false if too few are available.</summary>
    public bool SpendSkillPoints(int cost)
    {
        if (cost <= 0 || SkillPoints < cost)
        {
            return cost <= 0;
        }

        SkillPoints -= cost;
        return true;
    }

    /// <summary>Re-derives the cumulative per-level stat bonus for the current level.</summary>
    private void ApplyGrowth()
    {
        if (_stats == null || Curve == null)
        {
            return;
        }

        int bonusLevels = Level - 1; // level 1 grants nothing
        foreach ((StatType stat, float perLevel) in Curve.StatGains())
        {
            Stat target = _stats.GetStat(stat);
            target.RemoveModifiersFromSource(_growthSource);
            float total = perLevel * bonusLevels;
            if (total != 0f)
            {
                target.AddModifier(new StatModifier(total, ModifierType.Flat, _growthSource));
            }
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary
        {
            ["level"] = Level,
            ["xp"] = CurrentXp,
            ["sp"] = SkillPoints,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        Level = data.TryGetValue("level", out Variant levelVar) ? Mathf.Max(1, levelVar.AsInt32()) : 1;
        CurrentXp = data.TryGetValue("xp", out Variant xpVar) ? Mathf.Max(0, xpVar.AsInt32()) : 0;
        SkillPoints = data.TryGetValue("sp", out Variant spVar) ? Mathf.Max(0, spVar.AsInt32()) : 0;

        ApplyGrowth();

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new XpGainedEvent(Entity, 0, CurrentXp, XpToNext));
        }
    }
}

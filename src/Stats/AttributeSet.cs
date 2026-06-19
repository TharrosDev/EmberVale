using System.Collections.Generic;
using Godot;

namespace Embervale.Stats;

/// <summary>
/// Authoring-time data block describing the base values of an entity's stats.
/// Designers create <c>.tres</c> presets ("GoblinAttributes", "PlayerStart",
/// "DireWolfAttributes") in the Godot inspector; a <see cref="StatsComponent"/>
/// consumes one to seed its runtime <see cref="Stat"/>s. This is the
/// resource-driven content pipeline for character balance — new actors need no
/// code, only a new resource.
/// </summary>
[GlobalClass]
public partial class AttributeSet : Resource
{
    [ExportGroup("Resources")]
    [Export] public float Health { get; set; } = 100f;
    [Export] public float Stamina { get; set; } = 100f;
    [Export] public float Mana { get; set; } = 50f;

    [ExportGroup("Primary Attributes")]
    [Export] public float Strength { get; set; } = 10f;
    [Export] public float Dexterity { get; set; } = 10f;
    [Export] public float Intelligence { get; set; } = 10f;
    [Export] public float Vitality { get; set; } = 10f;
    [Export] public float Endurance { get; set; } = 10f;

    [ExportGroup("Derived / Combat")]
    [Export] public float Armor { get; set; } = 0f;
    [Export] public float PhysicalPower { get; set; } = 10f;
    [Export] public float SpellPower { get; set; } = 10f;
    [Export] public float MoveSpeed { get; set; } = 5f;
    [Export] public float AttackSpeed { get; set; } = 1f;
    [Export] public float CritChance { get; set; } = 0.05f;
    [Export] public float CritDamage { get; set; } = 1.5f;

    /// <summary>Materializes the exported fields into a stat-keyed lookup.</summary>
    public IReadOnlyDictionary<StatType, float> ToBaseValues()
    {
        return new Dictionary<StatType, float>
        {
            [StatType.Health] = Health,
            [StatType.Stamina] = Stamina,
            [StatType.Mana] = Mana,
            [StatType.Strength] = Strength,
            [StatType.Dexterity] = Dexterity,
            [StatType.Intelligence] = Intelligence,
            [StatType.Vitality] = Vitality,
            [StatType.Endurance] = Endurance,
            [StatType.Armor] = Armor,
            [StatType.PhysicalPower] = PhysicalPower,
            [StatType.SpellPower] = SpellPower,
            [StatType.MoveSpeed] = MoveSpeed,
            [StatType.AttackSpeed] = AttackSpeed,
            [StatType.CritChance] = CritChance,
            [StatType.CritDamage] = CritDamage,
        };
    }

    /// <summary>Programmatic default used as a fallback when no resource is assigned.</summary>
    public static AttributeSet CreateDefault()
    {
        return new AttributeSet();
    }
}

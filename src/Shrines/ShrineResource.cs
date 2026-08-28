using Embervale.Stats;
using Godot;

namespace Embervale.Shrines;

/// <summary>
/// One god's authored blessing. The resource is the single definition of the passive: shrine
/// interactables only name its stable id, while <see cref="BlessingComponent"/> resolves and applies
/// it. A claimed id persists on the player; the stat modifier is always re-derived from that id.
/// </summary>
[GlobalClass]
public partial class ShrineResource : Resource
{
    /// <summary>Stable, persisted shrine id, for example <c>shrine.solaryn</c>.</summary>
    [Export] public string Id { get; set; } = "shrine.unknown";

    /// <summary>Locale key for the place named in the interaction prompt.</summary>
    [Export] public string NameKey { get; set; } = "shrine.unknown.name";

    /// <summary>Locale key for the blessing named in the acquired-toast.</summary>
    [Export] public string BlessingNameKey { get; set; } = "blessing.unknown.name";

    /// <summary>The one stat this blessing changes. Later blessings remain data additions.</summary>
    [Export] public StatType Stat { get; set; } = StatType.Health;

    [Export] public ModifierType ModifierType { get; set; } = ModifierType.Flat;

    /// <summary>The modifier value; a percentage is expressed as a fraction (0.10 = 10%).</summary>
    [Export] public float Value { get; set; } = 1f;
}

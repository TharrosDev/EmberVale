using Embervale.Entities;
using Godot;

namespace Embervale.Progression;

/// <summary>
/// Marks an entity as worth experience when slain. It holds only the bounty value;
/// the killer's <see cref="ProgressionComponent"/> reads it on
/// <see cref="Embervale.Core.Events.EntityDiedEvent"/> and awards the XP. Attach it
/// to enemies (see <c>EnemyFactory</c>). No behaviour, so it stays a passive,
/// data-only capability.
/// </summary>
[GlobalClass]
public partial class ExperienceComponent : EntityComponent
{
    /// <summary>XP granted to the entity that lands the killing blow.</summary>
    [Export] public int XpValue { get; set; } = 10;
}

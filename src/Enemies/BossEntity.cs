using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Marker for a boss actor — an <see cref="EnemyEntity"/> (so the AI, targeting and combat treat it
/// as an ordinary hostile NPC) distinguished at the type level so the things that only apply to a
/// boss can branch on it.
///
/// <b>Who actually reads it:</b> <see cref="BossEncounterDirector"/> (a death is a defeat beat, a
/// slow-mo and a reward only for one of these) and <see cref="ArenaHookComponent"/> (an arena resets
/// when a boss falls). Both test the type directly off an <c>EntityDiedEvent</c>.
///
/// <b>It is deliberately not a <c>ServiceLocator</c> registration.</b> Phase 28 registered it for
/// the 28C healthbar and the 28D corruption loop; both have since moved onto
/// <c>BossEncounterStartedEvent</c> / <c>BossPhaseChangedEvent</c>, which is what a HUD should react
/// to anyway. The registration outlived its last reader and was removed — if something ever needs to
/// resolve "the boss currently being fought" again, register it then rather than maintaining a slot
/// nothing reads.
/// </summary>
[GlobalClass]
public partial class BossEntity : EnemyEntity
{
}

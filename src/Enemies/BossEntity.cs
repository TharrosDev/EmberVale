using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Marker for a boss actor — an <see cref="EnemyEntity"/> (so the AI, targeting and combat treat it as a
/// hostile NPC) distinguished at the type level so it can be registered as its own
/// <c>ServiceLocator</c> type. The boss healthbar (Phase 28C) and the corruption-gain loop (Phase 28D)
/// resolve <c>ServiceLocator → BossEntity</c> without clashing with the dummy <c>Entity</c> or the player.
/// </summary>
[GlobalClass]
public partial class BossEntity : EnemyEntity
{
}

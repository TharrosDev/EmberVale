using Embervale.Entities;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// Concrete actor type for hostile NPCs. A marker subclass of
/// <see cref="CharacterEntity"/> so enemies are distinguishable from the player
/// at the type level (e.g. perception, targeting). All behaviour lives in
/// components — chiefly <see cref="EnemyAIComponent"/>.
/// </summary>
[GlobalClass]
public partial class EnemyEntity : CharacterEntity
{
}

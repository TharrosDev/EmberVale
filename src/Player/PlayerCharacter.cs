using Embervale.Entities;
using Godot;

namespace Embervale.Player;

/// <summary>
/// Concrete actor type for the player. It is a marker subclass of
/// <see cref="CharacterEntity"/> so the player can be registered with and
/// resolved from the <see cref="Core.Services.ServiceLocator"/> by a type
/// distinct from enemy <see cref="CharacterEntity"/> instances (which share the
/// base type). Player behaviour lives in components; this type adds no logic.
/// </summary>
[GlobalClass]
public partial class PlayerCharacter : CharacterEntity
{
}

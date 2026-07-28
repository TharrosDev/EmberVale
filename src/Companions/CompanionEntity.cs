using Embervale.Entities;
using Godot;

namespace Embervale.Companions;

/// <summary>
/// Concrete actor type for a recruited companion. A marker subclass of
/// <see cref="CharacterEntity"/> so companions are distinguishable from the player and from hostile
/// NPCs at the type level (targeting, perception, save reconciliation). All behaviour lives in
/// components — chiefly <see cref="CompanionAIComponent"/>.
/// </summary>
[GlobalClass]
public partial class CompanionEntity : CharacterEntity
{
    /// <summary>The stable companion id this actor was built from (e.g. <c>companion.kael</c>).</summary>
    [Export]
    public string CompanionId { get; set; } = string.Empty;

    /// <summary>The <c>Loc</c> key for this companion's name, resolved at display time (so a locale
    /// switch mid-session renames them) — the same convention quest titles use.</summary>
    [Export]
    public string NameKey { get; set; } = string.Empty;
}

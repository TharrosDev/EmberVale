using System;

namespace Embervale.Magic;

/// <summary>
/// What a status does to its bearer beyond a stat modifier or a tick. Read through
/// <see cref="StatusEffectsComponent.Controls"/> by casting, movement and AI, so no system has to know
/// which status id means "cannot move".
/// </summary>
// APPEND ONLY: values persist in .tres, never renumber (EnumStabilityTests).
[Flags]
public enum StatusControl
{
    None = 0,

    /// <summary>Cannot move on foot (Thornsnare, Grave Mark's bone cage). Can still act and cast.</summary>
    Root = 1,

    /// <summary>Cannot start or hold a cast (Null Lance). A cast in its wind-up is interrupted.</summary>
    Silence = 2,

    /// <summary>Cannot act at all for the duration (a Thunder Step finish, a freeze).</summary>
    Stun = 4,

    /// <summary>Carries a caster's mark that other spells read (Kindle, Stormbrand, Grave Mark).</summary>
    Mark = 8,
}

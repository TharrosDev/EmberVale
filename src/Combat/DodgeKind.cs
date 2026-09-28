namespace Embervale.Combat;

/// <summary>Which evade a dodge press resolves to (<see cref="Dodge.Resolve"/>): a roll toward the held
/// direction, or a short backstep when no direction is held.</summary>
public enum DodgeKind
{
    Roll,
    Backstep,
}

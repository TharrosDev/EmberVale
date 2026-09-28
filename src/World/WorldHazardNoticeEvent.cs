using Embervale.Core.Events;

namespace Embervale.World;

/// <summary>
/// The world telling the player something about the ground they are on — "the water is too deep",
/// "you were pulled back to safe ground". <paramref name="ReasonKey"/> is a <c>Loc</c> key; the
/// notification feed shows it as a warning toast. Published by <see cref="WorldWading"/> and
/// <see cref="WorldRecovery"/>.
/// </summary>
public readonly record struct WorldHazardNoticeEvent(string ReasonKey) : IGameEvent;

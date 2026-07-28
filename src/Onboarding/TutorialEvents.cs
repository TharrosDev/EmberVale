using Embervale.Core.Events;

namespace Embervale.Onboarding;

/// <summary>Raised when the onboarding moves to a new hint (or to <see cref="TutorialStep.None"/>
/// when it ends). The HUD hint widget renders whatever this carries.</summary>
public readonly record struct TutorialStepChangedEvent(TutorialStep Step) : IGameEvent;

/// <summary>Raised once the player has performed a taught verb — the beat the hint clears on.</summary>
public readonly record struct TutorialStepCompletedEvent(TutorialStep Step) : IGameEvent;

/// <summary>Raised when the whole sequence is done or skipped. <paramref name="Skipped"/> separates
/// "taught" from "waved off", which the analytics sink cares about and the HUD does not.</summary>
public readonly record struct TutorialFinishedEvent(bool Skipped) : IGameEvent;

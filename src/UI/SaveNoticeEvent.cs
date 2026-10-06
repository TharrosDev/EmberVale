using Embervale.Core.Events;

namespace Embervale.UI;

/// <summary>
/// A save or load message for the toast feed that no save event already carries: the F9 "press
/// again to confirm" prompt, "nothing to load", a refused reload. <paramref name="Text"/> is already
/// localized; <paramref name="Warning"/> colours it as a problem rather than a remark.
/// </summary>
public readonly record struct SaveNoticeEvent(string Text, bool Warning = false) : IGameEvent;

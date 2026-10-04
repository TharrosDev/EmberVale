using Embervale.Core.Events;

namespace Embervale.Narrative;

/// <summary>
/// Raised once when the vertical slice's arc closes (Phase 33D). <paramref name="AbsorbedEmber"/>
/// records the choice the slice was built around — whether the player took the Iron King's ember —
/// so the closing card can reflect it rather than ending on the same words either way.
/// </summary>
public readonly record struct SliceCompletedEvent(bool AbsorbedEmber) : IGameEvent;

/// <summary>
/// Raised by the <c>PlayCards</c> dialogue effect: play full-screen story cards whose locale keys are
/// <c>&lt;Prefix&gt;.1</c>, <c>&lt;Prefix&gt;.2</c>, ... until a key is missing (see
/// <see cref="StoryCards"/>). The UI's story-card sequence subscribes; dialogue code never references UI.
/// </summary>
public readonly record struct StoryCardsRequestedEvent(string Prefix) : IGameEvent;

/// <summary>
/// Raised by the <c>Banner</c> dialogue effect: show the chapter banner for <paramref name="ChapterKey"/>
/// (text from <c>chapter.&lt;key&gt;.title</c> / <c>.subtitle</c>, falling back to <c>pale.chapter.*</c>).
/// Published only; the UI renders it.
/// </summary>
public readonly record struct StoryBannerRequestedEvent(string ChapterKey) : IGameEvent;

/// <summary>
/// A named story beat published by a story rule (<c>"beat"</c> in <c>data/story/rules</c>) or by code
/// (the Pale Concord reveal publishes <c>"pale_reveal"</c>). The UI and audio may react; nothing else
/// depends on it, and it is never saved.
/// </summary>
public readonly record struct StoryBeatEvent(string Key) : IGameEvent;

/// <summary>
/// A story moment's toast: <paramref name="TextKey"/> as the line and <paramref name="DetailKey"/> (may be
/// empty) as the dim line beneath it. Published by story code that has no toast of its own (the Pale
/// Concord reveal); the notification feed subscribes and holds it behind any menu or cinematic.
/// </summary>
public readonly record struct StoryToastRequestedEvent(string TextKey, string DetailKey = "") : IGameEvent;

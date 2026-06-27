using Embervale.Core.Events;
using Godot;

namespace Embervale.World;

/// <summary>Raised by the <see cref="WorldClock"/> when the hour-of-day changes (and
/// once on start/load). Schedules and ambience react to this rather than polling.</summary>
public readonly record struct TimeOfDayChangedEvent(int Hour, DayPhase Phase) : IGameEvent;

/// <summary>Raised by the <see cref="WeatherDirector"/> when the active weather changes
/// (and once on start/load). The atmosphere, encounters and UI react to this.</summary>
public readonly record struct WeatherChangedEvent(WeatherType Previous, WeatherType Current, string WeatherId) : IGameEvent;

/// <summary>Raised by the <see cref="EncounterDirector"/> when a dynamic encounter is
/// spawned near the player. Carries where it appeared and how many actors it spawned.</summary>
public readonly record struct EncounterTriggeredEvent(string EncounterId, Vector3 Position, int Count) : IGameEvent;

/// <summary>Raised by the <see cref="WorldEventDirector"/> when a named world event begins.</summary>
public readonly record struct WorldEventStartedEvent(string EventId, string DisplayName, Vector3 Position) : IGameEvent;

/// <summary>Raised when an active world event's objective advances.</summary>
public readonly record struct WorldEventProgressEvent(string EventId, int Progress, int Required) : IGameEvent;

/// <summary>Raised when a world event ends, either resolved (<paramref name="Completed"/>) or expired.</summary>
public readonly record struct WorldEventEndedEvent(string EventId, string DisplayName, bool Completed) : IGameEvent;

/// <summary>Raised by the <see cref="RegionStreamer"/> when a sub-cell scene is streamed in
/// (Phase 25B). <paramref name="Root"/> is the instanced cell node — the seam Phase 25D's
/// persistent-actor restore hooks.</summary>
public readonly record struct RegionCellLoadedEvent(string CellId, Node3D Root) : IGameEvent;

/// <summary>Raised by the <see cref="RegionStreamer"/> just before a sub-cell is freed (Phase 25B).</summary>
public readonly record struct RegionCellUnloadedEvent(string CellId) : IGameEvent;

/// <summary>Raised by a <see cref="RegionTransitionComponent"/> (or the <c>region goto</c> dev command)
/// to request a hard region-to-region load (Phase 25C). The bootstrap performs the swap: it unloads
/// the current region's cells, re-targets the streamer, teleports the player to the new region's
/// spawn, and shows the loading screen for the transition.</summary>
public readonly record struct RegionTransitionRequestedEvent(string RegionId) : IGameEvent;

/// <summary>Raised from the map screen (or the <c>travel goto</c> dev command) to fast-travel to a
/// discovered <see cref="FastTravelService"/> node (Phase 25G). The bootstrap reuses the 25C hard-load
/// path, landing the player at the node's position within its region.</summary>
public readonly record struct FastTravelRequestedEvent(string NodeId) : IGameEvent;

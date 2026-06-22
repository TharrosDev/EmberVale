using Embervale.Core.Events;

namespace Embervale.Analytics;

/// <summary>
/// A generic, dev-only telemetry record any system can publish onto the EventBus for the
/// <see cref="AnalyticsSink"/> to log. Deaths, quests and level-ups are captured directly from
/// their own gameplay events; this is the escape hatch for ad-hoc instrumentation a system wants
/// in the analytics stream without defining a dedicated event. <paramref name="Name"/> is the
/// metric (e.g. "shop_purchase", "fast_travel"); <paramref name="Detail"/> is an optional
/// free-form payload (an id, a count, a small key=value string).
/// </summary>
public readonly record struct AnalyticsEvent(string Name, string Detail = "") : IGameEvent;

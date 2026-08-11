using System.Collections.Generic;
using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Magic;
using Embervale.Player;
using Embervale.Quests;
using Embervale.Stats;
using Embervale.World;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The HUD screenshot harness — <c>godot --path . -- --hudshots</c> (39.5B).
///
/// ⚠️ <b>THIS IS THE TOOL THE REPO HAS BEEN MISSING FOR TWO SUB-PHASES.</b> 39.5A shipped three
/// screen-space defects through a fully green battery and named the gap; 39.5B then built a minimap,
/// a HUD slot, an overlay and a visibility system with no way to look at any of them. The reason is
/// structural and neither half of the toolchain closes it: <c>--play</c> boots the world but cannot
/// press a key, and the Godot MCP drives the <b>editor</b>, where the HUD does not exist at all
/// because <see cref="Bootstrap.GameBootstrap"/> constructs it at runtime.
///
/// So this drives the real HUD, in a real session, through real state, and renders each state to a
/// PNG an agent can actually open. It is <b>not</b> a test: it asserts nothing and gates nothing.
/// Its whole job is to turn "reviewed against the API" into "looked at".
///
/// ⚠️ <b>Every state is driven through the authoritative system, never by poking the HUD.</b> Low
/// health is <see cref="StatsComponent.SetCurrent"/>; a status chip is
/// <see cref="StatusEffectsComponent.Apply"/>; the menu state is <see cref="UiState.Open"/>. A
/// harness that set the widgets directly would photograph itself rather than the HUD, and would go
/// on producing perfect screenshots after the bindings broke.
/// </summary>
public sealed partial class HudShots : ShotHarness
{
    protected override string Flag => "--hudshots";

    protected override string OutputDir => "user://hudshots";

    /// <summary>
    /// The states worth looking at, in the order the brief's §69 asks for them.
    ///
    /// Ordered so each builds on the last rather than resetting: the resources drain in sequence, the
    /// statuses land on the drained bars, and the menu shot comes last because it is the only one
    /// that changes what is on screen rather than what the widgets say.
    /// </summary>
    protected override void BuildShotList()
    {
        Shot("01-exploration", () => Stats()?.RefillResources());

        Shot("02-health-low", () => SetFraction(StatType.Health, 0.18f));

        Shot("03-mana-low", () => SetFraction(StatType.Mana, 0.08f));

        Shot("04-endurance-empty", () => SetFraction(StatType.Stamina, 0f));

        Shot("05-statuses", ApplyStatuses);

        // ⚠️ The save this harness loads has no active quest, so without this the tracker — and the
        // distance/bearing readout that is one of 39.5B's headline changes — never appears in a single
        // image. A capture set that silently omits the feature under review is the failure mode this
        // whole tool exists to prevent.
        Shot("05b-quest-tracked", StartAndTrackAQuest);

        Shot("06-night", () => SetHour(23));

        Shot("07-dawn", () => SetHour(6));

        // The visibility rule this sub-phase added — the one shot that proves a HUD is ABSENT.
        Shot("08-menu-open", () => UiState.Open(this));

        Shot("09-menu-closed", () => UiState.Close(this));
    }

    // --- State drivers, all through the owning system ------------------------

    private static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private static StatsComponent? Stats() => Player()?.GetComponent<StatsComponent>();

    private static void SetFraction(StatType type, float fraction)
    {
        if (Stats() is { } stats)
        {
            stats.SetCurrent(type, stats.GetMax(type) * fraction);
        }
    }

    /// <summary>Applies whatever status effects the game actually has, up to three — the row's
    /// crowding is the thing being looked at, and inventing effects to fill it would photograph a
    /// HUD this game cannot produce (§73).</summary>
    private static void ApplyStatuses()
    {
        if (Player() is not { } player ||
            player.GetComponent<StatusEffectsComponent>() is not { } effects)
        {
            return;
        }

        int applied = 0;
        foreach (StatusEffectResource definition in StatusEffectDatabase.All())
        {
            effects.Apply(definition, player);
            if (++applied >= 3)
            {
                return;
            }
        }
    }

    /// <summary>Starts the first quest the player can actually take and tracks it, so the tracker,
    /// its objective rows and the distance/bearing readout are all on screen to be looked at.</summary>
    private static void StartAndTrackAQuest()
    {
        if (Player()?.GetComponent<QuestLogComponent>() is not { } log)
        {
            return;
        }

        foreach (QuestResource quest in QuestDatabase.All)
        {
            if (log.StartQuest(quest))
            {
                log.Track(quest.Id);
                return;
            }
        }
    }

    private static void SetHour(int hour)
    {
        if (ServiceLocator.Instance is { } locator && locator.TryGet(out WorldClock clock))
        {
            clock.SetTimeOfDay(hour);
        }
    }
}

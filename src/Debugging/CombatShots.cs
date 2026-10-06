using System;
using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Localization;
using Embervale.Player;
using Embervale.Settings;
using Embervale.UI;
using Godot;

namespace Embervale.Debugging;

/// <summary>
/// The combat screenshot harness — <c>godot --path . -- --combat-shots</c>. It exists because the
/// combat upgrade's presentation (damage numbers, telegraph rings, the warning arcs, lock-on cues, the
/// nameplate poise bar) was only ever reviewed against the API. A hostile Iron King is built through
/// the real factory in front of the player, and every state is driven through the authoritative
/// system: a real <see cref="CombatComponent.ReceiveDamage"/>, a real action start, a real lock.
///
/// Not a test: it asserts nothing beyond "the state was reached". Its job is to turn "reviewed" into
/// "looked at". Run WITHOUT <c>--headless</c>.
///
/// The shots after the thirteenth photograph the HUD around the fight: the enemy plates, the
/// damage-number modes, a captioned line, the toast feed holding back and collapsing, and the boss
/// bar at two layout widths. Each reads its state back from the widget's own capture accessors.
/// </summary>
public sealed partial class CombatShots : ShotHarness
{
    private const float Range = 6f;

    /// <summary>The item the pickup toasts are staged with.</summary>
    private const string PickupItem = "item.ammo.arrows";

    private EnemyEntity? _subject;
    private readonly List<EnemyEntity> _pack = new();

    // What a shot has to have reached, beyond the checks every shot shares.
    private readonly Dictionary<string, Func<string?>> _checks = new();

    // The settings the options shots overwrite, kept so the shots after them are taken as the player
    // set things. The last shot leaves the HUD scale changed on the live object, which is never saved.
    private bool _held;
    private int _heldNumberMode;
    private float _heldHudScale;
    private bool _heldSubtitles;
    private bool _heldSpeakerNames;
    private float _layoutWidthAtRest;

    // Answers to both spellings; the artifact folder and the log lines follow the one that was typed.
    protected override string Flag =>
        System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--combatshots") >= 0 ? "--combatshots" : "--combat-shots";

    protected override string OutputDir => "user://combat_shots";

    protected override string? ValidateShotState(string name)
    {
        if (Player() is not { } player)
        {
            return "player is not registered";
        }

        if (player.GetComponent<PlayerCameraRig>()?.Camera is not { Current: true })
        {
            return "player has no current gameplay camera";
        }

        if (_subject == null || !IsInstanceValid(_subject))
        {
            return "the enemy was not spawned";
        }

        if (name.Contains("-lock"))
        {
            if (player.GetComponent<LockOnComponent>() is not { TargetNode: not null })
            {
                return "the lock did not engage";
            }
        }

        return _checks.TryGetValue(name, out Func<string?>? check) ? check() : null;
    }

    private void Shot(string name, Action drive, Func<string?> check)
    {
        _checks[name] = check;
        Shot(name, drive);
    }

    private T? Find<T>()
        where T : Node => QuestShotFixtures.FindFirst<T>(GetTree().Root);

    protected override void BuildShotList()
    {
        Shot("01-spawn", () => Spawn("enemy.iron_king"));
        Shot("02-hit-number", () => Hit(18f, crit: false, poise: 1f));
        Shot("03-crit-number", () => Hit(55f, crit: true, poise: 1f));
        // A guard raised and held past the parry window, then struck: a plain block, not a parry.
        Shot("04-guard-up", () => Guard(true));
        Shot("05-blocked", () => Hit(30f, crit: false, poise: 1f));
        Shot("06-telegraph-slam", () => Attack("ironking.slam"));
        Shot("07-telegraph-sweep", () => Attack("ironking.sweep"));
        Shot("08-lock-on-acquire", LockOn);
        Shot("09-lock-on-held", () => { });
        // A boss is never knocked over, so the poise-break and riposte cues need an ordinary body.
        Shot("10-soldier", () => Spawn("enemy.soldier"));
        Shot("11-poise-break", () => Hit(12f, crit: false, poise: 999f));
        Shot("12-riposte", () => Hit(20f, crit: false, poise: 1f));
        // The locked target takes the nameplate (poise bar and state tag); a boss's plate sits under its bar.
        Shot("13-soldier-lock", LockOn);

        // Three soldiers, each wounded by the player. The one being aimed at is named by the plate at
        // the top of the screen, so the world plates are the other two: no enemy carries both.
        Shot("14-enemy-plates", SpawnPack, () =>
            Find<EnemyPlateLayer>() is not { } plates ? "the enemy plate layer is missing"
            : plates.LiveCount != 3 ? $"{plates.LiveCount} enemies hold a plate, expected 3"
            : plates.DrawnCount < 2 ? $"{plates.DrawnCount} plates were drawn, expected at least 2"
            : null);

        // Damage-number modes: crits and kills only drops a plain hit and keeps a crit; off drops both.
        Shot("15-numbers-crits-only-hit", () => NumberMode(DamageNumberRules.CritsAndKills, 6f, crit: false),
            () => Numbers(expectAny: false));
        Shot("16-numbers-crits-only-crit", () => NumberMode(DamageNumberRules.CritsAndKills, 9f, crit: true),
            () => Numbers(expectAny: true));
        Shot("17-numbers-off", () => NumberMode(DamageNumberRules.Off, 6f, crit: true),
            () => Numbers(expectAny: false));

        // A companion's remark through the feed's own path: with subtitles on it is a caption under
        // the speaker's name, not a toast. Then a line nobody is credited with.
        Shot("18-subtitle-speaker", () =>
        {
            RestoreSettings();
            Options(s =>
            {
                s.SubtitlesEnabled = true;
                s.SubtitleSpeakerNames = true;
            });
            Find<Notifications>()?.PushBark("companion.kael", "bark.kael_toren");
        }, () => Find<SubtitleLayer>() is not { ShowingForCapture: not null } layer ? "no line is captioned"
            : !layer.SpeakerShownForCapture ? "the caption does not name its speaker"
            : null);
        Shot("19-subtitle-no-speaker", () =>
        {
            Find<SubtitleLayer>()?.Dismiss();
            Find<SubtitleLayer>()?.Show(null, Loc.T("boss.morthul.intro"), 8f);
        }, () => Find<SubtitleLayer>() is not { ShowingForCapture: not null } layer ? "no line is captioned"
            : layer.SpeakerShownForCapture ? "a line with no speaker shows a name"
            : null);

        // The feed in a fight: a level-up raised a moment after a blow waits, and is shown once the
        // fight is over.
        Shot("20-toast-deferred", () =>
        {
            Find<SubtitleLayer>()?.Dismiss();
            Hit(3f, crit: false, poise: 1f);
            if (Player() is { } player)
            {
                EventBus.Instance?.Publish(new Progression.LeveledUpEvent(player, 7, 1));
            }
        }, () => Find<Notifications>() is not { QueuedForCapture: > 0 } ? "the notice did not wait out the fight" : null);
        Shot("21-toast-released", () => Find<Notifications>()?.EndCombatForCapture(),
            () => Find<Notifications>() is not { ToastCountForCapture: > 0 }
                ? "the held notice was not shown when the fight ended"
                : null);

        // One pickup, then two more of the same item while its toast is still up: one toast, "×3".
        Shot("22-toast-pickup", () => PickUp(1, intoEmptyFeed: true),
            () => Find<Notifications>() is not { } feed ? "the notification feed is missing"
                : feed.QueuedForCapture > 0 ? "the pickup is still waiting behind another toast"
                : feed.ToastCountForCapture != 1 ? "the pickup raised no toast"
                : null);
        Shot("23-toast-collapse-x3", () => PickUp(2),
            () => Find<Notifications>() is not { ToastCountForCapture: 3 }
                ? $"the pickup toast counts {Find<Notifications>()?.ToastCountForCapture ?? 0}, expected 3"
                : null);

        // The boss bar is a share of the width the HUD lays out in: at rest, and with the HUD scaled
        // down to 0.75, which lays a 1280-wide window out about 1707 wide.
        Shot("24-boss-bar", StageBoss, () => BossBar(wide: false));
        Shot("25-boss-bar-wide", () => Options(s => s.HudScale = 0.75f), () => BossBar(wide: true));
    }

    private static IEntity? Player() =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) ? player : null;

    private void Spawn(string archetypeId)
    {
        if (Player() is not { } player ||
            player.Body is not Node3D playerBody ||
            player.GetComponent<PlayerCameraRig>() is not { Camera: { } camera } ||
            EnemyArchetypeDatabase.Get(archetypeId) is not { } archetype)
        {
            return;
        }

        // The disposable capture player ignores damage. The input router stays on: it is what resolves
        // the focus the nameplate shows, and with no input arriving it moves nothing.
        if (player.GetComponent<CombatComponent>() is { } combat)
        {
            combat.IsInvulnerable = true;
        }

        if (_subject != null && IsInstanceValid(_subject))
        {
            Player()?.GetComponent<LockOnComponent>()?.ToggleNearest(); // drop the old lock
            _subject.QueueFree();
        }

        foreach (EnemyEntity extra in _pack)
        {
            if (IsInstanceValid(extra))
            {
                extra.QueueFree();
            }
        }

        _pack.Clear();

        Vector3 forward = -camera.GlobalBasis.Z;
        forward.Y = 0f;
        forward = forward.LengthSquared() < 1e-4f ? Vector3.Forward : forward.Normalized();
        Vector3 at = playerBody.GlobalPosition + (forward * Range) + new Vector3(0f, 0.04f, 0f);

        _subject = EnemyArchetypeFactory.Create(archetype, at);
        GetTree().CurrentScene.AddChild(_subject);
        _subject.LookAt(new Vector3(playerBody.GlobalPosition.X, _subject.GlobalPosition.Y, playerBody.GlobalPosition.Z),
            Vector3.Up);
        if (_subject.GetComponent<EnemyAIComponent>() is { } ai)
        {
            ai.ProcessMode = ProcessModeEnum.Disabled;
        }
    }

    /// <summary>A soldier in front of the player and one to either side, each struck once by the player.</summary>
    private void SpawnPack()
    {
        Spawn("enemy.soldier");
        if (_subject == null || !IsInstanceValid(_subject) || Player()?.Body is not Node3D playerBody ||
            EnemyArchetypeDatabase.Get("enemy.soldier") is not { } archetype)
        {
            return;
        }

        Vector3 side = _subject.GlobalBasis.X.Normalized();
        foreach (float offset in new[] { -3f, 3f })
        {
            EnemyEntity extra = EnemyArchetypeFactory.Create(archetype, _subject.GlobalPosition + (side * offset));
            GetTree().CurrentScene.AddChild(extra);
            extra.LookAt(new Vector3(playerBody.GlobalPosition.X, extra.GlobalPosition.Y, playerBody.GlobalPosition.Z),
                Vector3.Up);
            if (extra.GetComponent<EnemyAIComponent>() is { } ai)
            {
                ai.ProcessMode = ProcessModeEnum.Disabled;
            }

            _pack.Add(extra);
            extra.GetComponent<CombatComponent>()?.ReceiveDamage(
                new DamagePacket(10f, DamageType.Physical, Player(), false, 1f));
        }

        Hit(10f, crit: false, poise: 1f);
    }

    /// <summary>Sets the damage-number mode, clears the numbers on screen and lands one blow, so the
    /// count afterwards is what that blow added under that mode.</summary>
    private void NumberMode(int mode, float amount, bool crit)
    {
        Options(s => s.DamageNumberMode = mode);
        Find<DamageNumberLayer>()?.ClearForCapture();
        Hit(amount, crit, poise: 1f);
    }

    private string? Numbers(bool expectAny)
    {
        if (Find<DamageNumberLayer>() is not { } numbers)
        {
            return "the damage number layer is missing";
        }

        return expectAny == numbers.LiveCount > 0
            ? null
            : $"{numbers.LiveCount} damage number(s) on screen, expected {(expectAny ? "at least one" : "none")}";
    }

    /// <summary>Picks the staged item up <paramref name="times"/> times through the event a real
    /// pickup raises, with the fight over and the feed's merge window skipped.
    ///
    /// The first pickup goes into an emptied feed. At 853x533 logical the level-up toast the two
    /// shots before raised is two lines tall and is the only toast that fits between the tracker and
    /// the minimap, so the pickup waited behind it: '22' photographed the level-up and passed on its
    /// count, and '23' then added to a notice that was still queued and read 1 where it expected 3.
    /// The feed was right both times; the harness was photographing the wrong toast.</summary>
    private void PickUp(int times, bool intoEmptyFeed = false)
    {
        RestoreSettings();
        if (Player() is not { } player || ItemDatabase.Get(PickupItem) is not { } item ||
            Find<Notifications>() is not { } feed)
        {
            return;
        }

        feed.EndCombatForCapture();
        if (intoEmptyFeed)
        {
            feed.ClearShownForCapture();
        }

        for (int i = 0; i < times; i++)
        {
            EventBus.Instance?.Publish(new ItemPickedUpEvent(player, item, 1));
        }

        feed.FlushLootForCapture();
    }

    private void StageBoss()
    {
        if (_subject == null || !IsInstanceValid(_subject) || Find<BossFrame>() is not { } frame)
        {
            return;
        }

        _layoutWidthAtRest = Find<GameHud>()?.LayoutWidth ?? 0f;
        frame.Present(_subject, Loc.T("boss.name"), 3, "boss.iron_king.epithet", "boss.iron_king.intro");
    }

    private string? BossBar(bool wide)
    {
        if (Find<BossFrame>() is not { Visible: true } frame || Find<GameHud>() is not { } hud)
        {
            return "the boss frame is not up";
        }

        float expected = HudMetrics.BossBarWidth(hud.LayoutWidth);
        if (Mathf.Abs(frame.BarWidthForCapture - expected) > 1f)
        {
            return $"the boss bar is {frame.BarWidthForCapture:0} wide, expected {expected:0} at layout width {hud.LayoutWidth:0}";
        }

        return wide && hud.LayoutWidth < _layoutWidthAtRest * 1.3f
            ? $"the HUD lays out {hud.LayoutWidth:0} wide, expected about {_layoutWidthAtRest / 0.75f:0}"
            : null;
    }

    /// <summary>Changes the live settings and announces them the way applying the options screen
    /// does (not <see cref="SettingsService.Apply"/>, which would also re-apply the window this
    /// harness sized). What was there is kept for <see cref="RestoreSettings"/>.</summary>
    private void Options(Action<Settings.Settings> change)
    {
        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            return;
        }

        if (!_held)
        {
            _held = true;
            _heldNumberMode = settings.Current.DamageNumberMode;
            _heldHudScale = settings.Current.HudScale;
            _heldSubtitles = settings.Current.SubtitlesEnabled;
            _heldSpeakerNames = settings.Current.SubtitleSpeakerNames;
        }

        change(settings.Current);
        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
    }

    private void RestoreSettings()
    {
        if (!_held || ServiceLocator.Instance is not { } locator || !locator.TryGet(out SettingsService settings))
        {
            return;
        }

        _held = false;
        settings.Current.DamageNumberMode = _heldNumberMode;
        settings.Current.HudScale = _heldHudScale;
        settings.Current.SubtitlesEnabled = _heldSubtitles;
        settings.Current.SubtitleSpeakerNames = _heldSpeakerNames;
        EventBus.Instance?.Publish(new SettingsAppliedEvent(settings.Current));
    }

    private void Hit(float amount, bool crit, float poise)
    {
        if (_subject?.GetComponent<CombatComponent>() is { } combat)
        {
            combat.ReceiveDamage(new DamagePacket(amount, DamageType.Physical, Player(), crit, poise));
        }
    }

    private void Guard(bool up)
    {
        if (_subject?.GetComponent<CombatComponent>() is { } combat)
        {
            combat.IsBlocking = up;
        }
    }

    private void Attack(string actionId)
    {
        if (_subject?.GetComponent<CharacterActionComponent>() is { } action)
        {
            action.Cancel();
            action.TryStartById(actionId);
        }
    }

    private void LockOn()
    {
        _subject?.GetComponent<CharacterActionComponent>()?.Cancel();
        if (Player() is not { } player || player.GetComponent<LockOnComponent>() is not { } lockOn)
        {
            return;
        }

        lockOn.ToggleNearest();
        Core.Diagnostics.Log.Info($"{Flag}: lock after toggle: target={lockOn.TargetNode?.Name ?? "none"}");
    }
}

using Embervale.Combat;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// World-anchored health plates over the enemies the player is fighting: a keylined health bar with
/// the length the last blow removed held behind it, and a thin poise bar under that.
///
/// An enemy gets a plate when the player wounds it, when it wounds the player, when it turns on
/// the player, or when it is locked, and keeps it while <see cref="EnemyPlateRules.Live"/> says it
/// is still relevant. At most <see cref="MaxPlates"/> are up; a ninth takes the least relevant
/// one's place. Two things never carry a plate: a boss, whose plate is the boss frame, and the
/// target the top-centre nameplate is already naming (the locked target, else the one aimed at).
///
/// A child of <see cref="CombatFeedbackOverlay"/>, beside the damage numbers and the lock cues, so
/// it shares their canvas layer and their visibility rule. Drawn as rects in one <c>_Draw</c>, from
/// a fixed pool, at positions unprojected each frame. It ticks only while a plate is up: the events
/// that claim one wake it, and a load or the last plate leaving puts it back to sleep.
/// </summary>
public sealed partial class EnemyPlateLayer : Control
{
    /// <summary>The most plates on screen at once.</summary>
    public const int MaxPlates = 8;

    private const float PlateWidth = 72f;
    private const float HealthHeight = HudCoreMetrics.BarThinHeight + 2f;
    private const float PoiseHeight = 2f;

    /// <summary>Metres a plate floats over its enemy's head.</summary>
    private const float HeadClearance = 0.3f;

    /// <summary>The height assumed for a body with no capsule to measure.</summary>
    private const float DefaultHeight = 1.9f;

    private sealed class Plate
    {
        public IEntity? Enemy;
        public Node3D Body = null!;
        public StatsComponent Stats = null!;
        public CombatComponent? Combat;
        public EnemyAIComponent? Ai;
        public float Height;
        public bool Hostile;
        public bool Aggro;
        public bool Locked;
        public double TouchedAt;
        public double Health;
        public double Lag;
        public double LagHold;
        public float Poise;
        public bool Drawn;
        public Vector2 At;
        public float Alpha;
    }

    private readonly Plate[] _plates = new Plate[MaxPlates];
    private readonly PlateClaim[] _claims = new PlateClaim[MaxPlates];
    private int _live;

    // The player's own components, looked up when the player changes and not every frame.
    private IEntity? _player;
    private LockOnComponent? _lockOn;
    private InteractionSensor? _sensor;

    // What the HUD options say, read when settings are applied.
    private bool _shown = true;
    private bool _namedElsewhere = true;

    /// <summary>How many enemies hold a plate, drawn or not. For the probe and the harness.</summary>
    public int LiveCount => _live;

    /// <summary>How many plates the last frame drew. For the harness.</summary>
    public int DrawnCount
    {
        get
        {
            int drawn = 0;
            foreach (Plate plate in _plates)
            {
                drawn += plate is { Enemy: not null, Drawn: true } ? 1 : 0;
            }

            return drawn;
        }
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        for (int i = 0; i < _plates.Length; i++)
        {
            _plates[i] = new Plate();
        }

        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<HitConfirmedEvent>(OnHit);
        bus?.Subscribe<EnemyStateChangedEvent>(OnEnemyState);
        bus?.Subscribe<LockChangedEvent>(OnLock);
        bus?.Subscribe<EntityDiedEvent>(OnDied);
        bus?.Subscribe<GameLoadedEvent>(OnLoaded);
        bus?.Subscribe<SettingsAppliedEvent>(OnSettings);
        ReadOptions();
        SetProcess(false);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<HitConfirmedEvent>(OnHit);
        bus?.Unsubscribe<EnemyStateChangedEvent>(OnEnemyState);
        bus?.Unsubscribe<LockChangedEvent>(OnLock);
        bus?.Unsubscribe<EntityDiedEvent>(OnDied);
        bus?.Unsubscribe<GameLoadedEvent>(OnLoaded);
        bus?.Unsubscribe<SettingsAppliedEvent>(OnSettings);
    }

    // --- What claims a plate -------------------------------------------------------------------

    private void OnHit(HitConfirmedEvent e)
    {
        if (e.ByPlayer)
        {
            Touch(e.Target);
        }

        if (e.OnPlayer && e.Source != null)
        {
            Touch(e.Source);
        }
    }

    /// <summary>An enemy that has just turned on the player is worth a plate before the first blow.
    /// One fighting somebody else is not.</summary>
    private void OnEnemyState(EnemyStateChangedEvent e)
    {
        if (e.State == EnemyState.Combat &&
            e.Enemy.GetComponent<EnemyAIComponent>() is { IsHostileToPlayer: true })
        {
            Touch(e.Enemy);
        }
    }

    private void OnLock(LockChangedEvent e)
    {
        if (e.Target != null && CombatPerspective.IsPlayer(e.Player))
        {
            Touch(e.Target);
        }
    }

    private void OnDied(EntityDiedEvent e)
    {
        if (Find(e.Entity) is { } plate)
        {
            Release(plate);
            QueueRedraw();
        }
    }

    // A load replaces the actors the plates were holding.
    private void OnLoaded(GameLoadedEvent e) => ReleaseAll();

    private void OnSettings(SettingsAppliedEvent e)
    {
        ReadOptions();
        if (!_shown)
        {
            ReleaseAll();
        }
    }

    private void ReadOptions()
    {
        _shown = EnemyPlateRules.Shows(GameHud.ElementMode(HudElement.EnemyPlates));
        _namedElsewhere = GameHud.ElementMode(HudElement.TargetPlate) != HudElementMode.Hidden;
    }

    /// <summary>Gives <paramref name="enemy"/> a plate if it has none, and marks it as just seen.</summary>
    private void Touch(IEntity enemy)
    {
        if (!_shown || enemy is not Node node || !IsInstanceValid(node) || CombatPerspective.IsPlayer(enemy) ||
            enemy.GetComponent<StatsComponent>() is not { IsAlive: true } stats ||
            enemy.GetComponent<BossController>() != null)
        {
            return;
        }

        Plate plate = Find(enemy) ?? Claim(enemy, stats);
        plate.TouchedAt = Now();
        plate.Hostile = plate.Ai is { IsHostileToPlayer: true };
        SetProcess(true);
    }

    private Plate? Find(IEntity enemy)
    {
        foreach (Plate plate in _plates)
        {
            if (ReferenceEquals(plate.Enemy, enemy))
            {
                return plate;
            }
        }

        return null;
    }

    private Plate Claim(IEntity enemy, StatsComponent stats)
    {
        for (int i = 0; i < _plates.Length; i++)
        {
            Plate held = _plates[i];
            _claims[i] = new PlateClaim(held.Enemy != null, held.Aggro, held.Locked, held.TouchedAt);
        }

        Plate plate = _plates[EnemyPlateRules.SlotFor(_claims)];
        if (plate.Enemy == null)
        {
            _live++;
        }

        plate.Enemy = enemy;
        plate.Body = enemy.Body;
        plate.Stats = stats;
        plate.Combat = enemy.GetComponent<CombatComponent>();
        plate.Ai = enemy.GetComponent<EnemyAIComponent>();
        plate.Height = MeasureHeight(enemy.Body);
        plate.Health = stats.GetNormalized(StatType.Health);
        plate.Lag = plate.Health;
        plate.LagHold = 0d;
        plate.Aggro = false;
        plate.Locked = false;
        plate.Drawn = false;
        return plate;
    }

    /// <summary>The top of the body's capsule above its origin. Read once, when the plate is claimed.</summary>
    private static float MeasureHeight(Node3D body)
    {
        int children = body.GetChildCount();
        for (int i = 0; i < children; i++)
        {
            if (body.GetChild(i) is CollisionShape3D { Shape: CapsuleShape3D capsule } shape)
            {
                return shape.Position.Y + (capsule.Height * 0.5f);
            }
        }

        return DefaultHeight;
    }

    private void Release(Plate plate)
    {
        if (plate.Enemy == null)
        {
            return;
        }

        plate.Enemy = null;
        plate.Combat = null;
        plate.Ai = null;
        plate.Drawn = false;
        _live--;
    }

    private void ReleaseAll()
    {
        foreach (Plate plate in _plates)
        {
            Release(plate);
        }

        SetProcess(false);
        QueueRedraw();
    }

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    // --- The tick ------------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        double now = Now();
        Camera3D? camera = GetViewport().GetCamera3D();
        IEntity? locked = ResolvePlayer() ? _lockOn?.Target : null;

        // The nameplate at the top of the screen names the locked target, else the one aimed at.
        IEntity? named = !_namedElsewhere ? null : locked ?? _sensor?.FocusedEntity;
        bool motion = UiTheme.MotionEnabled;
        bool dirty = false;

        foreach (Plate plate in _plates)
        {
            if (plate.Enemy == null)
            {
                continue;
            }

            if (plate.Enemy is not Node node || !IsInstanceValid(node) || !IsInstanceValid(plate.Body) ||
                !IsInstanceValid(plate.Stats) || !plate.Stats.IsAlive)
            {
                Release(plate);
                dirty = true;
                continue;
            }

            plate.Locked = ReferenceEquals(plate.Enemy, locked);
            plate.Aggro = plate.Hostile && plate.Ai is { State: EnemyState.Combat };
            if (!EnemyPlateRules.Live(now, plate.TouchedAt, plate.Aggro, plate.Locked))
            {
                Release(plate);
                dirty = true;
                continue;
            }

            double health = plate.Stats.GetNormalized(StatType.Health);
            double lagBefore = plate.Lag;
            if (!motion)
            {
                plate.Lag = health;
            }
            else
            {
                if (health < plate.Health - 0.001d)
                {
                    plate.Lag = JuicedBarRules.OnDrop(plate.Lag, plate.Health);
                    plate.LagHold = JuicedBarRules.LagHoldSeconds;
                }

                (plate.Lag, plate.LagHold) = JuicedBarRules.Step(plate.Lag, plate.LagHold, health, delta);
            }

            float poise = plate.Combat?.PoiseNormalized ?? -1f;
            bool drawn = false;
            Vector2 at = default;
            float alpha = 0f;
            if (camera != null && !ReferenceEquals(plate.Enemy, named))
            {
                Vector3 head = plate.Body.GlobalPosition + (Vector3.Up * (plate.Height + HeadClearance));
                if (!camera.IsPositionBehind(head))
                {
                    alpha = EnemyPlateRules.Alpha(camera.GlobalPosition.DistanceTo(head));
                    if (alpha > 0f)
                    {
                        at = camera.UnprojectPosition(head).Round();
                        drawn = true;
                    }
                }
            }

            dirty |= drawn != plate.Drawn || at != plate.At || alpha != plate.Alpha || health != plate.Health ||
                     plate.Lag != lagBefore || poise != plate.Poise;
            plate.Health = health;
            plate.Poise = poise;
            plate.Drawn = drawn;
            plate.At = at;
            plate.Alpha = alpha;
        }

        if (dirty)
        {
            QueueRedraw();
        }

        if (_live == 0)
        {
            SetProcess(false); // nothing to follow until an event claims a plate
        }
    }

    /// <summary>Refreshes the player's lock-on and aim components when the player changed (a new
    /// session, a rebuilt actor). False with no player.</summary>
    private bool ResolvePlayer()
    {
        IEntity? player =
            ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter found) ? found : null;
        if (!ReferenceEquals(player, _player) || (_lockOn != null && !IsInstanceValid(_lockOn)))
        {
            _player = player;
            _lockOn = player?.GetComponent<LockOnComponent>();
            _sensor = player?.GetComponent<InteractionSensor>();
        }

        return player != null;
    }

    // --- The drawing ---------------------------------------------------------------------------

    public override void _Draw()
    {
        foreach (Plate plate in _plates)
        {
            if (plate.Enemy == null || !plate.Drawn)
            {
                continue;
            }

            float alpha = plate.Alpha;
            float left = plate.At.X - (PlateWidth * 0.5f);
            float top = plate.At.Y - HealthHeight - PoiseHeight - 1f;

            var health = new Rect2(left, top, PlateWidth, HealthHeight);
            DrawRect(health, Faded(UiTheme.Trough, alpha));
            float fill = Mathf.Round((float)plate.Health * PlateWidth);
            if (!JuicedBarRules.Settled(plate.Lag, plate.Health))
            {
                float chunk = Mathf.Round((float)plate.Lag * PlateWidth);
                DrawRect(new Rect2(left + fill, top, chunk - fill, HealthHeight), Faded(UiTheme.HudChunk, alpha));
            }

            DrawRect(new Rect2(left, top, fill, HealthHeight), Faded(UiTheme.Health, alpha));
            Keyline(health, alpha);

            // Poise sits under the health bar, a third of its height: what is left before it breaks.
            if (plate.Poise >= 0f)
            {
                var poise = new Rect2(left, top + HealthHeight + 1f, PlateWidth, PoiseHeight);
                DrawRect(poise, Faded(UiTheme.Trough, alpha));
                DrawRect(new Rect2(poise.Position, new Vector2(Mathf.Round(plate.Poise * PlateWidth), PoiseHeight)),
                    Faded(UiTheme.Poise, alpha));
                DrawRect(poise.Grow(0.5f), Faded(UiTheme.Keyline, alpha), false, 1f);
            }
        }
    }

    /// <summary><see cref="UiTheme.DrawKeyline"/>, at the plate's own opacity.</summary>
    private void Keyline(Rect2 rect, float alpha)
    {
        DrawRect(rect.Grow(0.5f), Faded(UiTheme.Keyline, alpha), false, 1f);
        DrawRect(rect.Grow(-0.5f), Faded(UiTheme.HudInnerEdge, alpha), false, 1f);
    }

    private static Color Faded(Color color, float alpha) => color with { A = color.A * alpha };
}

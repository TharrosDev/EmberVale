using System;
using Embervale.Combat.Actions;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.Combat;

/// <summary>
/// Camera shake: per-source trauma turned into a smooth shudder plus a directional kick, as a camera
/// <b>layer</b>. It never touches the camera transform; <see cref="Sample"/> returns a
/// <see cref="CameraNudge"/> and <c>PlayerCameraRig</c> sums it with every other layer and writes the
/// camera once. Single-player: only hits the player deals or takes, the player's staggers and the
/// player's own actions register, so a fight between two NPCs never shakes the view.
///
/// <para>Everything scales by <see cref="CameraComfort.Shake"/> (the settings slider, capped when
/// Reduced Motion is on). The maths lives in <see cref="ShakeMath"/>.</para>
/// </summary>
public partial class CameraShake : EntityComponent, ICameraLayer
{
    /// <summary>The largest frame the layer integrates, so a hitch cannot fling the kick springs.</summary>
    private const float MaxDt = 0.1f;

    private readonly TraumaPool _pool = new();
    private CriticalSpring _kickX;
    private CriticalSpring _kickZ;
    private CriticalSpring _kickRoll;
    private float _time;
    private SettingsService? _settings;
    private StatsComponent? _stats;

    protected override void OnInitialize()
    {
        _stats = Entity!.GetComponent<StatsComponent>();
        _settings = ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? settings
            : null;

        EventBus.Instance?.Subscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Subscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Subscribe<ActionReleasedEvent>(OnActionReleased);
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<DamageDealtEvent>(OnDamage);
        EventBus.Instance?.Unsubscribe<EntityStaggeredEvent>(OnStaggered);
        EventBus.Instance?.Unsubscribe<ActionReleasedEvent>(OnActionReleased);
    }

    private bool IsPlayer(IEntity? other) =>
        other != null && Entity != null && ReferenceEquals(other.Body, Entity.Body);

    private void OnDamage(DamageDealtEvent e)
    {
        if (IsPlayer(e.Target))
        {
            float maxHealth = _stats?.GetMax(StatType.Health) ?? 0f;
            Submit(ShakeMath.HitTaken(e.Amount, maxHealth, e.IsBlocked, e.IsCrit), FromSource(e.Source));
        }
        else if (IsPlayer(e.Source))
        {
            Submit(ShakeMath.HitDealt(e.IsCrit, e.IsBlocked));
        }
    }

    private void OnStaggered(EntityStaggeredEvent e)
    {
        if (Entity == null)
        {
            return;
        }

        bool player = IsPlayer(e.Entity);
        float distance = player ? 0f : e.Entity.Body.GlobalPosition.DistanceTo(Entity.Body.GlobalPosition);
        Submit(ShakeMath.Stagger(player, distance), player ? null : FromSource(e.Entity));
    }

    /// <summary>
    /// An action's own authored kick, on the frame it lands.
    ///
    /// ⚠️ <b>Only the PLAYER's actions shake the player's camera.</b> Every actor in the region
    /// publishes this event, and a camera that kicked for all of them would shudder continuously
    /// during any fight involving more than two people. What an enemy's blow does to the camera is
    /// already covered: it is a DamageDealtEvent when it connects.
    /// </summary>
    private void OnActionReleased(ActionReleasedEvent e)
    {
        if (IsPlayer(e.Actor) &&
            e.Actor is IEntity actor &&
            actor.GetComponent<CharacterActionComponent>()?.Current is { } action)
        {
            Submit(new ShakeHit(
                e.Kind == ActionKind.Cast ? ShakeSource.Spell : ShakeSource.Action,
                action.CameraImpulse));
        }
    }

    /// <summary>
    /// The one way anything else moves the camera: submit an impulse, in the same 0..1 trauma the
    /// hit reactions use.
    ///
    /// ⚠️ <b>Combat, spells and landings submit; they never write the transform.</b> Before this the
    /// only camera motion came from the two events above, so an action wanting a knock had nowhere
    /// to put it but the camera's own position — and two systems writing one transform is how a
    /// camera ends up fighting itself. <c>ActionDefinitionResource.CameraImpulse</c> is authored per
    /// action and arrives here.
    /// </summary>
    public void Impulse(float trauma) => Submit(new ShakeHit(ShakeSource.Action, trauma));

    /// <summary>
    /// A directional impulse: the same trauma, plus a kick that pushes the camera away from
    /// <paramref name="worldFrom"/>, the world-space position the blow came from. The roll leans
    /// toward the side it hit, so the player reads which way to turn.
    /// </summary>
    public void Impulse(float trauma, Vector3 worldFrom) =>
        Submit(new ShakeHit(ShakeSource.Action, trauma), worldFrom);

    /// <summary>Adds a resolved hit to the pool and, when it carries a source position, the kick.</summary>
    public void Submit(ShakeHit hit, Vector3? worldFrom = null)
    {
        if (hit.Trauma <= 0f)
        {
            return;
        }

        _pool.Add(hit.Source, hit.Trauma);
        if (worldFrom is not { } from || Entity == null)
        {
            return;
        }

        Vector3 flat = from - Entity.Body.GlobalPosition;
        (float side, float forward) = ShakeMath.ToLocal(flat.X, flat.Z, Entity.Body.GlobalRotation.Y);
        DirectionalKick kick = ShakeMath.KickFor(hit.Trauma, side, forward);
        float limit = ShakeMath.KickLimit;
        _kickX.Kick(kick.OffsetX, ShakeMath.KickOmega, ShakeMath.KickOffset * limit);
        _kickZ.Kick(kick.OffsetZ, ShakeMath.KickOmega, ShakeMath.KickBack * limit);
        _kickRoll.Kick(kick.Roll, ShakeMath.KickOmega, ShakeMath.KickRoll * limit);
    }

    /// <summary>The blow's position, or null when the source is gone or is the player.</summary>
    private Vector3? FromSource(IEntity? source) =>
        source == null || IsPlayer(source) || !GodotObject.IsInstanceValid(source.Body)
            ? null
            : source.Body.GlobalPosition;

    public CameraNudge Sample(float dt, in CameraSnapshot snapshot)
    {
        dt = Math.Clamp(dt, 0f, MaxDt);
        _pool.Step(dt);
        _kickX.Step(dt, ShakeMath.KickOmega);
        _kickZ.Step(dt, ShakeMath.KickOmega);
        _kickRoll.Step(dt, ShakeMath.KickOmega);
        _time += dt;

        float scale = CameraComfort.From(_settings?.Current).Shake;
        if (scale <= 0f)
        {
            return CameraNudge.Identity;
        }

        Shudder shudder = ShakeMath.Shake(_pool.Total, _time);
        return new CameraNudge(
            new Vector3(
                (shudder.OffsetX + _kickX.X) * scale,
                shudder.OffsetY * scale,
                _kickZ.X * scale),
            new Vector3(0f, 0f, (shudder.Roll + _kickRoll.X) * scale),
            0f,
            1f);
    }
}

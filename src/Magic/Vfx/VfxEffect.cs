using System;
using Embervale.Animation;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// Where a following effect should be this frame: a node (plus an offset), or a caster's casting
/// hand with the caster's body as the fallback. An effect never becomes a child of what it follows.
/// It copies a position each frame, and only after checking the thing is still alive and in the
/// tree, so an actor freed mid-cast or a bolt returned to its pool cannot take an effect with it or
/// be reached through one.
/// </summary>
internal readonly struct VfxAnchor
{
    private readonly Node3D? _node;
    private readonly Vector3 _offset;
    private readonly CharacterAnimationComponent? _hand;

    private VfxAnchor(Node3D? node, Vector3 offset, CharacterAnimationComponent? hand)
    {
        _node = node;
        _offset = offset;
        _hand = hand;
    }

    /// <summary>Follows <paramref name="node"/>, <paramref name="offset"/> metres from its origin
    /// (in world axes).</summary>
    public static VfxAnchor To(Node3D node, Vector3 offset = default) => new(node, offset, null);

    /// <summary>Follows <paramref name="caster"/>'s casting hand; a body with no hand is followed at
    /// <paramref name="fallbackOffset"/> above its origin instead.</summary>
    public static VfxAnchor ToHand(IEntity caster, Vector3 fallbackOffset)
    {
        Node3D? body = BodyOf(caster);
        CharacterAnimationComponent? hand = body == null ? null : caster.GetComponent<CharacterAnimationComponent>();
        return new VfxAnchor(body, fallbackOffset, hand);
    }

    /// <summary>The node followed, when it is still there.</summary>
    public Node3D? Node => Alive(_node) ? _node : null;

    /// <summary>The position to stand at. False when what was followed is gone.</summary>
    public bool TryResolve(out Vector3 position)
    {
        if (_hand != null && GodotObject.IsInstanceValid(_hand) && _hand.IsInsideTree() &&
            _hand.TryGetCastingHand(out position))
        {
            return true;
        }

        if (Alive(_node))
        {
            position = _node!.GlobalPosition + _offset;
            return true;
        }

        position = default;
        return false;
    }

    /// <summary>An entity's body, or null when the entity or its body has been freed.</summary>
    public static Node3D? BodyOf(IEntity? entity)
    {
        if (entity == null || (entity is GodotObject asObject && !GodotObject.IsInstanceValid(asObject)))
        {
            return null;
        }

        Node3D? body = entity.Body;
        return Alive(body) ? body : null;
    }

    private static bool Alive(Node3D? node) =>
        node != null && GodotObject.IsInstanceValid(node) && node.IsInsideTree();
}

/// <summary>
/// The base of every pooled effect block. It owns the three rules the whole layer rests on:
///
/// <list type="bullet">
/// <item>An effect <b>ages in <c>_Process</c></b> and nowhere else. No tweens, no SceneTree timers:
/// a paused tree pauses every effect, and nothing can fire after its node has gone back to a pool.</item>
/// <item>An effect <b>follows by copying a position</b> behind a validity check
/// (<see cref="VfxAnchor"/>), and when what it follows is gone it fades out where it stands.</item>
/// <item>An effect <b>ends exactly once</b>: <see cref="Finish"/> returns what it borrowed (a light,
/// a place in the ledger) and hands the node to its pool. <see cref="Serial"/> changes every time
/// the node is reused, so a stale <see cref="VfxHandle"/> cannot stop somebody else's effect.</item>
/// </list>
///
/// A block builds its meshes and materials in its constructor, as every pooled node here does: its
/// <c>_Ready</c> runs once in its life, and it is restyled on every reuse.
/// </summary>
public abstract partial class VfxEffect : Node3D
{
    /// <summary>Seconds a sustained effect takes to fade once it is stopped.</summary>
    protected const float StopFadeSeconds = 0.18f;

    private VfxAnchor _anchor;
    private bool _follows;
    private Vector3 _settleOffset;
    private float _settleSeconds;
    private bool _settleAccelerates;
    private bool _settles;
    private Node3D? _orientLike;
    private bool _deferred;
    private Vector3 _glide;
    private bool _glides;

    /// <summary>The pool's return. Null for an effect built outside a pool, which frees itself.</summary>
    internal Action<VfxEffect>? Reclaim { get; set; }

    internal SpellVfxDirector? Director { get; private set; }

    /// <summary>The ledger group this block belongs to. 0 = not counted.</summary>
    internal int Group { get; private set; }

    /// <summary>Changes on every reuse of the node.</summary>
    internal int Serial { get; private set; }

    /// <summary>True from <see cref="Begin"/> until <see cref="Finish"/>.</summary>
    internal bool Live { get; private set; }

    /// <summary>Seconds since <see cref="Begin"/>.</summary>
    protected double Age { get; private set; }

    /// <summary>True once <see cref="Stop"/> has been called; <see cref="StopAge"/> counts from then.</summary>
    protected bool Stopping { get; private set; }

    protected double StopAge { get; private set; }

    /// <summary>Starts a new life. Called by the spawner after the node is in the tree.</summary>
    internal void Begin(SpellVfxDirector director, int group)
    {
        Director = director;
        Group = group;
        Serial = unchecked(Serial + 1);
        Live = true;
        Age = 0d;
        Stopping = false;
        StopAge = 0d;
        _follows = false;
        _settles = false;
        _orientLike = null;
        _deferred = false;
        _glides = false;

        // A pooled node keeps the transform of its last life. One that was turned to match a wall
        // (OrientLike) would otherwise carry that turn into its next use, and an emitter's throw
        // direction is turned with its node: the next cone sprayed from it would point the wall's way.
        Transform = Transform3D.Identity;
        Visible = true;
    }

    /// <summary>
    /// Holds the effect back, hidden, until its first frame. For an effect that follows a node the
    /// caller positions just <em>after</em> telling the effect layer about it (a ground spell, a
    /// zone, a wall, a totem): without this it would draw one frame wherever that node was born.
    /// Call between <see cref="Begin"/> and the block's own <c>Arm</c>.
    /// </summary>
    internal void Defer()
    {
        _deferred = true;
        Visible = false;
    }

    /// <summary>True while the effect is still held back by <see cref="Defer"/>.</summary>
    protected bool IsDeferred => _deferred;

    /// <summary>Takes its facing from <paramref name="node"/> every frame, for as long as that node
    /// is alive (a wall that is turned after its effect is attached).</summary>
    internal void OrientLike(Node3D node) => _orientLike = node;

    /// <summary>Follows <paramref name="anchor"/> from now on.</summary>
    internal void Follow(in VfxAnchor anchor)
    {
        _anchor = anchor;
        _follows = true;
    }

    /// <summary>
    /// Starts <paramref name="offset"/> away from the followed point and settles onto it over
    /// <paramref name="seconds"/> (by default <see cref="VfxRecipeRules.HandSettleSeconds"/>: a
    /// bolt's picture leaving the hand while the bolt itself flies from the aim origin).
    /// <paramref name="accelerate"/> makes it arrive fast instead of leave fast (a meteor).
    /// </summary>
    internal void SettleFrom(
        Vector3 offset, float seconds = VfxRecipeRules.HandSettleSeconds, bool accelerate = false)
    {
        _settleOffset = offset;
        _settleSeconds = seconds;
        _settleAccelerates = accelerate;
        _settles = offset.LengthSquared() > 0.0001f;
    }

    /// <summary>
    /// What it follows moves at <paramref name="velocity"/> (metres a second), in physics steps. A
    /// bolt is moved once a physics tick, and a picture that copied it would visibly step at any
    /// frame rate above the tick rate; with this, the picture is carried on along the velocity for
    /// the part of a tick that has passed, so it glides.
    /// </summary>
    internal void Glide(Vector3 velocity)
    {
        _glide = velocity;
        _glides = velocity.LengthSquared() > 0.0001f;
    }

    /// <summary>Asks the effect to end gracefully: a sustained one fades, an emitter stops emitting
    /// and lets its particles die. A one-shot simply carries on to its end.</summary>
    internal void Stop()
    {
        if (!Live || Stopping)
        {
            return;
        }

        Stopping = true;
        StopAge = 0d;
        _glides = false; // whatever it followed has stopped moving, or is about to be gone
        OnStop();
    }

    /// <summary>Ends the effect now (the budget recycled it, or its owner was torn down).</summary>
    internal void Kill() => Finish();

    /// <summary>Marks the effect dead without returning it to a pool: the director is freeing every
    /// node under it and nothing here may run again.</summary>
    internal void Abandon()
    {
        Live = false;
        Director = null;
        SetProcess(false);
    }

    public override void _Process(double delta)
    {
        if (!Live)
        {
            return;
        }

        Age += delta;
        if (Stopping)
        {
            StopAge += delta;
        }

        if (_follows)
        {
            if (_anchor.TryResolve(out Vector3 position))
            {
                if (_settles)
                {
                    float left = VfxRecipeRules.SettleLeft(Age, _settleSeconds, _settleAccelerates);
                    position += _settleOffset * left;
                    _settles = left > 0f;
                }

                if (_glides)
                {
                    position += _glide * (float)(Engine.GetPhysicsInterpolationFraction() /
                                                 Mathf.Max(1, Engine.PhysicsTicksPerSecond));
                }

                GlobalPosition = position;
            }
            else
            {
                // What it followed is gone: stay put and wind down.
                _follows = false;
                Stop();
            }
        }

        if (_orientLike != null)
        {
            if (IsInstanceValid(_orientLike) && _orientLike.IsInsideTree())
            {
                GlobalBasis = _orientLike.GlobalBasis.Orthonormalized();
            }
            else
            {
                _orientLike = null;
            }
        }

        if (_deferred)
        {
            // Its first frame: it now stands where it belongs, so it may be seen. The frame it was
            // held back for does not count toward its age.
            _deferred = false;
            Age = 0d;
            Visible = true;
            OnShown();
        }

        if (!Tick((float)delta))
        {
            Finish();
        }
    }

    /// <summary>One frame. Returns false when the effect is over.</summary>
    protected abstract bool Tick(float delta);

    /// <summary>The effect was asked to end gracefully.</summary>
    protected virtual void OnStop()
    {
    }

    /// <summary>A deferred effect has just been placed and shown (see <see cref="Defer"/>).</summary>
    protected virtual void OnShown()
    {
    }

    /// <summary>The effect is over: return anything borrowed. Runs once per life.</summary>
    protected virtual void OnFinish()
    {
    }

    /// <summary>How far a stopped effect has faded: 1 when it was just stopped (or never was), 0
    /// when it is gone.</summary>
    protected float StopFade(float seconds = StopFadeSeconds) =>
        Stopping ? Mathf.Clamp(1f - ((float)StopAge / Mathf.Max(0.001f, seconds)), 0f, 1f) : 1f;

    /// <summary>The position followed, when there is one this frame.</summary>
    protected bool TryAnchor(out Vector3 position)
    {
        if (_follows)
        {
            return _anchor.TryResolve(out position);
        }

        position = default;
        return false;
    }

    private void Finish()
    {
        if (!Live)
        {
            return;
        }

        Live = false;
        _orientLike = null;
        OnFinish();
        SpellVfxDirector? director = Director;
        Director = null;
        director?.Retire(this);
        if (Reclaim != null)
        {
            Reclaim(this);
        }
        else
        {
            QueueFree();
        }
    }
}

/// <summary>
/// A reference to one life of a pooled effect. The node behind it is reused, so the handle carries
/// the serial it was issued for: once the effect has ended (or been recycled by the budget) every
/// call here is a no-op, however long the handle is kept.
/// </summary>
internal readonly struct VfxHandle<T>
    where T : VfxEffect
{
    private readonly T? _effect;
    private readonly int _serial;

    public VfxHandle(T? effect)
    {
        _effect = effect;
        _serial = effect?.Serial ?? 0;
    }

    private VfxHandle(T? effect, int serial)
    {
        _effect = effect;
        _serial = serial;
    }

    /// <summary>The same life of the same effect, as a handle that forgets which block it is.</summary>
    public VfxHandle<VfxEffect> Untyped() => new VfxHandle<VfxEffect>(_effect, _serial);

    /// <summary>The effect, while this life of it is still running; else null.</summary>
    public T? Get =>
        _effect != null && GodotObject.IsInstanceValid(_effect) && _effect.Live && _effect.Serial == _serial
            ? _effect
            : null;

    public bool IsLive => Get != null;

    public void Stop() => Get?.Stop();

    public void Kill() => Get?.Kill();
}

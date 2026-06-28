using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The arena entry trigger (Phase 28A): an interactable "challenge" object (a brazier) that, on the
/// player's <c>E</c>, summons the Iron King <b>once</b> and registers it as the active
/// <see cref="BossEntity"/> in the <see cref="ServiceLocator"/> (the hook the 28C healthbar and 28D
/// corruption loop resolve). Mirrors <see cref="World.RegionTransitionComponent"/>: a trigger that only
/// kicks off intent. While a boss is alive it stays inert; once he dies it re-arms (the cell can be
/// re-challenged until 28D persists his defeat). This node is the seed for the Phase 36 BossController —
/// the intro lock (28C) and phase logic (28B) graft on here.
/// </summary>
[GlobalClass]
public partial class BossSummonComponent : InteractableComponent
{
    /// <summary>Where the boss appears, relative to this brazier (world axes) — out in the arena.</summary>
    [Export] public Vector3 SpawnOffset { get; set; } = new(0f, 0f, -12f);

    private BossEntity? _boss;

    public override string Prompt => Loc.T("boss.challenge_prompt");

    public override void Interact(IEntity instigator)
    {
        if (_boss != null && IsInstanceValid(_boss))
        {
            return; // already fighting him
        }

        if (Entity?.Body is not { } brazier || brazier.GetParent() is not Node arena)
        {
            Log.Warn("BossSummonComponent: no arena parent to spawn the Iron King into.");
            return;
        }

        BossEntity boss = BossFactory.Create(Vector3.Zero);
        arena.AddChild(boss);
        boss.GlobalPosition = brazier.GlobalPosition + SpawnOffset;

        _boss = boss;
        ServiceLocator.Instance?.Register(boss);
        boss.TreeExited += OnBossGone;
        Log.Info("The Iron King rises to meet your challenge.");
    }

    private void OnBossGone()
    {
        _boss = null;
        ServiceLocator.Instance?.Unregister<BossEntity>();
    }
}

using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Core.Services;
using Embervale.Enemies;
using Embervale.Entities;
using Embervale.Player;
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
/// </summary>
public sealed partial class CombatShots : ShotHarness
{
    private const float Range = 6f;

    private EnemyEntity? _subject;

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

        return null;
    }

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

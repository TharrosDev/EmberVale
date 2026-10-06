using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;
using S = Embervale.Settings.Settings;

namespace Embervale.UI;

/// <summary>The Gameplay tab of <see cref="SettingsPanel"/>: difficulty, camera and combat comfort, tutorials.</summary>
public partial class SettingsPanel
{
    /// <summary>
    /// Whether the difficulty setting reaches the game. It does once incoming damage is scaled by
    /// <see cref="SettingsService.IncomingDamageScale"/> where a blow is resolved
    /// (<c>CombatComponent.ReceiveDamage</c>); until that call exists the row is not shown, because
    /// a difficulty dial that changes nothing is a lie the player finds out about slowly.
    /// </summary>
    public static readonly bool DifficultyWired = true;

    /// <summary>Runs a reset without it reaching an option this screen does not show: a reset
    /// must not change what the player cannot see.</summary>
    private void KeepingHidden(System.Action reset)
    {
        int difficulty = _settings.Current.Difficulty;
        reset();
        if (!DifficultyWired)
        {
            _settings.Current.Difficulty = difficulty;
        }
    }

    private void BuildGameplay(VBoxContainer body)
    {
        var s = _settings.Current;

        if (DifficultyWired)
        {
            Section(body, Loc.T("settings.section.challenge"), first: true);
            body.AddChild(DropdownRow(
                Info(Loc.T("settings.difficulty"), Loc.T("settings.difficulty.desc"), nameof(S.Difficulty)),
                new[] { Loc.T("settings.difficulty.story"), Loc.T("settings.difficulty.normal"), Loc.T("settings.difficulty.hard") },
                System.Math.Clamp(s.Difficulty, DifficultyRules.Story, DifficultyRules.Hard),
                i => { s.Difficulty = i; Persist(); }));
        }

        Section(body, Loc.T("settings.section.camera"), first: !DifficultyWired);
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.third_person"), Loc.T("settings.third_person.desc"), nameof(S.ThirdPersonCamera)),
            s.ThirdPersonCamera, v => { s.ThirdPersonCamera = v; Persist(); }));

        // Both apply live, so dragging the distance or flipping the shoulder moves the camera while
        // the panel is open — which is the only way to actually judge either.
        body.AddChild(SliderRow(
            Info(Loc.T("settings.tp_distance"), Loc.T("settings.tp_distance.desc"), nameof(S.ThirdPersonDistance)),
            2.0, 6.0, 0.1, s.ThirdPersonDistance, v => s.ThirdPersonDistance = v, Decimal));
        body.AddChild(DropdownRow(
            Info(Loc.T("settings.tp_shoulder"), Loc.T("settings.tp_shoulder.desc"), nameof(S.ThirdPersonShoulderSide)),
            new[]
            {
                Loc.T("settings.tp_shoulder.right"),
                Loc.T("settings.tp_shoulder.left"),
                Loc.T("settings.tp_shoulder.centre"),
            },
            s.ThirdPersonShoulderSide, i => { s.ThirdPersonShoulderSide = i; Persist(); }));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.cam_auto_shoulder"), Loc.T("settings.cam_auto_shoulder.desc"), nameof(S.AutoShoulderSwap)),
            s.AutoShoulderSwap, v => { s.AutoShoulderSwap = v; Persist(); }));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.cam_lock_framing"), Loc.T("settings.cam_lock_framing.desc"), nameof(S.LockOnFraming)),
            s.LockOnFraming, v => { s.LockOnFraming = v; Persist(); }));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.cam_obstruction_fade"), Loc.T("settings.cam_obstruction_fade.desc"), nameof(S.ObstructionFade)),
            s.ObstructionFade, v => { s.ObstructionFade = v; Persist(); }));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.cam_shake"), Loc.T("settings.cam_shake.desc"), nameof(S.CameraShakeIntensity)),
            0.0, 1.0, 0.05, s.CameraShakeIntensity, v => s.CameraShakeIntensity = v, Percent));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.cam_bob"), Loc.T("settings.cam_bob.desc"), nameof(S.HeadBob)),
            0.0, 1.0, 0.05, s.HeadBob, v => s.HeadBob = v, Percent));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.cam_fov_kick"), Loc.T("settings.cam_fov_kick.desc"), nameof(S.FovKick)),
            0.0, 1.0, 0.05, s.FovKick, v => s.FovKick = v, Percent));

        Section(body, Loc.T("settings.section.combat"));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.combat_hit_stop"), Loc.T("settings.combat_hit_stop.desc"), nameof(S.HitStopIntensity)),
            0.0, 1.0, 0.05, s.HitStopIntensity, v => s.HitStopIntensity = v, Percent));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.combat_flash"), Loc.T("settings.combat_flash.desc"), nameof(S.CombatFlashIntensity)),
            0.0, 1.0, 0.05, s.CombatFlashIntensity, v => s.CombatFlashIntensity = v, Percent));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.combat_lock_assist"), Loc.T("settings.combat_lock_assist.desc"), nameof(S.LockOnAssist)),
            s.LockOnAssist, v => { s.LockOnAssist = v; Persist(); }));
        body.AddChild(SliderRow(
            Info(Loc.T("settings.combat_aim_assist"), Loc.T("settings.combat_aim_assist.desc"), nameof(S.AimAssistStrength)),
            0.0, 1.0, 0.05, s.AimAssistStrength, v => s.AimAssistStrength = v, Percent));

        Section(body, Loc.T("settings.section.guidance"));
        body.AddChild(ToggleRow(
            Info(Loc.T("settings.show_tutorials"), Loc.T("settings.show_tutorials.desc"), nameof(S.ShowTutorials)),
            s.ShowTutorials, v =>
            {
                s.ShowTutorials = v;
                Persist();

                // Applies live: switching tutorials off mid-game clears the hint on screen rather than
                // waiting for a restart to take effect.
                if (!v && ServiceLocator.Instance is { } locator &&
                    locator.TryGet(out Onboarding.TutorialDirector tutorial))
                {
                    tutorial.Skip();
                }
            }));
    }
}

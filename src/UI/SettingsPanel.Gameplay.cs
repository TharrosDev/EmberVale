using Embervale.Core;
using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Embervale.Localization;
using Embervale.Settings;
using Godot;

namespace Embervale.UI;

/// <summary>The Gameplay section of <see cref="SettingsPanel"/>: difficulty, camera and combat comfort, tutorials.</summary>
public partial class SettingsPanel
{
    private void BuildGameplay(VBoxContainer body)
    {
        var s = _settings.Current;

        Section(body, Loc.T("settings.section.gameplay"));
        body.AddChild(DropdownRow(Loc.T("settings.difficulty"),
            new[] { Loc.T("settings.difficulty.story"), Loc.T("settings.difficulty.normal"), Loc.T("settings.difficulty.hard") },
            s.Difficulty, i => { s.Difficulty = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.third_person"), s.ThirdPersonCamera,
            v => { s.ThirdPersonCamera = v; Persist(); }));

        // Both apply live, so dragging the distance or flipping the shoulder moves the camera while
        // the panel is open — which is the only way to actually judge either.
        body.AddChild(SliderRow(Loc.T("settings.tp_distance"), 2.0, 6.0, 0.1, s.ThirdPersonDistance,
            v => s.ThirdPersonDistance = (float)v));
        body.AddChild(DropdownRow(Loc.T("settings.tp_shoulder"),
            new[]
            {
                Loc.T("settings.tp_shoulder.right"),
                Loc.T("settings.tp_shoulder.left"),
                Loc.T("settings.tp_shoulder.centre"),
            },
            s.ThirdPersonShoulderSide, i => { s.ThirdPersonShoulderSide = i; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.cam_auto_shoulder"), s.AutoShoulderSwap,
            v => { s.AutoShoulderSwap = v; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.cam_lock_framing"), s.LockOnFraming,
            v => { s.LockOnFraming = v; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.cam_obstruction_fade"), s.ObstructionFade,
            v => { s.ObstructionFade = v; Persist(); }));
        body.AddChild(SliderRow(Loc.T("settings.cam_shake"), 0.0, 1.0, 0.05, s.CameraShakeIntensity,
            v => s.CameraShakeIntensity = (float)v));
        body.AddChild(SliderRow(Loc.T("settings.cam_bob"), 0.0, 1.0, 0.05, s.HeadBob,
            v => s.HeadBob = (float)v));
        body.AddChild(SliderRow(Loc.T("settings.cam_fov_kick"), 0.0, 1.0, 0.05, s.FovKick,
            v => s.FovKick = (float)v));
        body.AddChild(SliderRow(Loc.T("settings.combat_hit_stop"), 0.0, 1.0, 0.05, s.HitStopIntensity,
            v => s.HitStopIntensity = (float)v));
        body.AddChild(SliderRow(Loc.T("settings.combat_flash"), 0.0, 1.0, 0.05, s.CombatFlashIntensity,
            v => s.CombatFlashIntensity = (float)v));
        body.AddChild(ToggleRow(Loc.T("settings.combat_numbers"), s.DamageNumbers,
            v => { s.DamageNumbers = v; Persist(); }));
        body.AddChild(ToggleRow(Loc.T("settings.combat_lock_assist"), s.LockOnAssist,
            v => { s.LockOnAssist = v; Persist(); }));
        body.AddChild(SliderRow(Loc.T("settings.combat_aim_assist"), 0.0, 1.0, 0.05, s.AimAssistStrength,
            v => s.AimAssistStrength = (float)v));
        body.AddChild(ToggleRow(Loc.T("settings.show_tutorials"), s.ShowTutorials, v =>
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

using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>Statuses and school effects going off on a bearer: a ward struck or broken, a stacked
/// status detonating, a freeze, a fed Kindle, a dispel.</summary>
public static partial class SpellVfx
{
    /// <summary>A status or school effect went off on <paramref name="target"/> at
    /// <paramref name="position"/> (its feet). <paramref name="radius"/> is how far it reached, 0 for
    /// one that touches only its bearer. <paramref name="spell"/> is the spell whose hit set it off,
    /// where there was one (a fed Kindle, a freeze, a dispel).</summary>
    public static void StatusProc(
        SpellProcKind kind, DamageType school, IEntity? target, Vector3 position, float radius,
        SpellResource? spell = null)
    {
        // Whose effect it is decides how bright it is drawn. With no caster to ask, a proc on
        // anyone but the player is treated as the player's doing (their status going off on a foe).
        Node3D? body = VfxAnchor.BodyOf(target);
        bool onPlayer = body != null && IsPlayer(target);
        Vector3 chest = position + Vector3.Up;
        if (!BeginSchool(school, null, chest, spell, out VfxCast opened))
        {
            return;
        }

        VfxCast cast = onPlayer
            ? opened
            : new VfxCast(
                opened.Spell, null, opened.School, true, VfxPalette.For(opened.School), opened.Recipe, opened.Authored,
                opened.Weight, opened.Fx);
        if (SpecialOf(spell)?.Proc is { } special && special(cast, kind, target, position, radius))
        {
            return;
        }

        VfxBudget budget = VfxQuality.Budget;
        switch (kind)
        {
            case SpellProcKind.WardHit:
            {
                // The ward shows itself where it took the blow, and holds.
                VfxShellSpec ripple = VfxShellSpec.Sphere(chest, 1f, cast.Colors);
                ripple.Fresnel = true;
                ripple.Life = 0.32f;
                ripple.StartScale = 0.9f;
                ripple.Opacity = 0.8f;
                VfxHandle<VfxShell> shell = cast.Fx.Shell(ripple);
                if (body != null)
                {
                    shell.Get?.Follow(VfxAnchor.To(body, Vector3.Up));
                }

                VfxFlareSpec glint = VfxFlareSpec.At(chest, 0.3f, cast.Colors);
                glint.Life = 0.18f;
                cast.Fx.Flare(glint);
                break;
            }

            case SpellProcKind.WardBreak:
            {
                // The shell bursts: a flash, a ring, and its pieces.
                var stage = new VfxStage
                {
                    Flare = true,
                    Ring = true,
                    Particles = VfxParticles.Shards,
                    Secondary = VfxParticles.Sparks,
                    ScreenFlash = onPlayer,
                };
                VfxPlan plan = cast.PlanOf(stage, 1.6f, onPlayer) with { Fireball = false };
                Blast(cast, plan, chest, 1.6f, Vector3.Zero, float.NaN, onPlayer, streak: true);
                VfxShellSpec gone = VfxShellSpec.Sphere(chest, 1.05f, cast.Colors);
                gone.Fresnel = true;
                gone.Life = 0.4f;
                gone.BurnsAway = true;
                gone.StartScale = 0.85f;
                cast.Fx.Shell(gone);
                break;
            }

            case SpellProcKind.Detonation:
            {
                // A stacked status going off: a blast in its own right, over the radius it reached.
                float reach = Mathf.Max(1.4f, radius);
                bool atEye = onPlayer && AtTheEye(chest + (Vector3.Up * 0.6f));
                if (school == DamageType.Fire && !atEye)
                {
                    // Kindle going off is a fireball in its own right.
                    FireBlast(cast, chest, reach * 1.15f, position.Y);
                    break;
                }

                if (school == DamageType.Lightning && !atEye)
                {
                    LightningBlast(cast, chest, reach, position.Y);
                    break;
                }

                var stage = new VfxStage
                {
                    Flare = true,
                    Ring = true,
                    Particles = SpellVfxCatalog.SchoolParticles(school),
                    Secondary = VfxRecipeRules.SchoolSecondary(school),
                    Distortion = true,
                    Mark = SpellVfxCatalog.SchoolMark(school),
                    ScreenFlash = reach >= VfxRecipeRules.ScreenFlashRadius,
                };
                bool hitsPlayer = onPlayer || PlayerWithin(chest, reach + 0.5f);
                Blast(cast, cast.PlanOf(stage, reach, hitsPlayer), chest, reach, Vector3.Zero, position.Y, hitsPlayer);
                break;
            }

            case SpellProcKind.Freeze:
            {
                // Ice closes over the bearer and holds for a moment; shards and cold fall off it.
                VfxSchoolColors ice = VfxPalette.For(DamageType.Frost);
                VfxShellSpec shell = VfxShellSpec.IceShell(chest, 1f, ice);
                shell.Size = new Vector3(1.5f, 2.1f, 1.5f);
                shell.Occlude = 0.35f;
                shell.Life = 0.9f;
                shell.BurnsAway = true;
                shell.StartScale = 0.7f;
                VfxHandle<VfxShell> frozen = cast.Fx.Shell(shell);
                if (body != null)
                {
                    frozen.Get?.Follow(VfxAnchor.To(body, Vector3.Up));
                }

                VfxFlareSpec snap = VfxFlareSpec.At(chest, 0.7f, ice);
                snap.Ring = true;
                snap.RingRadius = 1.3f;
                snap.Light = budget.MaxLights > 0;
                snap.LightRange = 4f;
                cast.Fx.Flare(snap);
                cast.Fx.Burst(VfxParticles.Shards, VfxBurstSpec.At(chest, ice, budget.ParticleMultiplier * 0.8f));
                ShardBurst(cast, chest, 1.2f, up: false);
                cast.Fx.Mark(new VfxGroundMarkSpec
                {
                    Mark = VfxMark.Frost,
                    Position = position,
                    Size = 2.4f,
                    Colors = ice,
                    Life = 5f,
                    Reach = 1f,
                });
                break;
            }

            case SpellProcKind.KindleFed:
            {
                // One more stack catching: a lick of flame up the bearer.
                VfxFlareSpec lick = VfxFlareSpec.At(chest, 0.34f, cast.Colors);
                lick.Life = 0.22f;
                cast.Fx.Flare(lick);
                VfxBurstSpec embers = VfxBurstSpec.At(chest, cast.Colors, budget.ParticleMultiplier * 0.5f);
                embers.Direction = Vector3.Up;
                embers.Spread = 40f;
                embers.Extents = new Vector3(0.25f, 0.4f, 0.25f);
                cast.Fx.Burst(VfxParticles.Embers, embers);
                break;
            }

            default:
            {
                // Dispel: the glyph of what was torn off, breaking apart.
                VfxDiscSpec glyph = VfxDiscSpec.At(chest, 0.7f, cast.Colors);
                glyph.Life = 0.5f;
                glyph.Rune = true;
                glyph.FaceCamera = true;
                glyph.Body = 0.1f;
                glyph.Rim = 0f;
                glyph.Spin = 4f;
                cast.Fx.Disc(glyph);
                VfxFlareSpec crack = VfxFlareSpec.At(chest, 0.4f, cast.Colors);
                crack.Life = 0.25f;
                crack.Ring = true;
                crack.RingRadius = 1.1f;
                cast.Fx.Flare(crack);
                cast.Fx.Burst(VfxParticles.Motes, VfxBurstSpec.At(chest, cast.Colors, budget.ParticleMultiplier * 0.7f));
                break;
            }
        }
    }
}

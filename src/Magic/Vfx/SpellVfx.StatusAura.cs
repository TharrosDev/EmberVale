using Embervale.Combat;
using Embervale.Entities;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// What a status looks like for as long as it is worn: burning embers, chill mist, a frozen shell,
/// stars about a stunned head, roots under the rooted, a glyph over the silenced, leaves on the
/// mending, wisps off the decaying.
///
/// <para><see cref="StatusEffectVfxComponent"/> owns when a status starts and ends and still builds
/// its plain marker under the bearer (the headless gates assert those nodes). This is the picture
/// drawn over it when the effect layer is live: the same blocks every spell uses, under
/// <c>VfxRoot</c>, following the bearer by copying its position.</para>
/// </summary>
public static partial class SpellVfx
{
    /// <summary>
    /// Starts the standing effect of <paramref name="statusId"/> on <paramref name="bearer"/> and
    /// returns the rig that <see cref="VfxRig.Stop"/> ends it through, or null when nothing was drawn
    /// (no effect layer, too far away, or the camera is inside the bearer's head).
    /// <paramref name="marker"/> is the plain node the component built, whose children the stun's
    /// stars ride; <paramref name="replaces"/> says the plain marker should now be hidden because
    /// this picture is the same thing drawn better (a swirl, a shell), not an addition to it.
    /// </summary>
    internal static VfxRig? StatusAura(
        IEntity bearer, string statusId, StatusVfxShape shape, DamageType school, float swirlHeight, float headHeight,
        Node3D? marker, out bool replaces)
    {
        replaces = false;
        if (!Active || VfxAnchor.BodyOf(bearer) is not { } body || StatusAuraHidden(bearer))
        {
            return null;
        }

        // Essential, so the budget never takes a status off a body that still has it; measured, so
        // one too far away to read is left to its plain marker.
        VfxSpawner fx = _director!.Open(
            body.GlobalPosition + Vector3.Up, IsPlayer(bearer), essential: true, sustained: true, measured: true);
        if (!fx.Full)
        {
            return null;
        }

        var rig = new VfxRig();
        VfxSchoolColors colors = VfxPalette.For(school);
        var chest = new Vector3(0f, swirlHeight, 0f);
        var head = new Vector3(0f, headHeight, 0f);
        switch (shape)
        {
            case StatusVfxShape.MarkRing:
            {
                // The ring's own light, and a sigil turning inside it where the tier has room.
                StatusGlow(rig, fx, body, head, 0.2f, 0.5f, colors.Scaled(0.5f, 0.6f));
                if (ArcanaLavish)
                {
                    StatusSigil(rig, fx, body, head, 0.3f, 0.8f, colors);
                }

                break;
            }

            case StatusVfxShape.Thorns:
            {
                if (StatusMarks)
                {
                    rig.Add(fx.Mark(new VfxGroundMarkSpec
                    {
                        Mark = VfxMark.Roots,
                        Position = body.GlobalPosition,
                        Size = 1.9f,
                        Colors = colors,
                        Sustain = true,
                        Reach = 1f,
                    })).Get?.Follow(VfxAnchor.To(body));
                }

                VfxBurstSpec leaves = StatusStream(body, new Vector3(0f, 0.3f, 0f), colors, ArcanaAmount(0.25f));
                leaves.Extents = new Vector3(0.45f, 0.1f, 0.45f);
                leaves.SpeedScale = 0.3f;
                StatusEmit(rig, fx, body, new Vector3(0f, 0.3f, 0f), VfxParticles.Leaves, leaves);
                break;
            }

            case StatusVfxShape.BrokenGlyph:
            {
                // The glyph that was taken, turning backwards, and what is left of it sifting down.
                StatusSigil(rig, fx, body, head, 0.36f, -0.6f, colors);
                var under = new Vector3(0f, headHeight - 0.2f, 0f);
                VfxBurstSpec dust = StatusStream(body, under, colors, ArcanaAmount(0.3f));
                dust.Extents = new Vector3(0.25f, 0.08f, 0.25f);
                dust.SpeedScale = 0.3f;
                dust.GravityScale = -1.6f;
                StatusEmit(rig, fx, body, under, VfxParticles.Motes, dust);
                break;
            }

            case StatusVfxShape.Stars:
            {
                // Each circling star gets a glint that rides it; the lean tiers get one for all three.
                int riders = 0;
                if (ArcanaRich && marker != null)
                {
                    foreach (Node child in marker.GetChildren())
                    {
                        if (child is Node3D star && riders < 3)
                        {
                            VfxFlareSpec glint = VfxFlareSpec.At(star.GlobalPosition, 0.11f, colors);
                            glint.Sustain = true;
                            glint.Level = 0.8f;
                            rig.Add(fx.Flare(glint)).Get?.Follow(VfxAnchor.To(star));
                            riders++;
                        }
                    }
                }

                if (riders == 0)
                {
                    StatusGlow(rig, fx, body, head, 0.16f, 0.6f, colors.Scaled(0.6f, 0.7f));
                }

                VfxBurstSpec sparks = StatusStream(body, head, colors, ArcanaDebris(0.12f));
                sparks.Extents = new Vector3(0.3f, 0.05f, 0.3f);
                sparks.SpeedScale = 0.25f;
                StatusEmit(rig, fx, body, head, VfxParticles.Sparks, sparks);
                break;
            }

            case StatusVfxShape.IceShell:
            {
                // A block of ice: rim-lit, half covering what is inside it, with cold falling off it.
                var middle = new Vector3(0f, 1f, 0f);
                VfxShellSpec ice = VfxShellSpec.Sphere(body.GlobalPosition + middle, 1f, colors);
                ice.Size = new Vector3(1.35f, 2.1f, 1.35f);
                ice.Sustain = true;
                ice.Fresnel = true;
                ice.Occlude = 0.35f;
                ice.Energy = 0.8f;
                VfxHandle<VfxShell> shell = rig.Add(fx.Shell(ice));
                shell.Get?.Follow(VfxAnchor.To(body, middle));
                replaces = shell.IsLive;

                var low = new Vector3(0f, 0.35f, 0f);
                VfxBurstSpec mist = StatusStream(body, low, colors, ArcanaAmount(0.45f));
                mist.Extents = new Vector3(0.6f, 0.12f, 0.6f);
                mist.SpeedScale = 0.4f;
                mist.GravityScale = -1.2f;
                StatusEmit(rig, fx, body, low, VfxParticles.Motes, mist);
                if (StatusMarks)
                {
                    rig.Add(fx.Mark(new VfxGroundMarkSpec
                    {
                        Mark = VfxMark.Frost,
                        Position = body.GlobalPosition,
                        Size = 2.2f,
                        Colors = colors,
                        Sustain = true,
                        Reach = 1f,
                    })).Get?.Follow(VfxAnchor.To(body));
                }

                break;
            }

            case StatusVfxShape.WardShell:
            {
                var middle = new Vector3(0f, 1f, 0f);
                VfxShellSpec ward = VfxShellSpec.Sphere(body.GlobalPosition + middle, 1f, colors);
                ward.Sustain = true;
                ward.Fresnel = true;
                ward.Opacity = 0.55f;
                ward.Energy = 0.7f;
                VfxHandle<VfxShell> shell = rig.Add(fx.Shell(ward));
                shell.Get?.Follow(VfxAnchor.To(body, middle));
                replaces = shell.IsLive;

                VfxBurstSpec motes = StatusStream(body, middle, colors, ArcanaDebris(0.2f));
                motes.Extents = new Vector3(0.8f, 0.9f, 0.8f);
                motes.SpeedScale = 0.3f;
                StatusEmit(rig, fx, body, middle, VfxParticles.Motes, motes);
                break;
            }

            default:
                replaces = StatusSwirl(rig, fx, body, statusId, school, chest, colors);
                break;
        }

        return rig.AnyLive ? rig : null;
    }

    /// <summary>
    /// Whether a status must not be drawn on <paramref name="bearer"/> right now: the camera is
    /// inside its head (the player's own body in first person), where anything riding the body is a
    /// smear across the lens. True as well when nothing can be drawn at all.
    /// </summary>
    internal static bool StatusAuraHidden(IEntity? bearer) =>
        !Active || VfxAnchor.BodyOf(bearer) is not { } body || ArcanaInside(body);

    /// <summary>
    /// Whether a status leaves its own patch on the floor (roots under the rooted, frost under the
    /// frozen). Only where the mark budget is generous: marks are kept newest first, and on a tier
    /// with a handful of them a snare that roots four bodies would push its own roots, and every
    /// scorch in the fight, off the ground.
    /// </summary>
    private static bool StatusMarks => VfxQuality.Budget.GroundMarks >= StatusMarkBudget;

    private const int StatusMarkBudget = 12;

    /// <summary>The statuses that are a swirl of something: what it is, where on the body, and which
    /// way it drifts, by status and failing that by school. True when the stream was drawn.</summary>
    private static bool StatusSwirl(
        VfxRig rig, in VfxSpawner fx, Node3D body, string statusId, DamageType school, Vector3 chest,
        in VfxSchoolColors colors)
    {
        VfxBurstSpec stream = StatusStream(body, chest, colors, ArcanaAmount(0.4f));
        stream.Extents = new Vector3(0.3f, 0.5f, 0.3f);
        stream.SpeedScale = 0.5f;
        VfxParticles kind = SpellVfxCatalog.SchoolParticles(school);
        Vector3 at = chest;
        switch (statusId)
        {
            case StatusIds.Burning:
            {
                // Embers climbing off the bearer, a lick of light in the chest, smoke over the head.
                kind = VfxParticles.Embers;
                stream.Density = ArcanaAmount(0.55f);
                stream.Extents = new Vector3(0.28f, 0.45f, 0.28f);
                stream.SpeedScale = 0.6f;
                stream.SizeScale = 0.8f;
                var over = new Vector3(0f, chest.Y + 0.6f, 0f);
                VfxBurstSpec smoke = StatusStream(body, over, colors, ArcanaDebris(0.25f));
                smoke.Extents = new Vector3(0.2f, 0.15f, 0.2f);
                smoke.SizeScale = 0.55f;
                smoke.SpeedScale = 0.6f;
                StatusEmit(rig, fx, body, over, VfxParticles.Smoke, smoke);
                if (ArcanaLavish)
                {
                    StatusGlow(rig, fx, body, chest, 0.2f, 0.45f, colors.Scaled(0.5f, 0.7f));
                }

                break;
            }

            case StatusIds.Chill:
            {
                // Cold pooling about the legs and sinking; a finer frost about the chest.
                kind = VfxParticles.Motes;
                at = new Vector3(0f, 0.5f, 0f);
                stream.Density = ArcanaAmount(0.6f);
                stream.Extents = new Vector3(0.45f, 0.18f, 0.45f);
                stream.GravityScale = -1.5f;
                VfxBurstSpec frost = StatusStream(body, chest, colors, ArcanaDebris(0.3f));
                frost.Tint = new Color(0.92f, 0.97f, 1f);
                frost.Extents = new Vector3(0.3f, 0.4f, 0.3f);
                frost.SpeedScale = 0.3f;
                frost.SizeScale = 0.7f;
                frost.GravityScale = -1f;
                StatusEmit(rig, fx, body, chest, VfxParticles.Motes, frost);
                break;
            }

            case StatusIds.Regrowth:
            {
                // Leaves turning slowly upward, in a rising drift of green light.
                kind = VfxParticles.Leaves;
                stream.Density = ArcanaAmount(0.35f);
                stream.Extents = new Vector3(0.35f, 0.5f, 0.35f);
                stream.Direction = Vector3.Up;
                stream.Spread = 25f;
                stream.SpeedScale = 0.35f;
                stream.GravityScale = -0.15f;
                VfxBurstSpec light = StatusStream(body, chest, colors, ArcanaAmount(0.4f));
                light.Extents = new Vector3(0.35f, 0.6f, 0.35f);
                light.SpeedScale = 0.6f;
                StatusEmit(rig, fx, body, chest, VfxParticles.Motes, light);
                break;
            }

            case StatusIds.Decay:
            {
                // Small dark wisps sloughing off and falling, with a thread of smoke where there is room.
                kind = VfxParticles.Wisps;
                stream.Density = ArcanaAmount(0.3f);
                stream.SizeScale = 0.5f;
                stream.GravityScale = -3.5f;
                VfxBurstSpec smoke = StatusStream(body, chest, colors, ArcanaDebris(0.2f));
                smoke.Extents = new Vector3(0.25f, 0.4f, 0.25f);
                smoke.SizeScale = 0.5f;
                smoke.SpeedScale = 0.4f;
                smoke.GravityScale = -0.4f;
                StatusEmit(rig, fx, body, chest, VfxParticles.Smoke, smoke);
                break;
            }

            case StatusIds.Barkskin:
            {
                // Faint: a few chips of bark hanging about the body, and a little green in them.
                kind = VfxParticles.Leaves;
                stream.Tint = ArcanaBark;
                stream.Density = ArcanaAmount(0.25f);
                stream.Extents = new Vector3(0.4f, 0.6f, 0.4f);
                stream.SpeedScale = 0.25f;
                stream.SizeScale = 0.8f;
                stream.GravityScale = 0.2f;
                VfxBurstSpec green = StatusStream(body, chest, colors, ArcanaAmount(0.25f));
                green.Extents = new Vector3(0.4f, 0.6f, 0.4f);
                green.SpeedScale = 0.4f;
                StatusEmit(rig, fx, body, chest, VfxParticles.Motes, green);
                break;
            }

            case StatusIds.Swarmed:
            {
                // The swarm itself: many fast specks that never settle.
                kind = VfxParticles.Motes;
                stream.Tint = ArcanaSting;
                stream.Density = ArcanaAmount(1.2f);
                stream.Extents = new Vector3(0.4f, 0.55f, 0.4f);
                stream.SpeedScale = 1.8f;
                stream.LifeScale = 0.4f;
                stream.GravityScale = 0f;
                break;
            }

            case StatusIds.SoulEcho:
            {
                kind = VfxParticles.Wisps;
                stream.Density = ArcanaAmount(0.2f);
                stream.SizeScale = 0.45f;
                break;
            }
        }

        stream.Position = body.GlobalPosition + at;
        return StatusEmit(rig, fx, body, at, kind, stream);
    }

    /// <summary>A steady stream at <paramref name="offset"/> on the bearer, to be shaped by the caller.</summary>
    private static VfxBurstSpec StatusStream(Node3D body, Vector3 offset, in VfxSchoolColors colors, float density)
    {
        VfxBurstSpec stream = VfxBurstSpec.At(body.GlobalPosition + offset, colors, density);
        stream.Continuous = true;
        return stream;
    }

    private static bool StatusEmit(
        VfxRig rig, in VfxSpawner fx, Node3D body, Vector3 offset, VfxParticles kind, in VfxBurstSpec spec)
    {
        VfxHandle<VfxBurst> stream = rig.Add(fx.Burst(kind, spec));
        stream.Get?.Follow(VfxAnchor.To(body, offset));
        return stream.IsLive;
    }

    /// <summary>A small steady glow with no light of its own (a status may last, and a light held
    /// that long would take one of the few the lean tiers have away from the spells).</summary>
    private static void StatusGlow(
        VfxRig rig, in VfxSpawner fx, Node3D body, Vector3 offset, float radius, float level, in VfxSchoolColors colors)
    {
        VfxFlareSpec glow = VfxFlareSpec.At(body.GlobalPosition + offset, radius, colors);
        glow.Sustain = true;
        glow.Level = level;
        rig.Add(fx.Flare(glow)).Get?.Follow(VfxAnchor.To(body, offset));
    }

    private static void StatusSigil(
        VfxRig rig, in VfxSpawner fx, Node3D body, Vector3 offset, float radius, float spin, in VfxSchoolColors colors)
    {
        VfxDiscSpec sigil = VfxDiscSpec.At(body.GlobalPosition + offset, radius, colors);
        sigil.Sustain = true;
        sigil.Rune = true;
        sigil.FaceCamera = true;
        sigil.Spin = spin;
        sigil.Body = 0.08f;
        sigil.Rim = 0f;
        rig.Add(fx.Disc(sigil)).Get?.Follow(VfxAnchor.To(body, offset));
    }
}

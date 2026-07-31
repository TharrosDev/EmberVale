using Embervale.Magic;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Cone containment (Phase 35C) — the geometry that decides whether standing behind a dragon saves
/// you. Everything around it (the physics query, the channel, the flash) is Godot-bound and verified
/// by build/run; this is the part that is pure and load-bearing.
/// </summary>
public class SpellConeTests
{
    private static readonly Vector3 Apex = Vector3.Zero;

    /// <summary>-Z is forward, matching the body basis every actor's aim is read from.</summary>
    private static readonly Vector3 Forward = new(0f, 0f, -1f);

    private const float Angle = 60f;   // ±30° off-axis
    private const float Length = 10f;

    private static bool Hit(Vector3 point) => SpellCone.Contains(Apex, Forward, Angle, Length, point);

    [Fact]
    public void DeadAhead_IsInside()
    {
        Assert.True(Hit(new Vector3(0f, 0f, -5f)));
    }

    [Fact]
    public void DirectlyBehind_IsOutside()
    {
        // The whole point of a cone over a nova: getting round the back is a defence.
        Assert.False(Hit(new Vector3(0f, 0f, 5f)));
    }

    [Fact]
    public void BesideTheApex_IsOutside()
    {
        Assert.False(Hit(new Vector3(5f, 0f, 0f)));
        Assert.False(Hit(new Vector3(-5f, 0f, 0f)));
    }

    [Fact]
    public void BeyondTheLength_IsOutside()
    {
        Assert.True(Hit(new Vector3(0f, 0f, -Length)));
        Assert.False(Hit(new Vector3(0f, 0f, -(Length + 0.1f))));
    }

    [Fact]
    public void TheAuthoredAngleIsFullWidth_NotHalf()
    {
        // 60° means ±30°, so a target 25° off-axis burns and one 35° off-axis does not. Reading this
        // as a half-angle would silently double every cone in the game.
        float z = -5f;
        Assert.True(Hit(new Vector3(Mathf.Tan(Mathf.DegToRad(25f)) * 5f, 0f, z)));
        Assert.False(Hit(new Vector3(Mathf.Tan(Mathf.DegToRad(35f)) * 5f, 0f, z)));
    }

    [Fact]
    public void TheBoundaryIsInclusive()
    {
        Assert.True(Hit(new Vector3(Mathf.Tan(Mathf.DegToRad(Angle * 0.5f)) * 5f, 0f, -5f)));
    }

    [Fact]
    public void ItIsACone_NotAWedge()
    {
        // Pitch counts too — which is what lets a hovering dragon breathe straight down.
        Assert.True(SpellCone.Contains(Apex, Vector3.Down, Angle, Length, new Vector3(0f, -5f, 0f)));
        Assert.False(SpellCone.Contains(Apex, Vector3.Down, Angle, Length, new Vector3(0f, 5f, 0f)));
        Assert.True(Hit(new Vector3(0f, 1f, -5f)));    // slightly above the axis, still inside
        Assert.False(Hit(new Vector3(0f, 8f, -5f)));   // far above it, outside
    }

    [Fact]
    public void TheApexItselfIsInside()
    {
        // Standing inside the dragon's mouth is not an escape.
        Assert.True(Hit(Apex));
    }

    [Fact]
    public void DegenerateTuningHitsNothing()
    {
        Vector3 point = new(0f, 0f, -1f);
        Assert.False(SpellCone.Contains(Apex, Forward, 0f, Length, point));
        Assert.False(SpellCone.Contains(Apex, Forward, Angle, 0f, point));
        Assert.False(SpellCone.Contains(Apex, Vector3.Zero, Angle, Length, point));
    }
}

using System.Collections.Generic;
using Embervale.Magic;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>The spell wheel's tap-or-hold decision, its geometry and the button's state machine.</summary>
public class SpellWheelRulesTests
{
    private static readonly SpellWheelLayout Layout = new(
        new[] { "a", "b", "", "", "", "", "", "h" },
        new IReadOnlyList<string>[]
        {
            new[] { "fire1", "fire2" },
            new[] { "frost1" },
            new string[0],
            new[] { "arc1", "arc2", "arc3", "arc4", "arc5" },
            new string[0],
            new string[0],
        });

    /// <summary>A cursor at <paramref name="degrees"/> clockwise from the top.</summary>
    private static Vector2 At(float degrees, float radius)
    {
        float radians = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * radius;
    }

    [Fact]
    public void AShortStillPress_IsATap_AndAnythingElseIsTheWheel()
    {
        Assert.Equal(SpellWheelGesture.Tap, SpellWheelRules.Classify(0f, 0f));
        Assert.Equal(SpellWheelGesture.Tap, SpellWheelRules.Classify(0.15f, 0.1f));
        Assert.Equal(SpellWheelGesture.Wheel, SpellWheelRules.Classify(SpellWheelRules.TapSeconds, 0f));
        Assert.Equal(SpellWheelGesture.Wheel, SpellWheelRules.Classify(0.05f, SpellWheelRules.DeadZone));
    }

    [Fact]
    public void TheDeadZone_PicksNothing()
    {
        Assert.Equal(SpellWheelPick.None, SpellWheelRules.Pick(Vector2.Zero, Layout, -1));
        Assert.Equal(SpellWheelPick.None, SpellWheelRules.Pick(At(90f, 0.17f), Layout, -1));
        Assert.False(SpellWheelPick.None.Selects);
    }

    [Theory]
    [InlineData(0f, 0, "a")]
    [InlineData(22f, 0, "a")]
    [InlineData(23f, 1, "b")]
    [InlineData(90f, 2, "")]
    [InlineData(338f, 0, "a")]
    [InlineData(315f, 7, "h")]
    public void TheInnerRing_IsEightFavouritesCentredFromTheTop(float degrees, int slot, string id)
    {
        SpellWheelPick pick = SpellWheelRules.Pick(At(degrees, 0.4f), Layout, -1);
        Assert.Equal(SpellWheelPickKind.Favourite, pick.Kind);
        Assert.Equal(slot, pick.Index);
        Assert.Equal(id, pick.SpellId);
        Assert.Equal(id.Length > 0, pick.Selects);
        Assert.Equal(-1, SpellWheelRules.Latch(pick));
    }

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(29f, 0)]
    [InlineData(31f, 1)]
    [InlineData(180f, 3)]
    [InlineData(331f, 0)]
    [InlineData(300f, 5)]
    public void TheOuterRing_IsSixSchools_AndLatchesTheHoveredOne(float degrees, int school)
    {
        SpellWheelPick pick = SpellWheelRules.Pick(At(degrees, 0.8f), Layout, -1);
        Assert.Equal(SpellWheelPickKind.School, pick.Kind);
        Assert.Equal(school, pick.Index);
        Assert.False(pick.Selects);
        Assert.Equal(school, SpellWheelRules.Latch(pick));
    }

    [Fact]
    public void TheFan_SplitsTheLatchedSchoolsSpells()
    {
        // Fire sits at the top with two spells: its fan is its own 60-degree wedge, halved.
        SpellWheelPick left = SpellWheelRules.Pick(At(345f, 1.2f), Layout, 0);
        SpellWheelPick right = SpellWheelRules.Pick(At(15f, 1.2f), Layout, 0);
        Assert.Equal(new SpellWheelPick(SpellWheelPickKind.Spell, 0, "fire1", 0), left);
        Assert.Equal(new SpellWheelPick(SpellWheelPickKind.Spell, 1, "fire2", 0), right);
        Assert.True(right.Selects);
        Assert.Equal(0, SpellWheelRules.Latch(right));
    }

    [Fact]
    public void ACrowdedFan_WidensSoEverySpellGetsItsMinimum()
    {
        // Five arcane spells need 100 degrees, wider than the school's 60. Arcane is centred at 180.
        Assert.Equal(100f, SpellWheelRules.FanSpanDegrees(5));
        Assert.Equal(60f, SpellWheelRules.FanSpanDegrees(1));
        Assert.Equal("arc1", SpellWheelRules.Pick(At(131f, 1.2f), Layout, 3).SpellId);
        Assert.Equal("arc3", SpellWheelRules.Pick(At(180f, 1.2f), Layout, 3).SpellId);
        Assert.Equal("arc5", SpellWheelRules.Pick(At(229f, 1.2f), Layout, 3).SpellId);
    }

    [Fact]
    public void PastTheRim_OutsideTheFan_TheSchoolAtThatAngleTakesOver()
    {
        // Latched on fire, cursor swung round to frost's angle: frost, not a fire spell.
        SpellWheelPick pick = SpellWheelRules.Pick(At(60f, 1.2f), Layout, 0);
        Assert.Equal(SpellWheelPickKind.School, pick.Kind);
        Assert.Equal(1, pick.Index);

        // No latch at all, or a school with no spells: the school under the cursor.
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, 1.2f), Layout, -1).Kind);
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(120f, 1.2f), Layout, 2).Kind);
    }

    [Fact]
    public void TheCursor_StepsByTheMouseAndStopsAtTheFansEdge()
    {
        Vector2 stepped = SpellWheelRules.StepCursor(Vector2.Zero, new Vector2(75f, 0f), SpellWheelRules.MouseScale);
        Assert.Equal(0.5f, stepped.X, 3);

        Vector2 clamped = SpellWheelRules.StepCursor(stepped, new Vector2(900f, 900f), SpellWheelRules.MouseScale);
        Assert.Equal(SpellWheelRules.FanEdge, clamped.Length(), 3);
    }

    [Fact]
    public void TheStick_RestsInTheDeadZone_ThenFavourites_ThenSchoolsAndFan()
    {
        Assert.Equal(Vector2.Zero, SpellWheelRules.FromStick(new Vector2(0.2f, 0f)));

        float inner = SpellWheelRules.FromStick(new Vector2(0f, -0.5f)).Length();
        Assert.InRange(inner, SpellWheelRules.DeadZone, SpellWheelRules.InnerEdge);

        float ring = SpellWheelRules.FromStick(new Vector2(0.8f, 0f)).Length();
        Assert.InRange(ring, SpellWheelRules.InnerEdge, SpellWheelRules.OuterEdge);

        Vector2 full = SpellWheelRules.FromStick(new Vector2(0f, 1f));
        Assert.Equal(SpellWheelRules.FanEdge, full.Length(), 3);
        Assert.Equal(180f, SpellWheelRules.AngleDegrees(full), 2);
    }

    // --- The button ----------------------------------------------------------

    private const float Frame = 1f / 60f;

    private static SpellWheelIntent Press(ref SpellWheelHold hold, bool toggle = false) =>
        hold.Step(pressed: true, held: true, released: false, Frame, 0f, toggle, confirm: false, cancel: false);

    private static SpellWheelIntent Hold(ref SpellWheelHold hold, float seconds = Frame, bool cancel = false) =>
        hold.Step(pressed: false, held: true, released: false, seconds, 0f, toggle: false, confirm: false, cancel);

    private static SpellWheelIntent Release(ref SpellWheelHold hold) =>
        hold.Step(pressed: false, held: false, released: true, Frame, 0f, toggle: false, confirm: false, cancel: false);

    [Fact]
    public void ATap_IsThePreviousSpell()
    {
        var hold = default(SpellWheelHold);
        Assert.Equal(SpellWheelIntent.None, Press(ref hold));
        Assert.True(hold.IsPending);
        Assert.Equal(SpellWheelIntent.None, Hold(ref hold));
        Assert.Equal(SpellWheelIntent.Previous, Release(ref hold));
        Assert.False(hold.IsPending);
    }

    [Fact]
    public void AHold_OpensTheWheel_AndLettingGoConfirms()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold);
        Assert.Equal(SpellWheelIntent.Open, Hold(ref hold, SpellWheelRules.TapSeconds));
        Assert.True(hold.IsOpen);
        Assert.False(hold.IsToggled);
        Assert.Equal(SpellWheelIntent.None, Hold(ref hold));
        Assert.Equal(SpellWheelIntent.Confirm, Release(ref hold));
        Assert.False(hold.IsOpen);
    }

    [Fact]
    public void ACancelledHold_DoesNothingWhenItIsLetGo()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold);
        Hold(ref hold, SpellWheelRules.TapSeconds);
        Assert.Equal(SpellWheelIntent.Cancel, Hold(ref hold, cancel: true));
        Assert.False(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.None, Hold(ref hold));
        Assert.Equal(SpellWheelIntent.None, Release(ref hold));
        Assert.Equal(SpellWheelIntent.None, Press(ref hold)); // and the next press starts clean
        Assert.True(hold.IsPending);
    }

    [Fact]
    public void ARefusedHold_FallsBackWhenItIsLetGo()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold);
        Assert.Equal(SpellWheelIntent.Open, Hold(ref hold, SpellWheelRules.TapSeconds));
        Assert.Equal(SpellWheelIntent.None, hold.Refuse());
        Assert.False(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.None, Hold(ref hold));
        Assert.Equal(SpellWheelIntent.Fallback, Release(ref hold));
    }

    [Fact]
    public void AWheelClosedFromOutside_SwallowsTheRestOfTheHold()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold);
        Hold(ref hold, SpellWheelRules.TapSeconds);
        hold.Closed(held: true);
        Assert.False(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.None, Release(ref hold));
    }

    [Fact]
    public void Toggled_ThePressOpens_AndASecondPressOrTheConfirmInputSelects()
    {
        var hold = default(SpellWheelHold);
        Assert.Equal(SpellWheelIntent.Open, Press(ref hold, toggle: true));
        Assert.True(hold.IsToggled);

        // Letting go of the button does not close a toggled wheel.
        Assert.Equal(
            SpellWheelIntent.None,
            hold.Step(false, false, true, Frame, 0f, toggle: true, confirm: false, cancel: false));
        Assert.True(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.Confirm, Press(ref hold, toggle: true));

        Press(ref hold, toggle: true);
        Assert.Equal(
            SpellWheelIntent.Confirm,
            hold.Step(false, false, false, Frame, 0f, toggle: true, confirm: true, cancel: false));

        Press(ref hold, toggle: true);
        Assert.Equal(
            SpellWheelIntent.Cancel,
            hold.Step(false, false, false, Frame, 0f, toggle: true, confirm: false, cancel: true));
        Assert.False(hold.IsOpen);
    }

    [Fact]
    public void Toggled_ARefusedPressFallsBackAtOnce()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold, toggle: true);
        Assert.Equal(SpellWheelIntent.Fallback, hold.Refuse());
        Assert.False(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.None, hold.Refuse()); // nothing left to refuse
    }

    [Fact]
    public void Reset_ForgetsAnOpenWheel()
    {
        var hold = default(SpellWheelHold);
        Press(ref hold);
        Hold(ref hold, SpellWheelRules.TapSeconds);
        hold.Reset();
        Assert.False(hold.IsOpen);
        Assert.Equal(SpellWheelIntent.None, Release(ref hold));
    }
}

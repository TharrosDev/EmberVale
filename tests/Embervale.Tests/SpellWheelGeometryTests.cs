using System.Collections.Generic;
using Embervale.Combat;
using Embervale.Magic;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The spell wheel's geometry at its edges and over its whole face: the ring boundaries, the fan's
/// latch as a cursor really moves through it, the minimum a spell gets in a crowded fan, and the
/// cursor and stick mappings. <see cref="SpellWheelRulesTests"/> holds the headline cases.
/// </summary>
public class SpellWheelGeometryTests
{
    private static readonly SpellWheelLayout Layout = new(
        new[] { "a", "b", "", "d", "", "", "g", "h" },
        new IReadOnlyList<string>[]
        {
            new[] { "fire1", "fire2", "fire3", "fire4" },
            new[] { "frost1" },
            new string[0],
            new[] { "arc1", "arc2", "arc3", "arc4", "arc5" },
            new[] { "nat1", "nat2", "nat3" },
            new[] { "nec1", "nec2" },
        });

    /// <summary>A cursor at <paramref name="degrees"/> clockwise from the top.</summary>
    private static Vector2 At(float degrees, float radius)
    {
        float radians = Mathf.DegToRad(degrees);
        return new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians)) * radius;
    }

    private static float Off(float a, float b) => Mathf.Abs((((a - b) + 540f) % 360f) - 180f);

    // --- Constants and angles ------------------------------------------------

    [Fact]
    public void TheRings_AreOrderedFromTheCentreOut()
    {
        Assert.True(SpellWheelRules.DeadZone > 0f);
        Assert.True(SpellWheelRules.DeadZone < SpellWheelRules.InnerEdge);
        Assert.True(SpellWheelRules.InnerEdge < SpellWheelRules.OuterEdge);
        Assert.True(SpellWheelRules.OuterEdge < SpellWheelRules.FanEdge);
        Assert.Equal(8, SpellWheelRules.FavouriteWedges);
        Assert.Equal(SpellFavouritesRules.SlotCount, SpellWheelRules.FavouriteWedges);
        Assert.Equal(60f, SpellWheelRules.SchoolWedgeDegrees);
        Assert.Equal(20f, SpellWheelRules.MinFanSpellDegrees);
    }

    [Fact]
    public void TheSchools_AreTheSixSchoolsOfMagic_ClockwiseFromFire()
    {
        Assert.Equal(
            new[]
            {
                DamageType.Fire, DamageType.Frost, DamageType.Lightning,
                DamageType.Arcane, DamageType.Nature, DamageType.Necrotic,
            },
            SpellWheelRules.Schools);
        Assert.DoesNotContain(DamageType.Physical, SpellWheelRules.Schools);
    }

    [Theory]
    [InlineData(0f, -1f, 0f)]
    [InlineData(1f, 0f, 90f)]
    [InlineData(0f, 1f, 180f)]
    [InlineData(-1f, 0f, 270f)]
    [InlineData(1f, -1f, 45f)]
    [InlineData(-1f, -1f, 315f)]
    public void Angles_RunClockwiseFromStraightUp_OnAScreen(float x, float y, float degrees)
    {
        Assert.Equal(degrees, SpellWheelRules.AngleDegrees(new Vector2(x, y)), 3);
    }

    [Fact]
    public void AWedge_IsCentredOnItsAngle_AndWrapsAtTheTop()
    {
        Assert.Equal(0, SpellWheelRules.Wedge(0f, 8));
        Assert.Equal(0, SpellWheelRules.Wedge(359.9f, 8));
        Assert.Equal(0, SpellWheelRules.Wedge(337.6f, 8));
        Assert.Equal(7, SpellWheelRules.Wedge(337.4f, 8));
        Assert.Equal(4, SpellWheelRules.Wedge(180f, 8));
        Assert.Equal(1, SpellWheelRules.Wedge(22.6f, 8));

        Assert.Equal(0, SpellWheelRules.Wedge(359.9f, 6));
        Assert.Equal(5, SpellWheelRules.Wedge(329.9f, 6));
        Assert.Equal(3, SpellWheelRules.Wedge(180f, 6));

        // Every degree lands in a wedge, and each wedge gets its share.
        var counts = new int[8];
        for (int degree = 0; degree < 360; degree++)
        {
            int wedge = SpellWheelRules.Wedge(degree + 0.5f, 8);
            Assert.InRange(wedge, 0, 7);
            counts[wedge]++;
        }

        Assert.All(counts, c => Assert.Equal(45, c));
    }

    // --- Ring boundaries -----------------------------------------------------

    [Fact]
    public void EachRing_StartsExactlyAtItsEdge()
    {
        // Just inside the dead zone is nothing; on its edge the favourites begin.
        Assert.Equal(SpellWheelPickKind.None, SpellWheelRules.Pick(At(0f, SpellWheelRules.DeadZone - 0.001f), Layout, -1).Kind);
        Assert.Equal(SpellWheelPickKind.Favourite, SpellWheelRules.Pick(At(0f, SpellWheelRules.DeadZone + 0.001f), Layout, -1).Kind);

        Assert.Equal(SpellWheelPickKind.Favourite, SpellWheelRules.Pick(At(0f, SpellWheelRules.InnerEdge - 0.001f), Layout, -1).Kind);
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, SpellWheelRules.InnerEdge + 0.001f), Layout, -1).Kind);

        // Inside the rim a latched school is still the school; the fan starts past it.
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, SpellWheelRules.OuterEdge - 0.001f), Layout, 0).Kind);
        Assert.Equal(SpellWheelPickKind.Spell, SpellWheelRules.Pick(At(0f, SpellWheelRules.OuterEdge + 0.001f), Layout, 0).Kind);
        Assert.Equal(SpellWheelPickKind.Spell, SpellWheelRules.Pick(At(0f, SpellWheelRules.FanEdge), Layout, 0).Kind);
    }

    [Fact]
    public void AFavouritePick_CarriesItsSlotAndWhatIsPinnedThere()
    {
        for (int slot = 0; slot < SpellWheelRules.FavouriteWedges; slot++)
        {
            SpellWheelPick pick = SpellWheelRules.Pick(At(slot * 45f, 0.4f), Layout, -1);
            Assert.Equal(SpellWheelPickKind.Favourite, pick.Kind);
            Assert.Equal(slot, pick.Index);
            Assert.Equal(Layout.Favourites[slot], pick.SpellId);
            Assert.Equal(-1, pick.School);
            Assert.Equal(Layout.Favourites[slot].Length > 0, pick.Selects);
        }
    }

    [Fact]
    public void AShortOrHoleyFavouritesList_ReadsAsEmptySlots()
    {
        var sparse = new SpellWheelLayout(new string[] { "only", null! }, Layout.SchoolSpells);
        Assert.Equal("only", SpellWheelRules.Pick(At(0f, 0.4f), sparse, -1).SpellId);

        SpellWheelPick hole = SpellWheelRules.Pick(At(45f, 0.4f), sparse, -1);
        Assert.Equal(string.Empty, hole.SpellId);
        Assert.False(hole.Selects);

        SpellWheelPick beyond = SpellWheelRules.Pick(At(270f, 0.4f), sparse, -1);
        Assert.Equal(6, beyond.Index);
        Assert.Equal(string.Empty, beyond.SpellId);
        Assert.False(beyond.Selects);
    }

    [Fact]
    public void ASchoolPick_NeverSelects_AndCarriesItsSchoolTwice()
    {
        for (int school = 0; school < SpellWheelRules.Schools.Count; school++)
        {
            SpellWheelPick pick = SpellWheelRules.Pick(At(school * 60f, 0.8f), Layout, -1);
            Assert.Equal(new SpellWheelPick(SpellWheelPickKind.School, school, School: school), pick);
            Assert.False(pick.Selects);
            Assert.Equal(string.Empty, pick.SpellId);
        }
    }

    // --- The fan -------------------------------------------------------------

    [Theory]
    [InlineData(0, 60f)]
    [InlineData(1, 60f)]
    [InlineData(2, 60f)]
    [InlineData(3, 60f)]
    [InlineData(4, 80f)]
    [InlineData(5, 100f)]
    [InlineData(9, 180f)]
    [InlineData(18, 360f)]
    [InlineData(40, 360f)]
    public void AFan_IsItsSchoolsWedge_OrWiderWhenItsSpellsNeedTheRoom(int spells, float span)
    {
        Assert.Equal(span, SpellWheelRules.FanSpanDegrees(spells), 3);
    }

    [Fact]
    public void EverySpellInAFan_GetsAtLeastTwentyDegrees()
    {
        for (int school = 0; school < SpellWheelRules.Schools.Count; school++)
        {
            for (int count = 1; count <= 18; count++)
            {
                // Sweep the whole circle in fine steps and measure each spell's share.
                var degrees = new float[count];
                const float step = 0.25f;
                for (float angle = 0f; angle < 360f; angle += step)
                {
                    int index = SpellWheelRules.FanIndex(angle, school, count);
                    Assert.InRange(index, -1, count - 1);
                    if (index >= 0)
                    {
                        degrees[index] += step;
                    }
                }

                float each = SpellWheelRules.FanSpanDegrees(count) / count;
                Assert.True(each >= SpellWheelRules.MinFanSpellDegrees - 0.001f);
                foreach (float share in degrees)
                {
                    // Within a step either side of its exact share, and never under the minimum.
                    Assert.InRange(share, each - (step * 2f), each + (step * 2f));
                    Assert.True(share >= SpellWheelRules.MinFanSpellDegrees - (step * 2f), $"school {school}, {count} spells: one gets {share}");
                }
            }
        }
    }

    [Fact]
    public void AFan_IsCentredOnItsSchool_AndRunsClockwise()
    {
        // Three nature spells: nature is centred at 240, its fan is its own 60 degrees.
        Assert.Equal(0, SpellWheelRules.FanIndex(215f, 4, 3));
        Assert.Equal(1, SpellWheelRules.FanIndex(240f, 4, 3));
        Assert.Equal(2, SpellWheelRules.FanIndex(265f, 4, 3));
        Assert.Equal(-1, SpellWheelRules.FanIndex(209f, 4, 3));
        Assert.Equal(-1, SpellWheelRules.FanIndex(271f, 4, 3));

        // Fire's fan straddles the top of the wheel.
        Assert.Equal(0, SpellWheelRules.FanIndex(325f, 0, 4));
        Assert.Equal(1, SpellWheelRules.FanIndex(350f, 0, 4));
        Assert.Equal(2, SpellWheelRules.FanIndex(10f, 0, 4));
        Assert.Equal(3, SpellWheelRules.FanIndex(35f, 0, 4));
        Assert.Equal(-1, SpellWheelRules.FanIndex(45f, 0, 4));

        // No spells, no fan.
        Assert.Equal(-1, SpellWheelRules.FanIndex(0f, 0, 0));
        Assert.Equal(-1, SpellWheelRules.FanIndex(0f, 0, -3));
    }

    [Fact]
    public void ACrowdedFan_ReachesPastItsSchoolsWedge_WhileItStaysLatched()
    {
        // Arcane (centred at 180) has five spells: its fan runs 130 to 230, over the edges of
        // lightning's and nature's wedges. Latched on arcane, those angles are still arcane spells.
        SpellWheelPick first = SpellWheelRules.Pick(At(135f, 1.2f), Layout, 3);
        Assert.Equal(new SpellWheelPick(SpellWheelPickKind.Spell, 0, "arc1", 3), first);
        SpellWheelPick last = SpellWheelRules.Pick(At(225f, 1.2f), Layout, 3);
        Assert.Equal(new SpellWheelPick(SpellWheelPickKind.Spell, 4, "arc5", 3), last);
        Assert.Equal(3, SpellWheelRules.Latch(last));

        // The same angles with nothing latched are the neighbouring schools.
        Assert.Equal(2, SpellWheelRules.Pick(At(135f, 1.2f), Layout, -1).Index);
        Assert.Equal(4, SpellWheelRules.Pick(At(225f, 1.2f), Layout, -1).Index);
    }

    [Fact]
    public void ACursorMovingThroughTheWheel_OpensLatchesAndClosesTheFan()
    {
        int latched = -1;
        SpellWheelPick Move(float degrees, float radius)
        {
            SpellWheelPick pick = SpellWheelRules.Pick(At(degrees, radius), Layout, latched);
            latched = SpellWheelRules.Latch(pick);
            return pick;
        }

        // Out through a favourite into arcane's wedge: the fan opens.
        Assert.Equal(SpellWheelPickKind.None, Move(180f, 0.1f).Kind);
        Assert.Equal(SpellWheelPickKind.Favourite, Move(180f, 0.4f).Kind);
        Assert.Equal(-1, latched);
        Assert.Equal(SpellWheelPickKind.School, Move(180f, 0.8f).Kind);
        Assert.Equal(3, latched);

        // Past the rim and along the fan, end to end: arcane stays latched the whole way.
        Assert.Equal("arc3", Move(180f, 1.1f).SpellId);
        for (float degrees = 131f; degrees <= 229f; degrees += 1f)
        {
            SpellWheelPick pick = Move(degrees, 1.3f);
            Assert.Equal(SpellWheelPickKind.Spell, pick.Kind);
            Assert.Equal(3, latched);
            Assert.True(pick.Selects);
        }

        // Off the end of the fan: the school at that angle takes over, and its fan opens next.
        SpellWheelPick nature = Move(235f, 1.3f);
        Assert.Equal(SpellWheelPickKind.School, nature.Kind);
        Assert.Equal(4, latched);
        Assert.Equal("nat1", Move(225f, 1.3f).SpellId);

        // Back inside the favourites the fan closes.
        Assert.Equal(SpellWheelPickKind.Favourite, Move(225f, 0.4f).Kind);
        Assert.Equal(-1, latched);

        // A school with no spells latches and offers nothing.
        Assert.Equal(SpellWheelPickKind.School, Move(120f, 0.8f).Kind);
        Assert.Equal(2, latched);
        SpellWheelPick empty = Move(120f, 1.2f);
        Assert.Equal(SpellWheelPickKind.School, empty.Kind);
        Assert.False(empty.Selects);
    }

    [Fact]
    public void ALatchThatNamesNoSchool_IsNoLatch()
    {
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, 1.2f), Layout, 6).Kind);
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, 1.2f), Layout, 99).Kind);
        Assert.Equal(SpellWheelPickKind.School, SpellWheelRules.Pick(At(0f, 1.2f), Layout, -7).Kind);

        Assert.Equal(-1, SpellWheelRules.Latch(SpellWheelPick.None));
        Assert.Equal(-1, SpellWheelRules.Latch(new SpellWheelPick(SpellWheelPickKind.Favourite, 3, "x")));
        Assert.Equal(5, SpellWheelRules.Latch(new SpellWheelPick(SpellWheelPickKind.School, 5, School: 5)));
        Assert.Equal(2, SpellWheelRules.Latch(new SpellWheelPick(SpellWheelPickKind.Spell, 0, "x", 2)));
    }

    [Fact]
    public void EveryPointOnTheWheel_PicksSomethingWellFormed()
    {
        for (int latched = -1; latched < SpellWheelRules.Schools.Count; latched++)
        {
            for (float radius = 0f; radius <= SpellWheelRules.FanEdge + 0.2f; radius += 0.05f)
            {
                for (float degrees = 0f; degrees < 360f; degrees += 3f)
                {
                    SpellWheelPick pick = SpellWheelRules.Pick(At(degrees, radius), Layout, latched);
                    switch (pick.Kind)
                    {
                        case SpellWheelPickKind.None:
                            Assert.True(radius < SpellWheelRules.DeadZone + 0.001f);
                            Assert.False(pick.Selects);
                            break;
                        case SpellWheelPickKind.Favourite:
                            Assert.InRange(pick.Index, 0, SpellWheelRules.FavouriteWedges - 1);
                            Assert.Equal(Layout.Favourites[pick.Index], pick.SpellId);
                            break;
                        case SpellWheelPickKind.School:
                            Assert.InRange(pick.Index, 0, SpellWheelRules.Schools.Count - 1);
                            Assert.Equal(pick.Index, pick.School);
                            Assert.False(pick.Selects);
                            break;
                        default:
                            Assert.Equal(latched, pick.School);
                            Assert.True(radius > SpellWheelRules.OuterEdge - 0.001f);
                            Assert.Equal(Layout.SchoolSpells[latched][pick.Index], pick.SpellId);
                            Assert.True(pick.Selects);
                            break;
                    }
                }
            }
        }
    }

    // --- Cursor and stick ----------------------------------------------------

    [Fact]
    public void TheCursor_MovesFreelyInsideTheFan_AndSlidesAlongItsEdge()
    {
        Vector2 moved = SpellWheelRules.StepCursor(new Vector2(0.2f, -0.1f), new Vector2(-30f, 60f), SpellWheelRules.MouseScale);
        Assert.Equal(0f, moved.X, 4);
        Assert.Equal(0.3f, moved.Y, 4);

        Assert.Equal(new Vector2(0.4f, 0.4f), SpellWheelRules.StepCursor(new Vector2(0.4f, 0.4f), Vector2.Zero, SpellWheelRules.MouseScale));

        // Pinned at the edge, a sideways push turns it round the edge rather than sticking.
        var edge = new Vector2(SpellWheelRules.FanEdge, 0f);
        Vector2 slid = SpellWheelRules.StepCursor(edge, new Vector2(0f, 150f), SpellWheelRules.MouseScale);
        Assert.Equal(SpellWheelRules.FanEdge, slid.Length(), 3);
        Assert.True(slid.Y > 0.5f);

        // And it comes straight back in.
        Vector2 back = SpellWheelRules.StepCursor(edge, new Vector2(-150f, 0f), SpellWheelRules.MouseScale);
        Assert.Equal(SpellWheelRules.FanEdge - 1f, back.X, 3);

        // The rim is one hundred and fifty pixels of mouse travel from the centre.
        Assert.Equal(1f, SpellWheelRules.StepCursor(Vector2.Zero, new Vector2(0f, -150f), SpellWheelRules.MouseScale).Length(), 3);
    }

    [Fact]
    public void TheStick_KeepsItsDirection_AndReachesFurtherTheHarderItIsPushed()
    {
        for (float degrees = 0f; degrees < 360f; degrees += 30f)
        {
            float last = 0f;
            for (float push = SpellWheelRules.StickDead + 0.01f; push <= 1.0001f; push += 0.05f)
            {
                Vector2 cursor = SpellWheelRules.FromStick(At(degrees, push));
                Assert.True(Off(SpellWheelRules.AngleDegrees(cursor), degrees) < 0.05f);
                Assert.True(cursor.Length() >= last - 0.0001f);
                last = cursor.Length();
            }
        }
    }

    [Fact]
    public void TheStick_ReachesEveryRing()
    {
        // At rest and inside its dead zone: the centre, which selects nothing.
        Assert.Equal(Vector2.Zero, SpellWheelRules.FromStick(Vector2.Zero));
        Assert.Equal(SpellWheelPickKind.None, SpellWheelRules.Pick(SpellWheelRules.FromStick(At(90f, 0.24f)), Layout, -1).Kind);

        // A half push: the favourite in that direction.
        SpellWheelPick favourite = SpellWheelRules.Pick(SpellWheelRules.FromStick(At(315f, 0.5f)), Layout, -1);
        Assert.Equal(new SpellWheelPick(SpellWheelPickKind.Favourite, 7, "h"), favourite);
        Assert.Equal(
            SpellWheelPickKind.Favourite,
            SpellWheelRules.Pick(SpellWheelRules.FromStick(At(0f, SpellWheelRules.StickOuter - 0.01f)), Layout, -1).Kind);

        // Past the outer threshold: the school, and it latches.
        SpellWheelPick school = SpellWheelRules.Pick(SpellWheelRules.FromStick(At(180f, 0.8f)), Layout, -1);
        Assert.Equal(SpellWheelPickKind.School, school.Kind);
        Assert.Equal(3, school.Index);

        // A full push with that school latched: a spell in its fan. Pushed past its travel, the same.
        SpellWheelPick spell = SpellWheelRules.Pick(SpellWheelRules.FromStick(At(180f, 1f)), Layout, SpellWheelRules.Latch(school));
        Assert.Equal("arc3", spell.SpellId);
        Assert.Equal(SpellWheelRules.FanEdge, SpellWheelRules.FromStick(At(45f, 1.4f)).Length(), 3);

        // Some stick travel is left for the school ring itself, so a fan is not all the stick can hit.
        float rimPush = SpellWheelRules.StickOuter
            + ((1f - SpellWheelRules.StickOuter) * (SpellWheelRules.OuterEdge - SpellWheelRules.InnerEdge)
                / (SpellWheelRules.FanEdge - SpellWheelRules.InnerEdge));
        Assert.Equal(SpellWheelRules.OuterEdge, SpellWheelRules.FromStick(At(0f, rimPush)).Length(), 3);
        Assert.True(rimPush - SpellWheelRules.StickOuter > 0.1f);
    }

    [Fact]
    public void ATap_NeedsBothAShortPressAndAStillCursor()
    {
        Assert.Equal(SpellWheelGesture.Tap, SpellWheelRules.Classify(SpellWheelRules.TapSeconds - 0.001f, SpellWheelRules.DeadZone - 0.001f));
        Assert.Equal(SpellWheelGesture.Wheel, SpellWheelRules.Classify(SpellWheelRules.TapSeconds - 0.001f, SpellWheelRules.DeadZone + 0.001f));
        Assert.Equal(SpellWheelGesture.Wheel, SpellWheelRules.Classify(SpellWheelRules.TapSeconds + 0.001f, 0f));
        Assert.Equal(SpellWheelGesture.Wheel, SpellWheelRules.Classify(10f, 5f));
    }
}

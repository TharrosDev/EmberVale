using Embervale.Combat;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Covers the pure combat screen-feedback mapping (Phase 29D): each state gets a distinct tint, and a
/// parry/crit flashes harder than a routine block.
/// </summary>
public class CombatFeedbackFxTests
{
    [Fact]
    public void EachState_HasADistinctTint()
    {
        var tints = new[]
        {
            CombatFeedbackFx.Tint(CombatFeedback.Crit),
            CombatFeedbackFx.Tint(CombatFeedback.Block),
            CombatFeedbackFx.Tint(CombatFeedback.Stagger),
            CombatFeedbackFx.Tint(CombatFeedback.Parry),
        };

        for (int i = 0; i < tints.Length; i++)
        {
            for (int j = i + 1; j < tints.Length; j++)
            {
                Assert.NotEqual(tints[i], tints[j]);
            }
        }
    }

    [Fact]
    public void Parry_PopsHarderThanBlock()
    {
        Assert.True(CombatFeedbackFx.PeakAlpha(CombatFeedback.Parry) > CombatFeedbackFx.PeakAlpha(CombatFeedback.Block));
    }
}

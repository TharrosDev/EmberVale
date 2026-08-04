using Embervale.Enemies;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// Pins what a boss's death pays out (Phase 36E). This is the one decision in the boss framework
/// that has already gone wrong in a shipped build, so it is pinned rather than trusted:
///
/// the director granted rewards behind an "already defeated?" guard but queued the defeat dialogue
/// <em>unconditionally</em>, for any boss. Once the Iron King was down, killing any dragon re-opened
/// his "absorb the flame?" choice — worth +25 corruption, with no condition of its own — so the
/// game's defining meter could be topped up once per boss kill.
/// </summary>
public class BossDefeatTests
{
    private const string Relic = "item.relic.iron_heart";
    private const string Dialogue = "dialogue.iron_king_absorb";

    [Fact]
    public void AFirstDefeatGrantsEverythingAuthored()
    {
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: false, Relic, Dialogue);

        Assert.True(outcome.GrantReward);
        Assert.True(outcome.SetFlag);
        Assert.True(outcome.OpenDialogue);
    }

    [Fact]
    public void ARepeatDefeatGrantsNothing()
    {
        // The bug, in one assertion: the dialogue must be gated with the reward, not beside it.
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: true, Relic, Dialogue);

        Assert.False(outcome.GrantReward);
        Assert.False(outcome.SetFlag);
        Assert.False(outcome.OpenDialogue);
    }

    [Fact]
    public void TheDialogueIsNeverOpenedWithoutTheReward()
    {
        // The three move together on purpose. If this ever splits, the corruption farm is back.
        foreach (bool defeated in new[] { true, false })
        {
            BossDefeat.Outcome outcome = BossDefeat.Resolve(defeated, Relic, Dialogue);
            Assert.Equal(outcome.GrantReward, outcome.OpenDialogue);
        }
    }

    [Fact]
    public void ABossWithNoRewardStillRecordsItsDefeat()
    {
        // A lair boss: the beat plays, the flag is set, nothing is granted.
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: false, string.Empty, string.Empty);

        Assert.False(outcome.GrantReward);
        Assert.True(outcome.SetFlag);
        Assert.False(outcome.OpenDialogue);
    }

    [Fact]
    public void ARewardWithNoConversationIsFine()
    {
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: false, Relic, string.Empty);

        Assert.True(outcome.GrantReward);
        Assert.True(outcome.SetFlag);
        Assert.False(outcome.OpenDialogue);
    }

    [Fact]
    public void AConversationWithNoRewardIsFine()
    {
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: false, string.Empty, Dialogue);

        Assert.False(outcome.GrantReward);
        Assert.True(outcome.SetFlag);
        Assert.True(outcome.OpenDialogue);
    }

    [Fact]
    public void NullIdsAreTreatedAsUnauthored()
    {
        BossDefeat.Outcome outcome = BossDefeat.Resolve(alreadyDefeated: false, null, null);

        Assert.False(outcome.GrantReward);
        Assert.False(outcome.OpenDialogue);
        Assert.True(outcome.SetFlag);
    }

    [Fact]
    public void NoneGrantsNothingAtAll()
    {
        Assert.False(BossDefeat.Outcome.None.GrantReward);
        Assert.False(BossDefeat.Outcome.None.SetFlag);
        Assert.False(BossDefeat.Outcome.None.OpenDialogue);
    }
}

using Embervale.Animation;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// That the Iron King's replacement body binds every animation slot the fight uses (Phase 37G).
///
/// ⚠️ <b>THIS TEST IS THE WHOLE REASON THE BODY SWAP WAS SAFE TO MAKE.</b> A slot that resolves to
/// nothing is <em>silent</em>: <see cref="AnimationClips.Resolve"/> returning empty is a legal answer
/// (a creature with no block clip simply never blocks), so a boss that has quietly lost its attack
/// animation winds up and never strikes, and nothing logs a word. The previous body shipped
/// <c>Slash</c>, <c>Stab</c> and <c>HitReact</c>; this one ships <c>Sword_Slash</c> and
/// <c>HitRecieve</c> — no overlap on two of the three, and the difference is invisible in a render
/// of a standing model.
///
/// ⚠️ <b>The pack also ships GUN clips</b> — <c>Idle_Gun</c>, <c>Gun_Shoot</c>, <c>Run_Shoot</c>. The
/// failure this guards is <c>AnimationClips</c>' own documented one: "a fantasy boss idling in a
/// rifle stance, which nothing would flag as an error".
/// </summary>
public class IronKingClipsTests
{
    /// <summary>
    /// The clip list read out of the imported `boss_iron_king.glb`, verbatim. Hard-coded rather than
    /// loaded: the test project cannot open a Godot resource, and a list that regenerated itself
    /// would agree with a broken model as happily as a working one.
    /// </summary>
    private static readonly string[] Clips =
    {
        "CharacterArmature|Death", "CharacterArmature|Gun_Shoot", "CharacterArmature|HitRecieve",
        "CharacterArmature|HitRecieve_2", "CharacterArmature|Idle", "CharacterArmature|Idle_Gun",
        "CharacterArmature|Idle_Gun_Pointing", "CharacterArmature|Idle_Gun_Shoot",
        "CharacterArmature|Idle_Neutral", "CharacterArmature|Idle_Sword",
        "CharacterArmature|Interact", "CharacterArmature|Kick_Left", "CharacterArmature|Kick_Right",
        "CharacterArmature|Punch_Left", "CharacterArmature|Punch_Right", "CharacterArmature|Roll",
        "CharacterArmature|Run", "CharacterArmature|Run_Back", "CharacterArmature|Run_Left",
        "CharacterArmature|Run_Right", "CharacterArmature|Run_Shoot",
        "CharacterArmature|Sword_Slash", "CharacterArmature|Walk", "CharacterArmature|Wave",
    };

    [Theory]
    [InlineData("idle", "CharacterArmature|Idle")]
    [InlineData("run", "CharacterArmature|Run")]
    [InlineData("attack", "CharacterArmature|Sword_Slash")]
    [InlineData("hit", "CharacterArmature|HitRecieve")]
    [InlineData("death", "CharacterArmature|Death")]
    public void EverySlotTheFightNeedsBindsToTheRightClip(string slot, string expected)
    {
        Assert.Equal(expected, AnimationClips.Resolve(Clips, slot));
    }

    [Fact]
    public void IdleDoesNotResolveToARifleStance()
    {
        // The pack lists Idle_Gun, Idle_Gun_Pointing, Idle_Gun_Shoot and Idle_Neutral alongside Idle.
        // Only AnimationClips' exact-match pass keeps the plain one — a first-match-wins scan would
        // be correct here purely because the list happens to be alphabetical.
        string idle = AnimationClips.Resolve(Clips, "idle");
        Assert.DoesNotContain("Gun", idle);
        Assert.EndsWith("|Idle", idle);
    }

    [Fact]
    public void AttackIsTheSwingAndNotAPunchOrAStance()
    {
        // "punch" IS an attack alias and this body ships Punch_Left/Punch_Right — a king with a sword
        // on his hip throwing hooks is the wrong read, and the alias order (weapon words first) is
        // what prevents it.
        Assert.Equal("CharacterArmature|Sword_Slash", AnimationClips.Resolve(Clips, "attack"));
    }

    [Fact]
    public void TheSlotsThisBodyGenuinelyLacksResolveToEmptyRatherThanToSomethingWrong()
    {
        // ⚠️ Empty is the CORRECT answer here and the previous body answered the same way — the Iron
        // King has never had a block, a cast or a channel. What would be a defect is one of these
        // grabbing Idle_Sword or Roll and playing it at a moment the fight means something else.
        Assert.Equal(string.Empty, AnimationClips.Resolve(Clips, "cast"));
        Assert.Equal(string.Empty, AnimationClips.Resolve(Clips, "channel"));
    }
}

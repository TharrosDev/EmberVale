namespace Embervale.Combat;

/// <summary>How much a body is moved by a blow that breaks its poise.</summary>
// APPEND ONLY: ordinals reach .tres and saves — never reorder/insert/remove (EnumStabilityTests).
public enum ReactionClass
{
    /// <summary>Small things. Everything staggers them and hard blows knock them down.</summary>
    Small,

    /// <summary>People. The default, and the one the game is balanced around.</summary>
    Humanoid,

    /// <summary>Armoured humanoids. They flinch where a lighter body would stagger.</summary>
    Armored,

    /// <summary>Large creatures. Only a real blow moves them, and never far.</summary>
    Large,

    /// <summary>Bosses. Poise still matters — it is how a punish window opens — but they are never
    /// knocked down and never pushed.</summary>
    Boss,
}

/// <summary>What actually happens to a body when its poise breaks.</summary>
// APPEND ONLY: ordinals reach saves — never reorder/insert/remove (EnumStabilityTests).
public enum StaggerResponse
{
    /// <summary>Nothing. The blow registers as damage and the body does not move.</summary>
    None,

    /// <summary>A flinch: a short reaction that does not interrupt what the body was doing.</summary>
    Flinch,

    /// <summary>A stagger: the body's action is cancelled and a punish window opens.</summary>
    Stagger,

    /// <summary>A heavy stagger: longer, and it moves the body.</summary>
    Heavy,

    /// <summary>Off its feet. Only ever the lightest bodies, and only from a real blow.</summary>
    Knockdown,
}

/// <summary>
/// The poise model, as rules rather than as one duration shared by everything.
///
/// <para><b>What this replaced.</b> A single <c>StaggerDuration</c> and a single poise pool, so a
/// goblin and the Iron King reacted to a hit identically once their numbers were spent — every blow
/// interrupted every enemy for the same 0.6 s, which is the "weightless ragdoll" §9 names. The
/// numbers differed; the *response* did not.</para>
///
/// <para>Godot-free, because these are the rules a designer tunes and the rules a test can pin.</para>
/// </summary>
public static class PoiseReaction
{
    /// <summary>
    /// What a body of this class does when a blow breaks its poise by <paramref name="overkill"/>
    /// (the poise damage past what was left, as a fraction of the body's maximum poise).
    ///
    /// ⚠️ <b>A boss is never knocked down and never pushed, and that is not a number to tune.</b>
    /// A boss that can be knocked over can be chain-knocked, and a fight that can be chain-knocked
    /// has one answer. Its poise still breaks — that is how the punish window opens — it simply
    /// staggers in place.
    /// </summary>
    public static StaggerResponse Resolve(ReactionClass body, float overkill) => body switch
    {
        ReactionClass.Boss => StaggerResponse.Stagger,

        ReactionClass.Large => overkill >= 0.6f ? StaggerResponse.Heavy : StaggerResponse.Flinch,

        ReactionClass.Armored => overkill >= 0.8f ? StaggerResponse.Heavy
            : overkill >= 0.25f ? StaggerResponse.Stagger
            : StaggerResponse.Flinch,

        ReactionClass.Small => overkill >= 0.5f ? StaggerResponse.Knockdown : StaggerResponse.Stagger,

        _ => overkill >= 0.75f ? StaggerResponse.Heavy : StaggerResponse.Stagger,
    };

    /// <summary>How long the response lasts, scaled off the body's authored stagger duration so a
    /// creature that was always slow to recover still is.</summary>
    public static float Duration(StaggerResponse response, float baseSeconds) => response switch
    {
        StaggerResponse.None => 0f,
        StaggerResponse.Flinch => baseSeconds * 0.35f,
        StaggerResponse.Stagger => baseSeconds,
        StaggerResponse.Heavy => baseSeconds * 1.6f,
        StaggerResponse.Knockdown => baseSeconds * 2.4f,
        _ => baseSeconds,
    };

    /// <summary>
    /// Whether this response cancels what the body was doing.
    ///
    /// ⚠️ A flinch deliberately does NOT. That is the whole distinction: an armoured enemy takes a
    /// light hit, reacts to it visibly, and keeps swinging — which is what makes hitting one feel
    /// different from hitting a goblin rather than merely slower.
    /// </summary>
    public static bool Interrupts(StaggerResponse response) =>
        response is StaggerResponse.Stagger or StaggerResponse.Heavy or StaggerResponse.Knockdown;

    /// <summary>
    /// How far the blow pushes the body, in metres, from the attack's authored knockback.
    ///
    /// Large bodies take a fraction and bosses take none. A knockback that moved a boss would let
    /// the player walk it out of its own arena.
    /// </summary>
    public static float Knockback(ReactionClass body, StaggerResponse response, float authored)
    {
        if (body == ReactionClass.Boss || !Interrupts(response) || authored <= 0f)
        {
            return 0f;
        }

        float scale = body switch
        {
            ReactionClass.Large => 0.25f,
            ReactionClass.Armored => 0.6f,
            ReactionClass.Small => 1.3f,
            _ => 1f,
        };

        return authored * scale * (response == StaggerResponse.Heavy ? 1.4f : 1f);
    }

    /// <summary>
    /// The overkill fraction for a blow: how far past the remaining poise it went, relative to the
    /// body's maximum.
    ///
    /// Relative rather than absolute so the same blow means "a lot" to a goblin and "a little" to a
    /// dragon without anyone authoring per-creature thresholds.
    /// </summary>
    public static float Overkill(float poiseDamage, float poiseLeft, float maxPoise)
    {
        if (maxPoise <= 0f)
        {
            return 1f;
        }

        float past = poiseDamage - poiseLeft;
        return past <= 0f ? 0f : past / maxPoise;
    }
}

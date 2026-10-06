namespace Embervale.Settings;

/// <summary>
/// What the difficulty setting does, which is one thing: it scales the damage of blows that land
/// on the player. Nothing else moves with it: not what the player deals, not what one creature does
/// to another, not health, loot or experience. Pure, so the numbers are pinned by tests.
/// </summary>
public static class DifficultyRules
{
    public const int Story = 0;
    public const int Normal = 1;
    public const int Hard = 2;

    public const float StoryIncoming = 0.6f;
    public const float HardIncoming = 1.35f;

    /// <summary>
    /// The multiplier on a blow against the player. ⚠️ Normal, and any number that is not a
    /// difficulty, is exactly 1: the headless gates and every balance number in the game were
    /// measured there, and multiplying by one must leave them bit for bit as they were.
    /// </summary>
    public static float IncomingPlayerDamage(int difficulty) => difficulty switch
    {
        Story => StoryIncoming,
        Hard => HardIncoming,
        _ => 1f,
    };

    /// <summary>As <see cref="IncomingPlayerDamage"/>, but 1 for a defender who is not the player.</summary>
    public static float Incoming(int difficulty, bool defenderIsPlayer) =>
        defenderIsPlayer ? IncomingPlayerDamage(difficulty) : 1f;
}

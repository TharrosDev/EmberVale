using System.Collections.Generic;
using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Items;
using Embervale.Player;
using Embervale.Save;
using Godot;

namespace Embervale.Loot;

/// <summary>
/// The player's saved loot history: the dry streak <see cref="PityRules"/> reads, the once-per-save
/// drops already claimed (a Flamebearer's signature legendary), a per-save salt that keeps a chest's
/// deterministic roll from being the same in every playthrough, and how many reward chests have been
/// stood up (their persistent ids need a counter that survives a load).
///
/// It is a plain object, not a node: the player's <see cref="InteractionSensor"/> owns one, registers
/// it for saving and unregisters it on teardown, so its lifetime is the player's. A save written
/// before it existed has no entry, which <see cref="Load"/> reads as a fresh ledger.
/// </summary>
public sealed class LootLedger : ISaveable
{
    private const string StreakKey = "dry";
    private const string ClaimedKey = "claimed";
    private const string SaltKey = "salt";
    private const string ChestsKey = "chests";

    private readonly HashSet<string> _claimed = new();

    public LootLedger()
    {
        Salt = NewSalt();
    }

    /// <summary>One ledger per save: the player is the only looter with a history.</summary>
    public string SaveId => "loot_ledger";

    /// <summary>Consecutive rolled pieces below Rare (see <see cref="PityRules"/>).</summary>
    public int DryStreak { get; private set; }

    /// <summary>Mixed into every deterministic chest seed (<see cref="LootSeeds"/>).</summary>
    public long Salt { get; private set; }

    /// <summary>Reward chests stood up so far; the next one's id uses it.</summary>
    public int ChestsSpawned { get; private set; }

    /// <summary>The ledger of whoever is looting: the finder's own when it has one, otherwise the
    /// player's (a companion's kill, a burn that outlived its caster). Null with no player.</summary>
    public static LootLedger? Of(IEntity? finder)
    {
        if (finder?.GetComponent<InteractionSensor>() is { } own)
        {
            return own.Ledger;
        }

        return ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player)
            ? player.GetComponent<InteractionSensor>()?.Ledger
            : null;
    }

    /// <summary>Records one rolled piece against the streak.</summary>
    public void RecordRoll(ItemRarity rarity)
    {
        DryStreak = PityRules.Next(DryStreak, rarity);
    }

    public bool HasClaimed(string key) => _claimed.Contains(key);

    /// <summary>Marks a once-per-save drop as taken. False when it already was.</summary>
    public bool Claim(string key) => !string.IsNullOrEmpty(key) && _claimed.Add(key);

    /// <summary>The next reward-chest ordinal (1, 2, ...), counted so ids never repeat in a save.</summary>
    public int NextChestOrdinal() => ++ChestsSpawned;

    public Godot.Collections.Dictionary Save()
    {
        var claimed = new Godot.Collections.Array();
        foreach (string key in _claimed)
        {
            claimed.Add(key);
        }

        return new Godot.Collections.Dictionary
        {
            [StreakKey] = DryStreak,
            [ClaimedKey] = claimed,
            [SaltKey] = Salt,
            [ChestsKey] = ChestsSpawned,
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        DryStreak = data.TryGetValue(StreakKey, out Variant streak) ? Mathf.Max(0, streak.AsInt32()) : 0;
        ChestsSpawned = data.TryGetValue(ChestsKey, out Variant chests) ? Mathf.Max(0, chests.AsInt32()) : 0;

        // A save with no salt predates the ledger: give it a new one rather than keep the salt of
        // the timeline being abandoned.
        Salt = data.TryGetValue(SaltKey, out Variant salt) ? salt.AsInt64() : NewSalt();

        _claimed.Clear();
        if (data.TryGetValue(ClaimedKey, out Variant claimed) && claimed.VariantType == Variant.Type.Array)
        {
            foreach (Variant key in claimed.AsGodotArray())
            {
                string id = key.AsString();
                if (id.Length > 0)
                {
                    _claimed.Add(id);
                }
            }
        }
    }

    /// <summary>Exclusive upper bound of a salt: 2^53, past which a whole number is no longer exact
    /// as a double. The engine's JSON parser returns every number as a float, so a larger salt came
    /// back from the first load rounded, and every unopened chest rerolled.</summary>
    public const long SaltLimit = 1L << 53;

    private static long NewSalt() => System.Random.Shared.NextInt64(1, SaltLimit);
}

/// <summary>Turns a persistent id and a save's salt into an RNG seed. Pure, so the same chest in the
/// same save always rolls the same loot (reloading does not reroll it) and two chests never share a
/// seed by accident.</summary>
public static class LootSeeds
{
    /// <summary>FNV-1a over the id's characters, folded with the salt.</summary>
    public static ulong For(string? persistentId, long salt)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;

        ulong hash = offset;
        foreach (char c in persistentId ?? string.Empty)
        {
            hash ^= c;
            hash *= prime;
        }

        hash ^= unchecked((ulong)salt);
        hash *= prime;
        return hash == 0UL ? offset : hash;
    }
}

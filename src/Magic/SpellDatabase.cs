using System.Collections.Generic;
using Embervale.Core;

namespace Embervale.Magic;

/// <summary>
/// Process-wide registry of <see cref="SpellResource"/>s, scanned once at startup
/// from <c>res://data/spells</c> (mirrors <see cref="Embervale.Progression.PerkDatabase"/>).
/// A <see cref="SpellcastingComponent"/> resolves its known spell ids through here,
/// and save/load restores a spell list by id. New spell = drop a <c>.tres</c>.
/// </summary>
public static class SpellDatabase
{
    private const string DefaultDirectory = "res://data/spells";

    private static readonly Dictionary<string, SpellResource> ById = new();
    private static readonly List<SpellResource> AllList = new();

    public static IReadOnlyList<SpellResource> All => AllList;

    public static void Initialize(string directory = DefaultDirectory)
    {
        ResourceDirectory.Load<SpellResource>(
            directory, "spell", spell => spell.Id, ById, AllList);
    }

    public static SpellResource? Get(string id)
    {
        return ById.TryGetValue(id, out SpellResource? spell) ? spell : null;
    }
}

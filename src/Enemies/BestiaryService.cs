using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Save;
using Godot;

namespace Embervale.Enemies;

/// <summary>
/// The Ash Hunters' tally (Phase 34G): how many of each creature the party has put down, and how
/// many of those were Ashen. Mirrors <see cref="Embervale.World.MapService"/> — a
/// <see cref="ServiceLocator"/>-registered <see cref="ISaveable"/> node with a
/// <see cref="Revision"/> counter the panel diffs against — because a bestiary is the same shape
/// of problem as map discovery: a growing set of ids the UI renders.
///
/// Counts <em>any</em> registered enemy's death, not only kills the player landed. Quest objectives
/// are killer-attributed because a quest is a contract; a field journal is a record of what the
/// party brought down, and a session where a companion lands the last blow should not leave the
/// page blank. That divergence from <c>QuestLogComponent</c> is deliberate.
/// </summary>
[GlobalClass]
public partial class BestiaryService : Node, ISaveable
{
    /// <summary>Group an Ashen (Phase 34F) spawn is tagged with, so a corrupted kill can be told
    /// apart from a plain one — the affliction deliberately leaves <c>TemplateId</c> alone.</summary>
    public const string AshenGroup = "bestiary.ashen";

    public string SaveId => "bestiary";

    private readonly Dictionary<string, int> _kills = new();
    private readonly Dictionary<string, int> _ashenKills = new();

    /// <summary>Bumped whenever a tally changes, so the bestiary UI knows to rebuild.</summary>
    public int Revision { get; private set; }

    public override void _EnterTree()
    {
        ServiceLocator.Instance?.Register(this);
        SaveManager.Instance?.Register(this);
        EventBus.Instance?.Subscribe<EntityDiedEvent>(OnEntityDied);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<EntityDiedEvent>(OnEntityDied);
        SaveManager.Instance?.Unregister(this);
        ServiceLocator.Instance?.Unregister(this);
    }

    /// <summary>Total kills recorded for a creature.</summary>
    public int KillsOf(string templateId) =>
        _kills.TryGetValue(templateId, out int count) ? count : 0;

    /// <summary>How many of those were Ashen — the Ash Hunters' actual brief, "corrupted beasts".</summary>
    public int AshenKillsOf(string templateId) =>
        _ashenKills.TryGetValue(templateId, out int count) ? count : 0;

    /// <summary>The reveal stage of a creature's page, given its authored threshold.</summary>
    public BestiaryStage StageOf(BestiaryEntryResource entry) =>
        BestiaryStages.Of(KillsOf(entry.Id), entry.KillsToKnow);

    /// <summary>How many creatures have been seen at least once (for the screen's header).</summary>
    public int DiscoveredCount => _kills.Count;

    private void OnEntityDied(EntityDiedEvent e)
    {
        // EntityDiedEvent fires for the player, the training dummy, companions and props too, so
        // filter to actual creatures — and to ones the registry knows, which keeps a debug spawn
        // from inventing a page that no bestiary entry backs.
        if (e.Entity is not EnemyEntity || !EnemyTemplateRegistry.IsRegistered(e.Entity.TemplateId))
        {
            return;
        }

        string id = e.Entity.TemplateId;
        _kills[id] = KillsOf(id) + 1;

        if (e.Entity.Body.IsInGroup(AshenGroup))
        {
            _ashenKills[id] = AshenKillsOf(id) + 1;
        }

        Revision++;
    }

    public Godot.Collections.Dictionary Save()
    {
        return new Godot.Collections.Dictionary
        {
            ["kills"] = ToVariant(_kills),
            ["ashen"] = ToVariant(_ashenKills),
        };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        ReadInto(data, "kills", _kills);
        ReadInto(data, "ashen", _ashenKills);
        Revision++;
    }

    private static Godot.Collections.Dictionary ToVariant(Dictionary<string, int> source)
    {
        var result = new Godot.Collections.Dictionary();
        foreach (KeyValuePair<string, int> pair in source)
        {
            result[pair.Key] = pair.Value;
        }

        return result;
    }

    private static void ReadInto(
        Godot.Collections.Dictionary data, string key, Dictionary<string, int> target)
    {
        target.Clear();
        if (!data.TryGetValue(key, out Variant raw) || raw.VariantType != Variant.Type.Dictionary)
        {
            return;
        }

        Godot.Collections.Dictionary stored = raw.AsGodotDictionary();
        foreach (Variant id in stored.Keys)
        {
            target[id.AsString()] = stored[id].AsInt32();
        }
    }
}

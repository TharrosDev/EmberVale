using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Entities;
using Embervale.Save;
using Godot;

namespace Embervale.Dialogue;

/// <summary>
/// A simple persistent set of named boolean story flags carried by an actor (the
/// player). Dialogue choices set/clear flags and gate replies on them, giving
/// conversations memory ("you've met the elder") without bespoke state per NPC.
/// It is deliberately general — later systems (NPC schedules, world events) can read
/// and write the same flags. Implements <see cref="ISaveable"/> so memory survives
/// save/load.
/// </summary>
[GlobalClass]
public partial class StoryFlagsComponent : EntityComponent, ISaveable
{
    private readonly HashSet<string> _flags = new();

    public string SaveId => SaveKey("flags");

    public IReadOnlyCollection<string> Flags => _flags;

    protected override void OnInitialize()
    {
        RegisterSaveable();
    }

    protected override void OnTeardown()
    {
        SaveManager.Instance?.Unregister(this);
    }

    public bool Has(string flag) => !string.IsNullOrEmpty(flag) && _flags.Contains(flag);

    /// <summary>Sets a flag; raises <see cref="StoryFlagChangedEvent"/> when it actually changes.</summary>
    public void Set(string flag)
    {
        if (string.IsNullOrEmpty(flag) || !_flags.Add(flag))
        {
            return;
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new StoryFlagChangedEvent(Entity, flag, true));
        }
    }

    /// <summary>Clears a flag; raises <see cref="StoryFlagChangedEvent"/> when it actually changes.</summary>
    public void Clear(string flag)
    {
        if (string.IsNullOrEmpty(flag) || !_flags.Remove(flag))
        {
            return;
        }

        if (Entity != null)
        {
            EventBus.Instance?.Publish(new StoryFlagChangedEvent(Entity, flag, false));
        }
    }

    // --- ISaveable ----------------------------------------------------------

    public Godot.Collections.Dictionary Save()
    {
        var flags = new Godot.Collections.Array();
        foreach (string flag in _flags)
        {
            flags.Add(flag);
        }

        return new Godot.Collections.Dictionary { ["flags"] = flags };
    }

    public void Load(Godot.Collections.Dictionary data)
    {
        _flags.Clear();

        if (data.TryGetValue("flags", out Variant flagsVar))
        {
            foreach (Variant entry in flagsVar.AsGodotArray())
            {
                string flag = entry.AsString();
                if (!string.IsNullOrEmpty(flag))
                {
                    _flags.Add(flag);
                }
            }
        }
    }
}

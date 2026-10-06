using System.Collections.Generic;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The books the effect director keeps, with no node in them: which effects are alive, whose they
/// are, how many lights are lit. Pure, so the budget is a test.
///
/// <para><b>An effect is a group, not a node.</b> One fireball landing is a flare, a burst, some
/// smoke and a shock ring, which is four pooled nodes and one effect. The live-effect budget counts
/// groups: a group opens when its first block is added and closes when its last one is retired, so
/// a spawn that drew nothing (too far away) never held a place.</para>
///
/// <para>When the budget is full the oldest group that is not the player's gives way, as
/// <see cref="VfxBudgetRules.PickRecycle"/> says, with two refinements the table leaves open. An
/// <em>essential</em> group is the picture of something the player has to be able to see (a
/// wind-up aura, which is the warning they dodge on; a bolt in flight; a zone; a wall): it is never
/// offered, and it does not take a place in the budget either (<see cref="BudgetedGroups"/>), or a
/// fight with eight casters would have spent the lowest tier's whole budget on auras and bolts and
/// every impact would evict the one before it. A <em>sustained</em> group that is not essential (a
/// channelled beam, which is redrawn on its next tick anyway) is offered only when no one-shot is
/// left to take.</para>
/// </summary>
public sealed class VfxLedger
{
    private struct Entry
    {
        public double Born;
        public bool Player;
        public bool Essential;
        public bool Sustained;
        public int Blocks;
    }

    private readonly Dictionary<int, Entry> _groups = new();
    private readonly List<int> _ids = new();
    private readonly List<(double Age, bool Player)> _ages = new();
    private int _nextId;
    private int _essential;

    /// <summary>Effects alive now, essential ones included.</summary>
    public int LiveGroups => _groups.Count;

    /// <summary>Effects alive now that count against the live-effect budget: every group that is
    /// not essential.</summary>
    public int BudgetedGroups => _groups.Count - _essential;

    /// <summary>Spell lights lit now.</summary>
    public int Lights { get; private set; }

    /// <summary>How many of those cast shadows.</summary>
    public int ShadowedLights { get; private set; }

    /// <summary>A fresh group id. Holds no place in the budget until a block is added under it.
    /// Never 0: that id means "not counted" (a ground mark, which has a budget of its own).</summary>
    public int NextId()
    {
        _nextId = _nextId == int.MaxValue ? 1 : _nextId + 1;
        return _nextId;
    }

    /// <summary>A block joined <paramref name="group"/>; the first one opens it.</summary>
    public void BlockAdded(int group, double now, bool player, bool essential, bool sustained)
    {
        if (group == 0)
        {
            return;
        }

        if (!_groups.TryGetValue(group, out Entry entry))
        {
            entry = new Entry { Born = now, Player = player, Essential = essential, Sustained = sustained };
            if (essential)
            {
                _essential++;
            }
        }

        entry.Blocks++;
        _groups[group] = entry;
    }

    /// <summary>A block of <paramref name="group"/> was retired. True when that closed the group.</summary>
    public bool BlockRemoved(int group)
    {
        if (group == 0 || !_groups.TryGetValue(group, out Entry entry))
        {
            return false;
        }

        entry.Blocks--;
        if (entry.Blocks <= 0)
        {
            _groups.Remove(group);
            if (entry.Essential)
            {
                _essential--;
            }

            return true;
        }

        _groups[group] = entry;
        return false;
    }

    /// <summary>Whether <paramref name="group"/> still has a block alive.</summary>
    public bool IsLive(int group) => _groups.ContainsKey(group);

    /// <summary>Blocks alive under <paramref name="group"/>.</summary>
    public int BlocksOf(int group) => _groups.TryGetValue(group, out Entry entry) ? entry.Blocks : 0;

    /// <summary>The group that gives way to a new effect, or 0 when nothing may be taken.</summary>
    public int PickVictim(double now)
    {
        int victim = Pick(now, sustained: false);
        return victim != 0 ? victim : Pick(now, sustained: true);
    }

    private int Pick(double now, bool sustained)
    {
        _ids.Clear();
        _ages.Clear();
        foreach (KeyValuePair<int, Entry> pair in _groups)
        {
            if (pair.Value.Essential || pair.Value.Sustained != sustained)
            {
                continue;
            }

            _ids.Add(pair.Key);
            _ages.Add((now - pair.Value.Born, pair.Value.Player));
        }

        int index = VfxBudgetRules.PickRecycle(_ages);
        return index < 0 ? 0 : _ids[index];
    }

    /// <summary>Takes one of <paramref name="max"/> lights. False when they are all lit.</summary>
    public bool TakeLight(int max)
    {
        if (Lights >= max)
        {
            return false;
        }

        Lights++;
        return true;
    }

    public void ReturnLight()
    {
        if (Lights > 0)
        {
            Lights--;
        }
    }

    /// <summary>Takes one of <paramref name="max"/> shadow-casting lights.</summary>
    public bool TakeShadow(int max)
    {
        if (ShadowedLights >= max)
        {
            return false;
        }

        ShadowedLights++;
        return true;
    }

    public void ReturnShadow()
    {
        if (ShadowedLights > 0)
        {
            ShadowedLights--;
        }
    }

    /// <summary>Forgets everything: a load freed every effect at once.</summary>
    public void Forget()
    {
        _groups.Clear();
        _essential = 0;
        Lights = 0;
        ShadowedLights = 0;
    }
}

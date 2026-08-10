using System;
using System.Collections.Generic;

namespace Embervale.Economy;

/// <summary>What has happened to a place's trade for a few days (Phase 38T).</summary>
public enum ShockKind
{
    /// <summary>The road is cut, the seam has flooded, the boats are not going out — a good the place
    /// normally has is suddenly dear here.</summary>
    Shortage = 0,

    /// <summary>A caravan got through and the warehouses are full — a good the place normally wants is
    /// suddenly cheap here.</summary>
    Glut = 1,

    /// <summary>A market fair: every good the district trades in floods the stalls at once. The only
    /// kind that touches more than one tag, and the only one that can land on a cell which authors no
    /// standing surplus or demand at all.</summary>
    Fair = 2,
}

/// <summary>
/// One live disturbance to a settlement's trade (Phase 38T): a cell, a tag, a kind, and the day window
/// it covers. <see cref="Tag"/> is unused for a <see cref="ShockKind.Fair"/>, which takes the whole of
/// the cell's authored candidate list.
/// </summary>
public readonly record struct SupplyShock(
    string CellId, string Tag, ShockKind Kind, int StartDay, int Days)
{
    /// <summary>Whether the window covers this day. Half-open: a 3-day shock beginning on day 10 runs
    /// on 10, 11 and 12, so <see cref="DaysLeft"/> can say "1 more day" on the last one.</summary>
    public bool ActiveOn(int day) => day >= StartDay && day < StartDay + Days;

    /// <summary>Days including today, so the notice can say "for 1 more day" rather than "for 0".</summary>
    public int DaysLeft(int day) => Math.Max(0, StartDay + Days - day);
}

/// <summary>
/// Caravan events and supply shocks (Phase 38T) — the timed half of 38G's demand table. Pure and
/// Godot-free like <see cref="RegionDemand"/>, <see cref="HaggleRules"/> and <see cref="WagerRules"/>,
/// for the same reason: the test project cannot construct a Godot object.
///
/// ⚠️ <b>A SHOCK IS A TEMPORARY TAG ON A CELL, NOT A NEW MULTIPLIER, AND EVERYTHING ELSE FOLLOWS FROM
/// THAT.</b> It does not scale a price, a markup or a fraction — it edits which of the two lists the
/// cell wears today and hands them to <see cref="RegionDemand.ValueAt"/> unchanged. So the 38A clamps,
/// the specialty premium, the standing ramp, a haggle and 38F's
/// <c>NoCombinationOfMultipliersLetsSellingBeatBuying</c> sweep all cover it already: there is no new
/// factor for them to be re-derived against, and 38G's structural symmetry (a value has no sides)
/// carries over untouched. That is the answer to invariant 4's question — *what is this a spread over?*
/// — being **nothing; it is not a spread at all.**
///
/// ⚠️ <b>What it DOES move is the worst case, and that is where the work went.</b> A cell that authored
/// no demand can now wear one, so every rule measured against "what the best buyer pays" — a contract
/// reward, a commission fee, a broker's cut — has to hold *during* a shock. `ContentValidator` asks
/// those existing rules at <see cref="PriceView.Peak"/> and <see cref="PriceView.Trough"/> rather than
/// gaining new rules about shocks (38S's carried lesson: an existing rule asked a harder question beats
/// a new rule that duplicates its band and then drifts from it).
///
/// ⚠️ <b>The roll is derived from the day; the window is stored.</b> <see cref="Begins"/> is a pure
/// function of (day, cell), so nothing here can be rerolled by a quickload — but a shortage can be
/// *relieved* early by the player hauling goods in, and that is a fact no clock can derive. The window
/// therefore lives in <see cref="SupplyShockService"/>'s save. Same division of labour as
/// <c>WagerLedger</c> and <c>HaggleLedger</c>, one step further along: derive the offer, store what the
/// player did about it.
/// </summary>
public static class SupplyShockRules
{
    /// <summary>Chance in 100 that a shock begins at one cell on one day. Low: the point of a shock is
    /// that the price landscape the player learned is usually the one they are trading in.</summary>
    public const int BeginPercent = 18;

    /// <summary>Shortest and longest a shock runs. Bounded in the type rather than in authored data —
    /// the gate says "a bounded number of days", and a duration authored per cell would be a number
    /// nobody could tune without replaying a week.</summary>
    public const int MinDays = 2;
    public const int MaxDays = 5;

    /// <summary>Units of the shocked goods that must reach the cell to break a shortage early. One
    /// cart's worth: enough that it is a haul, few enough that it is worth making.</summary>
    public const int ReliefUnits = 12;

    private const int BeginSalt = 0x38_7A;
    private const int KindSalt = 0x38_7B;
    private const int TagSalt = 0x38_7C;
    private const int DaysSalt = 0x38_7D;

    /// <summary>Whether a shock begins at this cell on this day. Pure — the same day and cell give the
    /// same answer on any machine, in any save (<see cref="StableRoll"/> says why that is not
    /// <c>string.GetHashCode()</c>).</summary>
    public static bool Begins(int day, string cellId) =>
        StableRoll.Percent(day, BeginSalt, cellId) < BeginPercent;

    /// <summary>How long the shock that begins on this day runs, inside the bounds above.</summary>
    public static int Duration(int day, string cellId) =>
        MinDays + (int)(StableRoll.Percent(day, DaysSalt, cellId) % (uint)(MaxDays - MinDays + 1));

    /// <summary>
    /// The shock beginning at this cell today, or <c>null</c> for none.
    ///
    /// ⚠️ <b>A shock that would change no price is not rolled at all.</b> The tag is picked from the
    /// candidates whose authored state the shock would actually invert — a glut of ore at the mine, where
    /// ore is already a surplus, is a notice on the board announcing that nothing has happened. That
    /// filter is why the kind is rolled first and the tag second: with the kind fixed, "which candidates
    /// are visible" is a plain question with a deterministic answer.
    /// </summary>
    public static SupplyShock? Roll(
        int day,
        string cellId,
        IReadOnlyList<string> candidates,
        IReadOnlyList<string> surplus,
        IReadOnlyList<string> demand)
    {
        if (string.IsNullOrEmpty(cellId) || candidates.Count == 0 || !Begins(day, cellId))
        {
            return null;
        }

        uint kindRoll = StableRoll.Percent(day, KindSalt, cellId);
        ShockKind kind = kindRoll < 45 ? ShockKind.Shortage : kindRoll < 85 ? ShockKind.Glut : ShockKind.Fair;

        if (kind == ShockKind.Fair)
        {
            // A fair floods the stalls, so it is only an event where something is not already cheap.
            return AnyOutside(candidates, surplus)
                ? new SupplyShock(cellId, string.Empty, kind, day, Duration(day, cellId))
                : null;
        }

        // Shortage makes a tag dear, so it needs one that is not already in demand; a glut is the mirror.
        var visible = new List<string>();
        IReadOnlyList<string> already = kind == ShockKind.Shortage ? demand : surplus;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!Contains(already, candidates[i]))
            {
                visible.Add(candidates[i]);
            }
        }

        if (visible.Count == 0)
        {
            return null;
        }

        int index = (int)(StableRoll.Percent(day, TagSalt, cellId) % (uint)visible.Count);

        return new SupplyShock(cellId, visible[index], kind, day, Duration(day, cellId));
    }

    /// <summary>
    /// The two lists a cell wears today: its authored surplus and demand with every active shock applied.
    ///
    /// ⚠️ <b>A shock removes its tag from the opposite list before adding it to its own</b>, or the cell
    /// would author it in both and <see cref="RegionDemand.ValueAt"/> would silently resolve it as a
    /// surplus — which is the exact authoring nonsense <c>--validate</c> refuses in a <c>.tres</c>, and
    /// it would be indistinguishable from a shock doing nothing.
    /// </summary>
    public static (List<string> Surplus, List<string> Demand) Apply(
        IReadOnlyList<string> surplus,
        IReadOnlyList<string> demand,
        IReadOnlyList<string> candidates,
        IReadOnlyList<SupplyShock> active,
        int day)
    {
        var liveSurplus = new List<string>(surplus);
        var liveDemand = new List<string>(demand);

        for (int i = 0; i < active.Count; i++)
        {
            SupplyShock shock = active[i];
            if (!shock.ActiveOn(day))
            {
                continue;
            }

            switch (shock.Kind)
            {
                case ShockKind.Shortage:
                    Move(shock.Tag, from: liveSurplus, to: liveDemand);
                    break;

                case ShockKind.Glut:
                    Move(shock.Tag, from: liveDemand, to: liveSurplus);
                    break;

                case ShockKind.Fair:
                    for (int t = 0; t < candidates.Count; t++)
                    {
                        Move(candidates[t], from: liveDemand, to: liveSurplus);
                    }

                    break;
            }
        }

        return (liveSurplus, liveDemand);
    }

    /// <summary>
    /// Whether a sale into a shocked settlement counts against its shortage, and whether this delivery
    /// is the one that breaks it. Split out of <see cref="SupplyShockService.Deliver"/> so the decision
    /// is testable: the service around it is a Node, and a Node cannot be constructed in the test
    /// project — nor called from the GDScript harness, whose reach stops at Variant-compatible
    /// signatures.
    ///
    /// ⚠️ <b>A glut and a fair are not relievable</b>, and neither is a tag the shock is not about.
    /// Hauling more of a good into a place already drowning in it would pay the player twice for one
    /// cart: once at the counter, once for "fixing" the price they were selling into.
    /// </summary>
    public static bool Relieves(
        SupplyShock shock, IReadOnlyList<string> itemTags, int hauled, int units, out bool breaks)
    {
        breaks = false;

        if (shock.Kind != ShockKind.Shortage || units <= 0 || !Contains(itemTags, shock.Tag))
        {
            return false;
        }

        breaks = hauled + units >= ReliefUnits;

        return true;
    }

    /// <summary>
    /// The cell's lists as a validator must read them: <see cref="PriceView.Peak"/> is every candidate
    /// in demand at once (the most any buyer could ever pay here), <see cref="PriceView.Trough"/> the
    /// mirror.
    ///
    /// ⚠️ <b>This is deliberately worse than any shock the game can actually roll</b> — one shock runs at
    /// a cell at a time and a fair only moves one direction. A rule proved against this band cannot be
    /// broken by a rotation of shocks nobody thought to simulate, which is the whole reason the check is
    /// a band rather than a replay.
    /// </summary>
    public static (List<string> Surplus, List<string> Demand) Extremes(
        IReadOnlyList<string> surplus,
        IReadOnlyList<string> demand,
        IReadOnlyList<string> candidates,
        PriceView view)
    {
        var liveSurplus = new List<string>(surplus);
        var liveDemand = new List<string>(demand);

        for (int i = 0; i < candidates.Count; i++)
        {
            if (view == PriceView.Peak)
            {
                Move(candidates[i], from: liveSurplus, to: liveDemand);
            }
            else
            {
                Move(candidates[i], from: liveDemand, to: liveSurplus);
            }
        }

        return (liveSurplus, liveDemand);
    }

    private static void Move(string tag, List<string> from, List<string> to)
    {
        if (string.IsNullOrEmpty(tag))
        {
            return;
        }

        from.Remove(tag);
        if (!to.Contains(tag))
        {
            to.Add(tag);
        }
    }

    private static bool AnyOutside(IReadOnlyList<string> candidates, IReadOnlyList<string> list)
    {
        for (int i = 0; i < candidates.Count; i++)
        {
            if (!Contains(list, candidates[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(IReadOnlyList<string> list, string tag)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], tag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Which day's prices a caller is asking about (Phase 38T). <see cref="Today"/> is what the player is
/// charged; the other two are the band a shock can move a cell through and exist for
/// <c>ContentValidator</c>, which has to prove a rule holds on the worst day rather than on this one.
/// </summary>
public enum PriceView
{
    /// <summary>What this counter charges right now — the only view any live transaction uses.</summary>
    Today = 0,

    /// <summary>The most this place could ever value a good at: every shock candidate in demand.</summary>
    Peak = 1,

    /// <summary>The least this place could ever value a good at: every shock candidate a surplus.</summary>
    Trough = 2,
}

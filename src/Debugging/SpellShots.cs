using Embervale.Core.Diagnostics;

namespace Embervale.Debugging;

/// <summary>
/// The spell screenshot harness: <c>godot --path . -- --spellshots</c>. For each spell it is to
/// stage a caster and a target and capture the cast, the travel or impact and what lingers, in third
/// person and for a few spells per school in first; <c>EMBERVALE_SPELLSHOTS_FILTER</c> narrows the
/// run by spell id or school. Run WITHOUT <c>--headless</c>.
///
/// <para>Seam: the shot list is empty. The base harness treats an empty list as a failed run, so
/// until the first <c>Shot</c> is registered this says so and exits 0; the <c>_Ready</c> override
/// goes when the list is filled.</para>
/// </summary>
public sealed partial class SpellShots : ShotHarness
{
    /// <summary>Narrows the run to one spell id or one school.</summary>
    public const string FilterVariable = "EMBERVALE_SPELLSHOTS_FILTER";

    protected override string Flag => "--spellshots";

    protected override string OutputDir => "user://spell_shots";

    protected override void BuildShotList()
    {
    }

    public override void _Ready()
    {
        Log.Info($"{Flag}: no shots are built yet; nothing to capture.");
        GetTree().Quit(0);
    }
}

using Embervale.Core.Diagnostics;
using Embervale.Core.Services;
using Godot;

namespace Embervale.World;

/// <summary>
/// Marks where a <see cref="MapLocationResource"/> actually stands (Phase 39.5A). Placed in a cell
/// scene, <b>parented to the stall, counter or building it names</b>, and carrying nothing but the
/// id — its own transform is the location's position.
///
/// ⚠️ <b>Parent it to the thing, not to the cell root.</b> That is what makes the map incapable of
/// drifting: nudge a market stall and its pin moves with it, because the pin is a child of the
/// stall. A marker parented to the cell root with a hand-copied offset is a second copy of the
/// stall's position, and invariant 22 already records what that costs — a schedule holds a copy of
/// its cell's <c>Center</c>, and moving a cell is therefore never a one-line edit.
///
/// Mirrors <see cref="TravelNodeComponent"/>'s "authored where it sits, not in a database" rule.
/// Unlike that one this is not interactable and has no prompt: it is a pin, not a thing you press
/// <c>E</c> on. It does no per-frame work — it registers once and is done.
/// </summary>
[GlobalClass]
public partial class MapLocationComponent : Node3D
{
    /// <summary>The <c>location.*</c> id this marks. Cross-checked against
    /// <see cref="MapLocationDatabase"/> by <c>--validate</c>, which scans cell scenes for exactly
    /// this property — so unlike a <c>VendorComponent.ShopId</c>, a typo here is caught.</summary>
    [Export] public string LocationId { get; set; } = string.Empty;

    public override void _Ready()
    {
        if (string.IsNullOrEmpty(LocationId))
        {
            Log.Warn($"MapLocationComponent at '{GetPath()}' has no LocationId; not registered.");
            return;
        }

        if (ServiceLocator.Instance is not { } locator || !locator.TryGet(out MapService map))
        {
            // Not an error worth shouting about: a cell scene opened on its own in the editor, or
            // instantiated by a tools/ harness, has no MapService and does not need one.
            return;
        }

        map.RegisterLocation(LocationId, GlobalPosition);
    }
}

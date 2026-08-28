using Embervale.Core.Diagnostics;
using Embervale.Entities;
using Embervale.Interaction;
using Embervale.Localization;
using Godot;

namespace Embervale.Shrines;

/// <summary>In-world shrine caller. It names one authored <c>shrine.*</c> resource and routes the
/// interaction into the player's <see cref="BlessingComponent"/> rather than keeping local state.
/// That makes several future shrine placements naturally share the same one-visit rule.</summary>
[GlobalClass]
public partial class ShrineComponent : InteractableComponent
{
    /// <summary>The authored <c>shrine.*</c> blessing id to offer.</summary>
    [Export] public string ShrineId { get; set; } = string.Empty;

    private ShrineResource? Shrine => ShrineDatabase.Get(ShrineId);

    public override string Prompt => Shrine is { } shrine
        ? Loc.TF("interact.shrine.pray", Loc.T(shrine.NameKey))
        : Loc.T("interact.shrine.unavailable");

    public override void Interact(IEntity instigator)
    {
        ShrineResource? shrine = Shrine;
        BlessingComponent? blessings = instigator.GetComponent<BlessingComponent>();
        if (shrine == null || blessings == null)
        {
            Log.Warn($"Shrine '{ShrineId}' could not offer a blessing: resource or player component missing.");
            return;
        }

        // The body decides nothing and announces nothing: BlessingComponent owns the corruption
        // gate, the claim and all three announcements, so every shrine placement behaves alike.
        blessings.Offer(shrine);
    }
}

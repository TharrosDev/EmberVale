using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Dialogue;
using Embervale.Entities;
using Embervale.Player;
using Godot;

namespace Embervale.World;

/// <summary>
/// Removes a placed actor from the live world once a persistent story flag is set (41E).
///
/// This is intentionally a presentation of existing saved state, not an <see cref="Embervale.Save.ISaveable"/>:
/// the flag is the sole authority and a load replaces it wholesale. It hides collision as well as
/// visuals, so an NPC who has left cannot leave a ghost interaction prompt behind.
/// </summary>
[GlobalClass]
public partial class FlagVisibilityComponent : EntityComponent
{
    /// <summary>When this player flag is set, hide this actor. Empty leaves it present.</summary>
    [Export] public string HiddenWhenFlagId { get; set; } = string.Empty;

    private readonly Dictionary<CollisionObject3D, uint> _hiddenLayers = new();

    protected override void OnInitialize()
    {
        EventBus.Instance?.Subscribe<StoryFlagChangedEvent>(OnFlagChanged);
        EventBus.Instance?.Subscribe<GameLoadedEvent>(OnGameLoaded);
        Refresh();
    }

    protected override void OnTeardown()
    {
        EventBus.Instance?.Unsubscribe<StoryFlagChangedEvent>(OnFlagChanged);
        EventBus.Instance?.Unsubscribe<GameLoadedEvent>(OnGameLoaded);
    }

    private void OnFlagChanged(StoryFlagChangedEvent e)
    {
        if (e.Flag == HiddenWhenFlagId)
        {
            Refresh();
        }
    }

    // Flags load as one replacement collection, without individual changed events.
    private void OnGameLoaded(GameLoadedEvent e) => Refresh();

    private void Refresh()
    {
        bool hasFlag = ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player) &&
            player.GetComponent<StoryFlagsComponent>()?.Has(HiddenWhenFlagId) == true;
        SetPresent(!FlagVisibilityRules.ShouldHide(HiddenWhenFlagId, hasFlag));
    }

    private void SetPresent(bool present)
    {
        if (Entity?.Body is not Node3D body || !IsInstanceValid(body))
        {
            return;
        }

        body.Visible = present;
        foreach (Node child in body.GetChildren())
        {
            if (child is not CollisionObject3D collider)
            {
                continue;
            }

            if (present)
            {
                if (_hiddenLayers.TryGetValue(collider, out uint layer))
                {
                    collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, layer);
                    _hiddenLayers.Remove(collider);
                }
            }
            else if (collider.CollisionLayer != 0u)
            {
                _hiddenLayers[collider] = collider.CollisionLayer;
                collider.SetDeferred(CollisionObject3D.PropertyName.CollisionLayer, 0u);
            }
        }
    }
}

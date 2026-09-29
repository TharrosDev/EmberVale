using Embervale.Core.Services;
using Embervale.Entities;
using Embervale.Player;

namespace Embervale.Combat;

/// <summary>
/// Whose side a piece of combat presentation is told from. Single-player: only what the player deals
/// or takes freezes the world, flashes the screen or floats a number, so two NPCs trading blows never
/// do. One helper rather than the four private copies the overlays used to carry.
/// </summary>
public static class CombatPerspective
{
    /// <summary>Whether <paramref name="entity"/> is the player. With no player registered (a probe, a
    /// scene built by hand) the player's team stands in, so the presentation still runs.</summary>
    public static bool IsPlayer(IEntity? entity)
    {
        if (entity == null)
        {
            return false;
        }

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out PlayerCharacter player))
        {
            return ReferenceEquals(entity, player);
        }

        return entity.GetComponent<CombatComponent>() is { Team: 0 };
    }
}

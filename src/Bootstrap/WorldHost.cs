using Embervale.Core.Services;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// The loaded world, and the owner of every service that only means anything while one is loaded:
/// the region streamer, weather, the sky, the encounter and world-event directors, and the portals
/// a region puts in the player's way.
///
/// <para><b>A region transition does not destroy this.</b> The streamer reconfigures in place —
/// unload the old cells, configure the new region, rebuild portals and safe zones — because that is
/// what the game has always done and weather that resets at every doorway would be a behaviour
/// change, not a refactor. World lifetime ends when the session's world is torn down.</para>
/// </summary>
public sealed partial class WorldHost : Node3D, IServiceScopeHost
{
    private ServiceScope? _scope;

    public ServiceScope Scope => _scope ??= new ServiceScope(ServiceLifetime.World);

    public override void _EnterTree()
    {
        Name = "WorldHost";
        _ = Scope;
    }

    public override void _ExitTree()
    {
        _scope?.Dispose();
        _scope = null;
    }
}

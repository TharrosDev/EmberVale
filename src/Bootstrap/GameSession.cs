using Embervale.Core.Services;
using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// One playthrough's runtime, and the owner of every service whose lifetime is a save file's:
/// the clock, autosave, the ledgers, map discovery, the party, persistence directors.
///
/// <para>It exists because those services used to be children of the bootstrap, which is to say
/// children of the process. There was no moment at which a session ended — quitting to the title
/// reloaded the whole scene, and the only reason that worked is that it threw the process's entire
/// scene tree away. <b>Freeing this node ends a session</b>, and the scope it hosts takes the
/// registrations with it, so the next New Game starts from nothing without a reload.</para>
///
/// <para>It is a <see cref="Node3D"/> rather than a <see cref="Node"/> so the 3D actors parented
/// under it keep the identical (identity) transform chain they had under the bootstrap.</para>
/// </summary>
public sealed partial class GameSession : Node3D, IServiceScopeHost
{
    private ServiceScope? _scope;

    public ServiceScope Scope => _scope ??= new ServiceScope(ServiceLifetime.Session);

    /// <summary>The world this session has loaded. Freed and rebuilt independently of the session,
    /// which is what makes "unload the world, keep the save" expressible.</summary>
    public WorldHost World { get; private set; } = null!;

    public override void _EnterTree()
    {
        Name = "GameSession";
        _ = Scope; // open the scope before any child registers into it

        World = new WorldHost();
        AddChild(World);
    }

    public override void _ExitTree()
    {
        _scope?.Dispose();
        _scope = null;
    }
}

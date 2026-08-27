using Godot;

namespace Embervale.Bootstrap;

/// <summary>
/// Tiny scene-preview seam for tools that load authored cells without <see cref="GameBootstrap"/>.
/// Production still initializes through the bootstrap; QA harnesses add this node before a streamer
/// so they exercise the identical centralized database order instead of seeing false registry warnings.
/// </summary>
[GlobalClass]
public partial class ContentDatabaseLoader : Node
{
    public override void _EnterTree() => ContentDatabases.InitializeAll();
}

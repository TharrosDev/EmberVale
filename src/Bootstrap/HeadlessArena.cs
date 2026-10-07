namespace Embervale.Bootstrap;

/// <summary>
/// <c>--arena</c>: the fight simulator's entry point. The simulator itself (<c>ArenaRunner</c>) is
/// development tooling and is not compiled into a shipping build, so this is the one name
/// <see cref="ApplicationRoot"/> can always reference.
/// <code>godot --headless --fixed-fps 60 --path . -- --arena=enemy.goblin --trials=5</code>
/// </summary>
public static class HeadlessArena
{
    public const string FlagArgument = "--arena";

    /// <summary>Starts the simulator when <see cref="FlagArgument"/> was passed and this is a
    /// tooling build. Returns true when it took the run over.</summary>
    public static bool RunIfRequested(ApplicationRoot root, SessionLifecycleCoordinator lifecycle)
    {
#if EMBERVALE_TOOLING
        if (HeadlessValidation.HasFlag(FlagArgument))
        {
            ArenaRunner.Run(root, lifecycle);
            return true;
        }
#endif
        return false;
    }
}

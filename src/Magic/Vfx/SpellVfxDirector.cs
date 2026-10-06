using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Settings;
using Godot;

namespace Embervale.Magic.Vfx;

/// <summary>
/// The session's owner of spell effects. Everything <see cref="SpellVfx"/> draws is a child of
/// <see cref="VfxRoot"/> and never of an actor's body, so an effect outlives the swing that made it
/// and dies with the session. It keeps <see cref="VfxQuality"/> in step with the settings and clears
/// every live effect when a load begins.
///
/// <para>On a headless display it still exists and still binds, but <see cref="Enabled"/> is false
/// and the facade draws nothing.</para>
/// </summary>
public partial class SpellVfxDirector : Node
{
    /// <summary>The parent of every spell effect.</summary>
    public Node3D VfxRoot { get; }

    /// <summary>False on a headless display: nothing is drawn, so nothing is built.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Builds <see cref="VfxRoot"/> here rather than in <c>_Ready</c>, so it exists for any
    /// caller that reaches the director before the tree has readied it.</summary>
    public SpellVfxDirector()
    {
        VfxRoot = new Node3D { Name = "VfxRoot" };
        AddChild(VfxRoot);
    }

    public override void _Ready()
    {
        Enabled = DisplayServer.GetName() != "headless";

        // The plain flashes are drawn under VfxRoot and nowhere else, so their pool is this node's.
        SpellFlash.OpenPool();
        SpellVfx.Bind(this);

        if (ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings))
        {
            ApplySettings(settings.Current);
        }

        EventBus? bus = EventBus.Instance;
        bus?.Subscribe<SettingsAppliedEvent>(OnSettingsApplied);
        bus?.Subscribe<GameLoadingEvent>(OnGameLoading);
    }

    public override void _ExitTree()
    {
        EventBus? bus = EventBus.Instance;
        bus?.Unsubscribe<SettingsAppliedEvent>(OnSettingsApplied);
        bus?.Unsubscribe<GameLoadingEvent>(OnGameLoading);
        SpellVfx.Clear();
        SpellVfx.Unbind(this);
        SpellFlash.ClosePool();
    }

    /// <summary>Frees every live effect. An effect belongs to the timeline that made it, and a load
    /// abandons that timeline.</summary>
    public void KillAll()
    {
        for (int i = VfxRoot.GetChildCount() - 1; i >= 0; i--)
        {
            VfxRoot.GetChild(i).QueueFree();
        }

        SpellVfx.Clear();
    }

    private void OnSettingsApplied(SettingsAppliedEvent e) => ApplySettings(e.Current);

    private void OnGameLoading(GameLoadingEvent _) => KillAll();

    private static void ApplySettings(Settings.Settings settings) =>
        VfxQuality.Apply(settings.SpellEffects, settings.RenderQuality, settings.ReducedMotion);
}

using Embervale.Combat;
using Embervale.Core;
using Embervale.Core.Events;
using Embervale.Core.Services;
using Embervale.Settings;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The player's HUD options: scale, opacity, safe zone and a mode per element.
///
/// Applied when the HUD is built and whenever settings are applied. The per-element modes are the
/// second of two gates: <see cref="HudVisibility"/> says what the HUD mode allows and
/// <see cref="HudDynamicRules"/> says what the player wants, and an element shows only when both
/// agree. With every option at its default (all Always, scale 1, opacity 1, no safe zone) nothing
/// here changes what the HUD does.
///
/// A widget that writes its own <c>Visible</c> reads <see cref="Shows"/> at that write, so two
/// owners never fight over one flag. An element whose content just changed calls
/// <see cref="MarkChanged"/> so that a Dynamic element comes up for it.
/// </summary>
public partial class GameHud
{
    private readonly HudElementMode[] _elementModes = new HudElementMode[HudPresets.ElementCount];
    private readonly bool[] _elementShown = new bool[HudPresets.ElementCount];
    private readonly double[] _elementChangedAt = new double[HudPresets.ElementCount];

    // No element is Dynamic by default, and then the per-frame resolve below has nothing to do.
    private bool _anyDynamic;
    private double _combatAt = double.NegativeInfinity;

    private Crosshair _crosshair = null!;
    private PanelContainer _vitalsPanel = null!;

    /// <summary>Whether the player's options allow <paramref name="element"/> right now. The HUD
    /// mode is not part of the answer: the slot a widget sits in already carries that.</summary>
    public bool Shows(HudElement element) => _elementShown[(int)element];

    /// <summary>Notes that <paramref name="element"/>'s content just changed, which brings it up for
    /// a few seconds when it is set to Dynamic.</summary>
    public void MarkChanged(HudElement element) => _elementChangedAt[(int)element] = Now();

    /// <summary>The width the scaled HUD lays out in, for <see cref="HudMetrics"/>.</summary>
    public float LayoutWidth => _layout.Scaled.Size.X;

    /// <summary>The saved mode of <paramref name="element"/>, for a HUD element that lives outside
    /// this class (damage numbers, enemy plates, toasts, subtitles). Always before settings exist.</summary>
    public static HudElementMode ElementMode(HudElement element) =>
        ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService settings)
            ? (HudElementMode)SettingsMath.HudElementMode(settings.Current.HudElementModes, (int)element)
            : HudElementMode.Always;

    private static double Now() => Time.GetTicksMsec() / 1000.0;

    private void ReadyOptions()
    {
        System.Array.Fill(_elementShown, true);
        System.Array.Fill(_elementChangedAt, double.NegativeInfinity);
        _layout.Scaled.Resized += ApplyMetrics;
        EventBus.Instance?.Subscribe<HitConfirmedEvent>(OnHitForOptions);
        ApplyOptions();
    }

    private void ExitOptions() => EventBus.Instance?.Unsubscribe<HitConfirmedEvent>(OnHitForOptions);

    private void OnHitForOptions(HitConfirmedEvent e)
    {
        if (e.ByPlayer || e.OnPlayer)
        {
            _combatAt = Now();
        }
    }

    /// <summary>Reads the HUD options and applies them. Settings are applied on every tick of a
    /// slider being dragged, so each step here writes only what changed.</summary>
    private void ApplyOptions()
    {
        Settings.Settings? settings =
            ServiceLocator.Instance is { } locator && locator.TryGet(out SettingsService service)
                ? service.Current
                : null;

        _layout.ApplyScale(settings?.HudScale ?? 1f);
        _layout.ApplySafeZone(settings?.HudSafeZone ?? 0f);
        _layout.ApplyOpacity(settings?.HudOpacity ?? 1f);
        ApplyMetrics();

        _anyDynamic = false;
        bool changed = false;
        for (int i = 0; i < _elementModes.Length; i++)
        {
            var mode = (HudElementMode)SettingsMath.HudElementMode(settings?.HudElementModes, i);
            _elementModes[i] = mode;
            _anyDynamic |= mode == HudElementMode.Dynamic;

            // Always and Hidden are settled here; a Dynamic element starts shown and the next tick
            // resolves it, so a mode change never blanks an element for a frame.
            bool shown = mode != HudElementMode.Hidden;
            changed |= shown != _elementShown[i];
            _elementShown[i] = shown;
        }

        if (changed)
        {
            ApplyElementVisibility();
        }

        InvalidateShown();
    }

    /// <summary>Widths that follow the layout: the two cards this class builds. The compass and the
    /// boss bar size themselves from <see cref="HudMetrics"/> and <see cref="LayoutWidth"/>.</summary>
    private void ApplyMetrics()
    {
        float width = LayoutWidth;
        if (width <= 0f)
        {
            return; // not laid out yet; Resized calls back when it is
        }

        SetMinimumWidth(_vitalsPanel, HudMetrics.VitalsWidth(width));
        SetMinimumWidth(_questPanel, HudMetrics.TrackerWidth(width));
    }

    private static void SetMinimumWidth(Control control, float width)
    {
        if (control.CustomMinimumSize.X != width)
        {
            control.CustomMinimumSize = new Vector2(width, control.CustomMinimumSize.Y);
        }
    }

    /// <summary>Resolves the Dynamic elements against what the game is doing, once a frame, and
    /// rewrites visibility only on the frame an answer changes.</summary>
    private void UpdateElements()
    {
        if (!_anyDynamic)
        {
            return;
        }

        double now = Now();
        bool inCombat = HudDynamicRules.Lingering(now, _combatAt, HudDynamicRules.CombatLingerSeconds)
            || _bossFrame.Visible
            || (_player is Node node && IsInstanceValid(node)
                && _player.GetComponent<LockOnComponent>() is { Target: not null });
        bool recall = Input.IsActionPressed(UiLive.HudRecall);
        bool menuOpen = _mode == HudMode.Menu;
        bool belowMax = VitalsBelowMax();

        bool changed = false;
        for (int i = 0; i < _elementModes.Length; i++)
        {
            if (_elementModes[i] != HudElementMode.Dynamic)
            {
                continue;
            }

            var signals = new HudSignals(
                inCombat,
                belowMax,
                HudDynamicRules.Lingering(now, _elementChangedAt[i], HudDynamicRules.ChangeLingerSeconds),
                recall,
                menuOpen);
            bool shown = HudDynamicRules.Visible((HudElement)i, HudElementMode.Dynamic, signals);
            changed |= shown != _elementShown[i];
            _elementShown[i] = shown;
        }

        if (changed)
        {
            ApplyElementVisibility();
        }
    }

    private bool VitalsBelowMax()
    {
        if (_player is not Node node || !IsInstanceValid(node) ||
            !_player.TryGetComponent(out StatsComponent stats))
        {
            return false;
        }

        const float Full = 0.999f;
        return stats.GetNormalized(StatType.Health) < Full
            || stats.GetNormalized(StatType.Stamina) < Full
            || stats.GetNormalized(StatType.Mana) < Full;
    }

    /// <summary>
    /// Writes the slot and widget visibility for the current HUD mode and element answers. Called
    /// when either changes, never per frame.
    ///
    /// A slot that holds one element takes that element's answer directly. The compass, the target
    /// plate and the prompt share a slot with other widgets and write their own <c>Visible</c>, so
    /// they read <see cref="Shows"/> where they do it. The party strip shares the vitals' slot and
    /// also hides itself, so it is handed the answer (<see cref="PartyWidget.Allowed"/>).
    /// </summary>
    private void ApplyElementVisibility()
    {
        HudMode mode = _mode;
        bool vitals = HudVisibility.ShowsVitals(mode);
        bool navigation = HudVisibility.ShowsNavigation(mode);

        // vitals, spell, status, party
        _layout.BottomLeft.Visible = vitals && (Shows(HudElement.Vitals) || Shows(HudElement.Party));
        _vitalsPanel.Visible = Shows(HudElement.Vitals);
        if (_party != null)
        {
            _party.Allowed = Shows(HudElement.Party);
        }

        // The hotbar rides with the vitals rather than the rest: assigning a quick-use slot is done
        // from inside the inventory (its own 1–5 buttons), and doing that with the bar you are
        // assigning to hidden is working blind.
        _layout.BottomDock.Visible = vitals && Shows(HudElement.Hotbar);            // quick-use hotbar
        _layout.TopLeft.Visible = navigation && Shows(HudElement.Clock);            // clock + weather
        _layout.TopRight.Visible = navigation && Shows(HudElement.QuestTracker) && !_trackerYields; // quest tracker
        _layout.TopCenter.Visible = HudVisibility.ShowsTopCentre(mode);             // boss survives cinematic locks
        _layout.BottomRight.Visible = navigation && Shows(HudElement.Minimap);      // minimap
        _layout.BottomCenter.Visible = HudVisibility.ShowsPrompt(mode);             // interaction prompt, tutorial hint
        _layout.Overlay.Visible = HudVisibility.ShowsHud(mode);                     // crosshair, vignette, reticle, arcs
        _crosshair.Visible = Shows(HudElement.Crosshair);
    }
}

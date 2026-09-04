using Embervale.Combat;
using Embervale.Entities;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The aimed-at target's nameplate (Phase 18; lifted out of <c>GameHud</c> in 37.5B). Name,
/// health, and — new in 37.5B — a **disposition spine**: a coloured left edge saying whether the
/// thing you are looking at wants to kill you. The Frostfang clans and the Ancient dragon are
/// neutral-until-provoked, so "is this hostile?" stopped being answerable from the model alone
/// the moment Phase 34.5 landed, and the HUD never said.
///
/// It does **not** know how the target was chosen. Lock-on priority lives in <c>GameHud</c>
/// beside the <c>InteractionSensor</c> that owns it; this widget is told what to show.
/// </summary>
public partial class Nameplate : PanelContainer
{
    private Label _name = null!;
    private JuicedBar _bar = null!;
    private StyleBoxFlat _frame = null!;

    /// <summary>The last subject shown, so the health bar can snap rather than animate when the
    /// player's aim crosses from one target to another.</summary>
    private IEntity? _last;

    public Nameplate()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        CustomMinimumSize = new Vector2(200, 0);
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        _frame = UiTheme.CardStyle(UiTheme.Neutral);
        AddThemeStyleboxOverride("panel", _frame);

        MarginContainer pad = UiTheme.Padding(UiTheme.SpaceSm);
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 3);

        _name = UiTheme.Body("");
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_name);

        _bar = JuicedBar.Create(UiTheme.Health, 180f);
        col.AddChild(_bar);

        pad.AddChild(col);
        AddChild(pad);
    }

    /// <summary>
    /// Shows <paramref name="focus"/>, or hides the plate when there is nothing to show.
    ///
    /// The caller has usually already validated the node, but this re-checks: a focused target can
    /// be freed by a despawn or a save/load rebuild while the reference lingers, and dereferencing
    /// a disposed node throws every frame rather than once (the guard `GameHud` has carried since
    /// Phase 18 — kept here so the widget is safe on its own terms).
    /// </summary>
    public void Show(IEntity? focus, IEntity? player)
    {
        if (focus is not Node node || !IsInstanceValid(node) || ReferenceEquals(focus, player) ||
            focus.GetComponent<StatsComponent>() is not { } stats)
        {
            _last = null;
            Visible = false;
            return;
        }

        _name.Text = focus.DisplayName;

        Color disposition = Disposition(focus, player);
        _name.AddThemeColorOverride("font_color", disposition);
        _frame.BorderColor = disposition;

        double health = stats.GetNormalized(StatType.Health);
        if (!ReferenceEquals(focus, _last))
        {
            // Snap when the subject changes, or the drain lag animates across two different
            // creatures and reads as the first one healing.
            _last = focus;
            _bar.Snap(health);
        }
        else
        {
            _bar.SetTarget(health);
        }

        Visible = true;
    }

    /// <summary>
    /// Reads the target's stance toward the player off the combat teams.
    ///
    /// Teams are the honest source here: an enemy that has not been provoked shares no team with
    /// the player but is not attacking either, and the HUD should say *neutral* rather than
    /// promising safety it cannot guarantee. Anything with no <see cref="CombatComponent"/> at all
    /// is a villager or a prop — friendly, because it cannot fight.
    /// </summary>
    private static Color Disposition(IEntity focus, IEntity? player)
    {
        if (focus.GetComponent<CombatComponent>() is not { } theirs)
        {
            return UiTheme.Friendly;
        }

        if (player?.GetComponent<CombatComponent>() is not { } ours)
        {
            return UiTheme.Neutral;
        }

        return theirs.Team == ours.Team ? UiTheme.Friendly : UiTheme.Hostile;
    }
}

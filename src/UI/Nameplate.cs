using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Entities;
using Embervale.Localization;
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
    private IEntity? _lastCombat;
    private JuicedBar _poise = null!;
    private Label _tag = null!;

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

        // Combat additions: a thin poise bar under the health bar (how close the target is to
        // breaking) and a state tag (staggered, guarding, or what its wind-up asks of you).
        _poise = JuicedBar.Create(new Color(0.62f, 0.66f, 0.72f), 180f);
        _poise.CustomMinimumSize = new Vector2(180f, 5f);
        _poise.Visible = false;
        col.AddChild(_poise);

        _tag = UiTheme.Caption("");
        _tag.HorizontalAlignment = HorizontalAlignment.Center;
        _tag.Visible = false;
        col.AddChild(_tag);

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
            _lastCombat = null;
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

        ShowCombat(focus, changed: !ReferenceEquals(focus, _lastCombat));
        _lastCombat = focus;
        Visible = true;
    }

    /// <summary>The poise bar and the state tag. Poise drains as the target is worn down and refills
    /// on a break; the tag names the moment worth acting on — staggered (press the attack), winding up
    /// (and whether to parry, dodge or step out of a sweep), guarding.</summary>
    private void ShowCombat(IEntity focus, bool changed)
    {
        if (focus.GetComponent<CombatComponent>() is not { } combat)
        {
            _poise.Visible = false;
            _tag.Visible = false;
            return;
        }

        _poise.Visible = true;
        if (changed)
        {
            _poise.Snap(combat.PoiseNormalized);
        }
        else
        {
            _poise.SetTarget(combat.PoiseNormalized);
        }

        string? key = null;
        Color tint = UiTheme.Dim;
        if (combat.IsStaggered)
        {
            key = "combat.feedback.tag_staggered";
            tint = UiTheme.Accent;
        }
        else if (focus.GetComponent<CharacterActionComponent>() is { Phase: ActionPhase.Startup } &&
                 TelegraphClasses.Of(focus) is { } cls)
        {
            key = TelegraphClasses.LabelKey(cls);
            tint = cls switch
            {
                TelegraphClass.Parryable => UiTheme.Accent,
                TelegraphClass.Unblockable => UiTheme.Bad,
                TelegraphClass.Sweep => UiTheme.AccentHot,
                _ => UiTheme.Text,
            };
        }
        else if (combat.IsBlocking)
        {
            key = "combat.feedback.tag_guarding";
        }

        _tag.Visible = key != null;
        if (key != null)
        {
            _tag.Text = Loc.T(key);
            _tag.AddThemeColorOverride("font_color", tint);
        }
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

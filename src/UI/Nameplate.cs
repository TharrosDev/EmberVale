using Embervale.Combat;
using Embervale.Combat.Actions;
using Embervale.Entities;
using Embervale.Localization;
using Embervale.Stats;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The aimed-at target's nameplate (Phase 18; lifted out of <c>GameHud</c> in 37.5B). A HUD plate
/// with keylined bars: name, health, and — new in 37.5B — a **disposition spine**: a coloured left edge saying whether the
/// thing you are looking at wants to kill you. The Frostfang clans and the Ancient dragon are
/// neutral-until-provoked, so "is this hostile?" stopped being answerable from the model alone
/// the moment Phase 34.5 landed, and the HUD never said.
///
/// It does **not** know how the target was chosen. Lock-on priority lives in <c>GameHud</c>
/// beside the <c>InteractionSensor</c> that owns it; this widget is told what to show.
/// </summary>
public partial class Nameplate : PanelContainer
{
    private const float BarWidth = 180f;

    private Label _name = null!;
    private JuicedBar _bar = null!;
    private StyleBoxFlat _frame = null!;

    /// <summary>The last subject shown, so the health bar can snap rather than animate when the
    /// player's aim crosses from one target to another.</summary>
    private IEntity? _last;
    private IEntity? _lastCombat;
    private JuicedBar _poise = null!;
    private Label _tag = null!;

    // What the plate is showing, so a target held in view is not restated every frame: the name,
    // the disposition colour (a stylebox write redraws the frame) and the state tag.
    private string? _nameShown;
    private Color _dispositionShown = new(-1f, -1f, -1f, -1f);
    private string? _tagKeyShown;
    private Color _tagTintShown = new(-1f, -1f, -1f, -1f);

    private static Nameplate? _current;

    /// <summary>Who the plate is naming on screen right now, or null: nothing aimed at, or the plate
    /// hidden by the HUD (the boss frame or an event banner owns the top centre, the element is off).
    /// What the enemy plates ask so they neither double up on a target nor leave it with no bar.</summary>
    public static IEntity? Naming =>
        _current is { } plate && IsInstanceValid(plate) && plate.IsVisibleInTree() ? plate._last : null;

    public override void _EnterTree() => _current = this;

    public override void _ExitTree()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    public Nameplate()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SizeFlagsHorizontal = SizeFlags.ShrinkCenter;

        AddThemeStyleboxOverride("panel", new StyleBoxEmpty());

        // The plate is the HUD's own (a translucent cut with one lit edge), and that one edge is
        // the disposition spine.
        PanelContainer plate = UiTheme.HudPlate(UiTheme.Neutral);
        _frame = UiTheme.HudPlateStyle(UiTheme.Neutral);
        plate.AddThemeStyleboxOverride("panel", _frame);

        var col = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        col.AddThemeConstantOverride("separation", UiTheme.SpaceXs);

        _name = UiTheme.HudInk(UiTheme.Body(""));
        _name.HorizontalAlignment = HorizontalAlignment.Center;
        col.AddChild(_name);

        _bar = JuicedBar.Create(UiTheme.Health, BarWidth);
        _bar.CustomMinimumSize = new Vector2(BarWidth, HudCoreMetrics.BarHeight);
        _bar.Keylined = true;
        _bar.LagChunk = true;
        col.AddChild(_bar);

        // Combat additions: a thin poise bar under the health bar (how close the target is to
        // breaking) and a state tag (staggered, guarding, or what its wind-up asks of you).
        _poise = JuicedBar.Create(UiTheme.Poise, BarWidth);
        _poise.CustomMinimumSize = new Vector2(BarWidth, HudCoreMetrics.BarThinHeight);
        _poise.Keylined = true;
        _poise.Visible = false;
        col.AddChild(_poise);

        _tag = UiTheme.HudInk(UiTheme.Caption(""));
        _tag.HorizontalAlignment = HorizontalAlignment.Center;
        _tag.Visible = false;
        col.AddChild(_tag);

        plate.AddChild(col);
        AddChild(plate);
    }

    /// <summary>Forgets what the plate is showing (locale or colour-vision change), so the next
    /// <see cref="Show"/> rewrites the name, the tag and both colours.</summary>
    public void InvalidateShown()
    {
        _nameShown = null;
        _tagKeyShown = null;
        _dispositionShown = new Color(-1f, -1f, -1f, -1f);
        _tagTintShown = new Color(-1f, -1f, -1f, -1f);
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

        string name = focus.DisplayName;
        if (name != _nameShown)
        {
            _nameShown = name;
            _name.Text = name;
        }

        Color disposition = Disposition(focus, player);
        if (disposition != _dispositionShown)
        {
            _dispositionShown = disposition;
            UiLive.FontColor(_name, disposition);
            _frame.BorderColor = disposition;
        }

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
        if (key != null && (!ReferenceEquals(key, _tagKeyShown) || tint != _tagTintShown))
        {
            if (key != _tagKeyShown)
            {
                _tag.Text = Loc.T(key);
            }

            _tagKeyShown = key;
            _tagTintShown = tint;
            UiLive.FontColor(_tag, tint);
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

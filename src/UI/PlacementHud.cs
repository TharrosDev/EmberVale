using Embervale.Core;
using Embervale.Core.Services;
using Embervale.Housing;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The strip shown while placement mode is on (Phase 37C): what you are holding, how many are left,
/// the key that sets it down, and — the part that matters — <b>why the spot under the cursor is being
/// refused</b>.
///
/// <b>Not modal.</b> The player has to keep walking and aiming to choose a spot, so this opens
/// without <c>UiState</c>'s pause. It still opts into <see cref="CloseOnCancel"/>, which the base
/// pairs with <c>LastCancelCloseFrame</c> so one Esc cancels placement without also opening the
/// pause menu.
///
/// It renders every frame from <see cref="PlacementDirector"/>'s live verdict rather than caching
/// anything, because the ghost's colour and this text are the same fact told two ways and must never
/// disagree.
/// </summary>
public partial class PlacementHud : UiPanel
{
    private Label _title = null!;
    private Label _keys = null!;
    private Label _status = null!;

    private PlacementDirector? _placement;

    protected override bool Modal => false;

    protected override bool CloseOnCancel => true;

    protected override void BuildShell(PanelContainer shell)
    {
        shell.AnchorLeft = 0.5f;
        shell.AnchorRight = 0.5f;
        shell.AnchorTop = 1f;
        shell.AnchorBottom = 1f;
        shell.OffsetLeft = -210;
        shell.OffsetRight = 210;
        shell.OffsetTop = -150;
        shell.OffsetBottom = -60;
        shell.GrowHorizontal = Control.GrowDirection.Both;
        shell.GrowVertical = Control.GrowDirection.Begin;

        MarginContainer margin = UiTheme.Padding(10);
        shell.AddChild(margin);

        var column = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", UiTheme.SpaceXs);
        margin.AddChild(column);

        _title = UiTheme.Header(string.Empty);
        column.AddChild(_title);

        _keys = UiTheme.Caption(string.Empty, UiTheme.Dim);
        column.AddChild(_keys);

        column.AddChild(new HSeparator());

        _status = UiTheme.Body(string.Empty);
        column.AddChild(_status);
    }

    public override void _Process(double delta)
    {
        PlacementDirector? placement = Placement();
        bool active = placement is { Active: true };

        if (active != IsOpen)
        {
            SetOpen(active);
        }

        if (active)
        {
            // Every frame: the verdict changes as the player sweeps the cursor, and a dirty flag
            // would only ever be set every frame anyway.
            MarkDirty();
        }

        base._Process(delta);
    }

    protected override void OnOpenChanged(bool open)
    {
        // Closing the strip is how Esc cancels placement — the base handles the key, this makes it
        // mean something.
        if (!open)
        {
            Placement()?.Cancel();
        }
    }

    protected override void Rebuild()
    {
        if (Placement() is not { Kit: { } kit } placement)
        {
            return;
        }

        _title.Text = Loc.TF("place.holding", kit.DisplayName, placement.Remaining);
        _keys.Text = placement.RemovalTarget != null
            ? Loc.TF("place.keys_remove", GameInput.PromptLabel(GameInput.Place))
            : Loc.TF("place.keys", GameInput.PromptLabel(GameInput.Place));

        if (placement.RemovalTarget is { } target)
        {
            _status.Text = Loc.TF("place.pick_up", target.DisplayName);
            _status.AddThemeColorOverride("font_color", UiTheme.Accent);
            return;
        }

        (string text, Color color) = placement.Outcome switch
        {
            PlacementOutcome.Ok => (Loc.T("place.ok"), UiTheme.Good),
            PlacementOutcome.NotOwned => (Loc.T("place.refuse_not_owned"), UiTheme.Bad),
            PlacementOutcome.NoGround => (Loc.T("place.refuse_no_ground"), UiTheme.Bad),
            PlacementOutcome.Blocked => (Loc.T("place.refuse_blocked"), UiTheme.Bad),
            _ => (Loc.TF("place.refuse_outside", placement.HoldingName), UiTheme.Bad),
        };

        _status.Text = text;
        _status.AddThemeColorOverride("font_color", color);
    }

    private PlacementDirector? Placement()
    {
        if (_placement != null && IsInstanceValid(_placement))
        {
            return _placement;
        }

        _placement = ServiceLocator.Instance is { } locator && locator.TryGet(out PlacementDirector director)
            ? director
            : null;
        return _placement;
    }
}

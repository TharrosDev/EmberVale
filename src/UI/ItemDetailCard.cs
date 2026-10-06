using System;
using System.Collections.Generic;
using Embervale.Core.Events;
using Embervale.Localization;
using Godot;

namespace Embervale.UI;

/// <summary>
/// The plate <c>ItemSlot.Detail</c> returns. The
/// layout is built by <see cref="ItemSlot"/>; this node owns the two things about a detail card that
/// are live after it is built:
///
/// - the flip between the one-item numbers and the side-by-side comparison, on
///   <see cref="ItemSlot.CompareAction"/>. Both views are built up front and one is hidden, so the
///   flip rebuilds nothing and can happen under a focused button elsewhere on the screen;
/// - the footer's glyph and verb pairs, which are snapshots of the bindings and are redrawn when the
///   player changes device or rebinds.
///
/// Because the toggle lives in the card, every screen that shows one (the pack, a merchant, a chest)
/// gets the comparison without wiring anything.
/// </summary>
public sealed partial class ItemDetailCard : PanelContainer
{
    // One press flips the shared preference once, however many cards are on screen to hear it.
    private static ulong _lastToggleFrame = ulong.MaxValue;

    private Control? _single;
    private Control? _pair;
    private HFlowContainer? _footer;
    private string _carry = string.Empty;
    private LegendEntry[] _actions = Array.Empty<LegendEntry>();

    public ItemDetailCard(Color litEdge, int edgeWidth)
    {
        AddThemeStyleboxOverride("panel", UiTheme.PlateStyle(litEdge, edgeWidth));
    }

    /// <summary>Whether there is a worn item to set this one beside.</summary>
    public bool CanCompare => _pair != null;

    /// <summary>Whether the side-by-side view is the one showing.</summary>
    public bool ShowingSideBySide => _pair is { Visible: true };

    internal void SetCompareViews(Control single, Control pair)
    {
        _single = single;
        _pair = pair;
        ApplyCompare();
    }

    internal void SetFooter(HFlowContainer footer, string carry, IReadOnlyList<LegendEntry>? actions)
    {
        _footer = footer;
        _carry = carry;

        var entries = new List<LegendEntry>();
        if (actions != null)
        {
            entries.AddRange(actions);
        }

        if (_pair != null)
        {
            entries.Add(new LegendEntry(ItemSlot.CompareAction, Loc.T("item.detail.compare")));
        }

        _actions = entries.ToArray();
        FillFooter();
    }

    /// <summary>Shows the view the shared preference asks for.</summary>
    public void ApplyCompare()
    {
        if (_single == null || _pair == null)
        {
            return;
        }

        _pair.Visible = ItemSlot.CompareOpen;
        _single.Visible = !ItemSlot.CompareOpen;
    }

    public override void _Ready()
    {
        // Overriding _Input switches it on; a card with nothing to compare has nothing to listen for.
        SetProcessInput(_pair != null);
    }

    public override void _EnterTree()
    {
        EventBus.Instance?.Subscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Subscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    public override void _ExitTree()
    {
        EventBus.Instance?.Unsubscribe<InputDeviceChangedEvent>(OnDeviceChanged);
        EventBus.Instance?.Unsubscribe<InputBindingsChangedEvent>(OnBindingsChanged);
    }

    public override void _Input(InputEvent @event)
    {
        // A card left behind in a closed panel must not answer, and neither must one on a screen
        // where the player is typing a search.
        if (_pair == null || !@event.IsActionPressed(ItemSlot.CompareAction) || @event.IsEcho() || !IsVisibleInTree())
        {
            return;
        }

        if (GetViewport()?.GuiGetFocusOwner() is LineEdit)
        {
            return;
        }

        ulong frame = Engine.GetProcessFrames();
        if (frame != _lastToggleFrame)
        {
            _lastToggleFrame = frame;
            ItemSlot.CompareOpen = !ItemSlot.CompareOpen;
            UiAudio.Play(UiCue.Click);
        }

        ApplyCompare();
    }

    private void OnDeviceChanged(InputDeviceChangedEvent e) => FillFooter();

    private void OnBindingsChanged(InputBindingsChangedEvent e) => FillFooter();

    /// <summary>What it weighs and costs, then each action as its glyph and its verb.</summary>
    private void FillFooter()
    {
        if (_footer == null)
        {
            return;
        }

        UiTheme.ClearChildren(_footer);

        Label carry = UiTheme.Caption(_carry);
        carry.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        _footer.AddChild(carry);

        foreach (LegendEntry entry in _actions)
        {
            var pair = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
            pair.AddThemeConstantOverride("separation", UiTheme.Space2xs);
            pair.AddChild(Centred(UiGlyph.For(entry.Action)));
            if (entry.SecondAction is { } second)
            {
                pair.AddChild(Centred(UiGlyph.For(second)));
            }

            pair.AddChild(Centred(UiTheme.Caption(entry.Label, UiTheme.Text)));
            _footer.AddChild(pair);
        }
    }

    private static Control Centred(Control control)
    {
        control.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        return control;
    }
}

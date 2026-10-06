using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Embervale.Core;
using Embervale.Settings;
using Godot;
using Xunit;

namespace Embervale.Tests;

/// <summary>
/// The remapping rules with no engine: what a binding is saved as, what is read back from a
/// settings file a player may have edited, which inputs can never be bound, and when two actions
/// collide. The input map itself is rewritten in-engine by <c>GameInput.ApplyBindings</c>; a wrong
/// answer here is what would put the wrong key in it.
/// </summary>
public class InputBindingRulesTests
{
    private static readonly InputBinding KeyE = InputBinding.OfKey(Key.E);
    private static readonly InputBinding KeyF = InputBinding.OfKey(Key.F);
    private static readonly InputBinding Space = InputBinding.OfKey(Key.Space);

    // --- The saved form ---------------------------------------------------------------------

    [Theory]
    [InlineData("key:E")]
    [InlineData("key:Space")]
    [InlineData("key:Key1")]
    [InlineData("key:Shift")]
    [InlineData("mouse:Left")]
    [InlineData("mouse:WheelDown")]
    [InlineData("mouse:Xbutton1")]
    [InlineData("joy:A")]
    [InlineData("joy:DpadUp")]
    [InlineData("joy:LeftShoulder")]
    [InlineData("axis:TriggerLeft:+")]
    [InlineData("axis:TriggerRight:+")]
    [InlineData("axis:LeftY:-")]
    [InlineData("none")]
    public void Serialise_RoundTripsThroughParse(string text)
    {
        Assert.True(InputBindingRules.TryParse(text, out InputBinding binding));
        Assert.Equal(text, InputBindingRules.Serialise(binding));
    }

    [Fact]
    public void Serialise_SpellsEachKindWithTheEngineNames()
    {
        Assert.Equal("key:E", InputBindingRules.Serialise(KeyE));
        Assert.Equal("mouse:Right", InputBindingRules.Serialise(InputBinding.OfMouse(MouseButton.Right)));
        Assert.Equal("joy:X", InputBindingRules.Serialise(InputBinding.OfJoy(JoyButton.X)));
        Assert.Equal("axis:TriggerRight:+", InputBindingRules.Serialise(InputBinding.OfAxis(JoyAxis.TriggerRight, 1)));
        Assert.Equal("axis:LeftX:-", InputBindingRules.Serialise(InputBinding.OfAxis(JoyAxis.LeftX, -1)));
        Assert.Equal("none", InputBindingRules.Serialise(InputBinding.Unbound));
    }

    [Theory]
    [InlineData("KEY:e")]
    [InlineData(" key:E ")]
    [InlineData("Key:E")]
    public void Parse_ForgivesCaseAndPadding(string text)
    {
        Assert.True(InputBindingRules.TryParse(text, out InputBinding binding));
        Assert.Equal(KeyE, binding);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("E")]
    [InlineData("key")]
    [InlineData("key:")]
    [InlineData("key:NotAKey")]
    [InlineData("key:None")]
    [InlineData("key:E:extra")]
    [InlineData("key:999999999")]
    [InlineData("mouse:None")]
    [InlineData("mouse:Seventh")]
    [InlineData("joy:Invalid")]
    [InlineData("joy:Banana")]
    [InlineData("axis:TriggerLeft")]
    [InlineData("axis:TriggerLeft:up")]
    [InlineData("axis:Invalid:+")]
    [InlineData("wheel:Up")]
    public void Parse_RejectsWhatIsNotABinding(string? text)
    {
        Assert.False(InputBindingRules.TryParse(text, out InputBinding binding));
        Assert.True(binding.IsUnbound);
    }

    [Fact]
    public void Parse_None_IsAnUnboundBindingAndNotAFailure()
    {
        Assert.True(InputBindingRules.TryParse("none", out InputBinding binding));
        Assert.True(binding.IsUnbound);
        Assert.True(InputBindingRules.TryParse("NONE", out _));
    }

    [Fact]
    public void AnAxisBindingAlwaysHasADirection()
    {
        Assert.Equal(1, InputBinding.OfAxis(JoyAxis.TriggerLeft, 5).Sign);
        Assert.Equal(1, InputBinding.OfAxis(JoyAxis.TriggerLeft, 0).Sign);
        Assert.Equal(-1, InputBinding.OfAxis(JoyAxis.LeftX, -3).Sign);
    }

    // --- What can be remapped ---------------------------------------------------------------

    [Fact]
    public void Actions_ListEachActionOnce()
    {
        var names = InputBindingRules.Actions.Select(a => a.Action).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void Actions_KeepEachGroupTogether()
    {
        // The screen draws a group heading whenever the group changes; a group split in two would
        // be headed twice.
        var order = new List<BindingGroup>();
        foreach (RemapAction action in InputBindingRules.Actions)
        {
            if (order.Count == 0 || order[^1] != action.Group)
            {
                order.Add(action.Group);
            }
        }

        Assert.Equal(order.Count, order.Distinct().Count());
    }

    [Theory]
    [InlineData(GameInput.Pause)]
    [InlineData(GameInput.MenuTabPrev)]
    [InlineData(GameInput.MenuTabNext)]
    [InlineData(GameInput.MenuSubPrev)]
    [InlineData(GameInput.MenuSubNext)]
    [InlineData(GameInput.LookLeft)]
    [InlineData(GameInput.LookUp)]
    [InlineData(GameInput.HotbarChord)]
    [InlineData("ui_accept")]
    [InlineData("ui_cancel")]
    [InlineData("ui_up")]
    [InlineData("not_an_action")]
    public void MenuAndSystemActions_AreNotRemappable(string action)
    {
        Assert.False(InputBindingRules.IsRemappable(action, BindingDevice.Keyboard));
        Assert.False(InputBindingRules.IsRemappable(action, BindingDevice.Gamepad));
        Assert.DoesNotContain(InputBindingRules.Actions, a => a.Action == action);
    }

    [Fact]
    public void EveryRemappableAction_HasAKeyboardHalf()
    {
        Assert.All(InputBindingRules.Actions, a => Assert.True(a.Keyboard, a.Action));
    }

    [Theory]
    [InlineData(GameInput.MoveForward)]
    [InlineData(GameInput.MoveBack)]
    [InlineData(GameInput.MoveLeft)]
    [InlineData(GameInput.MoveRight)]
    [InlineData("hotbar_1")]
    [InlineData("hotbar_5")]
    public void TheStickAndTheHotbarChord_AreFixedOnAPad(string action)
    {
        Assert.True(InputBindingRules.IsRemappable(action, BindingDevice.Keyboard));
        Assert.False(InputBindingRules.IsRemappable(action, BindingDevice.Gamepad));
        Assert.False(InputBindingRules.CanBind(action, BindingDevice.Gamepad, InputBinding.OfJoy(JoyButton.A)));
        Assert.False(InputBindingRules.CanBind(action, BindingDevice.Gamepad, InputBinding.Unbound));
    }

    [Fact]
    public void EveryHotbarSlot_IsListed()
    {
        foreach (string slot in GameInput.Hotbar)
        {
            Assert.True(InputBindingRules.IsRemappable(slot, BindingDevice.Keyboard), slot);
        }
    }

    [Fact]
    public void EveryRemappableAction_HasANameInTheCatalogue()
    {
        // The screen builds these keys by concatenation, which LocKeyUsageTests cannot see.
        HashSet<string> keys = File.ReadLines(Path.Combine(RepositoryRoot(), "data", "locale", "strings.csv"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => l.Split(',')[0])
            .ToHashSet();
        Assert.All(InputBindingRules.Actions, a => Assert.Contains("settings.bind.action." + a.Action, keys));
    }

    // --- Reserved inputs --------------------------------------------------------------------

    [Theory]
    [InlineData(Key.Escape)]
    [InlineData(Key.Enter)]
    [InlineData(Key.KpEnter)]
    [InlineData(Key.Tab)]
    [InlineData(Key.Up)]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.F1)]
    [InlineData(Key.F5)]
    [InlineData(Key.F9)]
    [InlineData(Key.F12)]
    [InlineData(Key.Meta)]
    [InlineData(Key.None)]
    public void KeysThatWorkTheMenus_AreReserved(Key key)
    {
        Assert.True(InputBindingRules.IsReserved(InputBinding.OfKey(key)));
        Assert.False(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Keyboard, InputBinding.OfKey(key)));
    }

    [Theory]
    [InlineData(Key.W)]
    [InlineData(Key.Space)]
    [InlineData(Key.Shift)]
    [InlineData(Key.Ctrl)]
    [InlineData(Key.Capslock)]
    [InlineData(Key.Key1)]
    [InlineData(Key.Q)]
    [InlineData(Key.Backspace)]
    public void OrdinaryKeys_AreNotReserved(Key key)
    {
        Assert.False(InputBindingRules.IsReserved(InputBinding.OfKey(key)));
        Assert.True(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Keyboard, InputBinding.OfKey(key)));
    }

    [Fact]
    public void OnAPad_StartAndGuideAreReserved_AndEveryOtherButtonIsFree()
    {
        Assert.True(InputBindingRules.IsReserved(InputBinding.OfJoy(JoyButton.Start)));
        Assert.True(InputBindingRules.IsReserved(InputBinding.OfJoy(JoyButton.Guide)));
        Assert.True(InputBindingRules.IsReserved(InputBinding.OfJoy(JoyButton.Invalid)));
        foreach (JoyButton button in new[]
                 {
                     JoyButton.A, JoyButton.B, JoyButton.X, JoyButton.Y, JoyButton.Back, JoyButton.LeftShoulder,
                     JoyButton.RightShoulder, JoyButton.LeftStick, JoyButton.RightStick, JoyButton.DpadUp,
                     JoyButton.DpadDown, JoyButton.DpadLeft, JoyButton.DpadRight,
                 })
        {
            Assert.False(InputBindingRules.IsReserved(InputBinding.OfJoy(button)), button.ToString());
        }
    }

    [Fact]
    public void OnAPad_OnlyTheTriggersCanBeBoundAsAxes()
    {
        Assert.False(InputBindingRules.IsReserved(InputBinding.OfAxis(JoyAxis.TriggerLeft, 1)));
        Assert.False(InputBindingRules.IsReserved(InputBinding.OfAxis(JoyAxis.TriggerRight, 1)));
        Assert.True(InputBindingRules.IsReserved(InputBinding.OfAxis(JoyAxis.TriggerLeft, -1)));
        foreach (JoyAxis stick in new[] { JoyAxis.LeftX, JoyAxis.LeftY, JoyAxis.RightX, JoyAxis.RightY })
        {
            Assert.True(InputBindingRules.IsReserved(InputBinding.OfAxis(stick, 1)), stick.ToString());
            Assert.True(InputBindingRules.IsReserved(InputBinding.OfAxis(stick, -1)), stick.ToString());
        }
    }

    [Fact]
    public void MouseButtonsAndTheWheel_CanBeBound()
    {
        foreach (MouseButton button in new[]
                 {
                     MouseButton.Left, MouseButton.Right, MouseButton.Middle, MouseButton.WheelUp,
                     MouseButton.WheelDown, MouseButton.Xbutton1, MouseButton.Xbutton2,
                 })
        {
            Assert.True(InputBindingRules.CanBind(GameInput.Attack, BindingDevice.Keyboard, InputBinding.OfMouse(button)));
        }

        Assert.True(InputBindingRules.IsReserved(InputBinding.OfMouse(MouseButton.None)));
    }

    [Fact]
    public void ABinding_OnlyGoesOnItsOwnDevice()
    {
        Assert.False(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Gamepad, Space));
        Assert.False(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Keyboard, InputBinding.OfJoy(JoyButton.A)));
        Assert.True(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Gamepad, InputBinding.OfJoy(JoyButton.A)));
        Assert.True(InputBindingRules.CanBind(GameInput.Attack, BindingDevice.Gamepad, InputBinding.OfAxis(JoyAxis.TriggerRight, 1)));
        Assert.Equal(BindingDevice.Gamepad, InputBindingRules.DeviceOf(InputBinding.OfAxis(JoyAxis.TriggerRight, 1)));
        Assert.Equal(BindingDevice.Keyboard, InputBindingRules.DeviceOf(InputBinding.OfMouse(MouseButton.Left)));
    }

    [Fact]
    public void Clearing_IsAllowedOnAnyHalfThatCanChange()
    {
        Assert.True(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Keyboard, InputBinding.Unbound));
        Assert.True(InputBindingRules.CanBind(GameInput.Jump, BindingDevice.Gamepad, InputBinding.Unbound));
        Assert.False(InputBindingRules.CanBind(GameInput.Pause, BindingDevice.Keyboard, InputBinding.Unbound));
    }

    // --- Reading the saved lists ------------------------------------------------------------

    [Fact]
    public void Resolve_WithNothingSaved_IsTheDefault()
    {
        Assert.Equal(Space, InputBindingRules.Resolve(null, GameInput.Jump, BindingDevice.Keyboard, Space));
        Assert.Equal(Space, InputBindingRules.Resolve(Array.Empty<string>(), GameInput.Jump, BindingDevice.Keyboard, Space));
        Assert.False(InputBindingRules.IsRemapped(null, GameInput.Jump, BindingDevice.Keyboard));
    }

    [Fact]
    public void Resolve_TakesTheSavedBinding()
    {
        string[] saved = { "jump=key:F", "interact=mouse:Xbutton1" };
        Assert.Equal(KeyF, InputBindingRules.Resolve(saved, GameInput.Jump, BindingDevice.Keyboard, Space));
        Assert.Equal(InputBinding.OfMouse(MouseButton.Xbutton1),
            InputBindingRules.Resolve(saved, GameInput.Interact, BindingDevice.Keyboard, KeyE));
        Assert.True(InputBindingRules.IsRemapped(saved, GameInput.Jump, BindingDevice.Keyboard));
        Assert.False(InputBindingRules.IsRemapped(saved, GameInput.Dodge, BindingDevice.Keyboard));
    }

    [Fact]
    public void Resolve_HonoursACleared_Binding()
    {
        string[] saved = { "jump=none" };
        Assert.True(InputBindingRules.Resolve(saved, GameInput.Jump, BindingDevice.Keyboard, Space).IsUnbound);
        Assert.True(InputBindingRules.IsRemapped(saved, GameInput.Jump, BindingDevice.Keyboard));
    }

    [Theory]
    [InlineData("jump=")]
    [InlineData("jump")]
    [InlineData("jump=key:NotAKey")]
    [InlineData("jump=key:Escape")]
    [InlineData("jump=joy:A")]
    [InlineData("jump=axis:LeftX:+")]
    [InlineData("jumping=key:F")]
    [InlineData("=key:F")]
    public void Resolve_IgnoresALineItCannotUse(string line)
    {
        string[] saved = { line };
        Assert.Equal(Space, InputBindingRules.Resolve(saved, GameInput.Jump, BindingDevice.Keyboard, Space));
        Assert.False(InputBindingRules.IsRemapped(saved, GameInput.Jump, BindingDevice.Keyboard));
    }

    [Fact]
    public void Resolve_IgnoresASavedBindingForAnActionThatCannotChange()
    {
        string[] saved = { "pause=key:P", "move_forward=joy:A" };
        InputBinding escape = InputBinding.OfKey(Key.Escape);
        Assert.Equal(escape, InputBindingRules.Resolve(saved, GameInput.Pause, BindingDevice.Keyboard, escape));
        Assert.True(InputBindingRules.Resolve(saved, GameInput.MoveForward, BindingDevice.Gamepad, InputBinding.Unbound).IsUnbound);
    }

    [Fact]
    public void Resolve_TheLastLineForAnActionWins()
    {
        string[] saved = { "jump=key:F", "jump=key:G" };
        Assert.Equal(InputBinding.OfKey(Key.G), InputBindingRules.Resolve(saved, GameInput.Jump, BindingDevice.Keyboard, Space));
    }

    [Fact]
    public void Resolve_AnActionWhoseNameStartsAnother_IsNotConfused()
    {
        // lock_on is a prefix of nothing here, but hotbar_1 and a future hotbar_10 would be.
        string[] saved = { "lock_cycle_next=key:F" };
        InputBinding middle = InputBinding.OfMouse(MouseButton.Middle);
        Assert.Equal(middle, InputBindingRules.Resolve(saved, GameInput.LockOn, BindingDevice.Keyboard, middle));
    }

    [Fact]
    public void Effective_CoversEveryActionOnTheDevice_AndNoOther()
    {
        Dictionary<string, InputBinding> keys = InputBindingRules.Effective(null, BindingDevice.Keyboard, _ => KeyE);
        Dictionary<string, InputBinding> pads = InputBindingRules.Effective(null, BindingDevice.Gamepad, _ => InputBinding.Unbound);

        Assert.Equal(InputBindingRules.Actions.Count(a => a.Keyboard), keys.Count);
        Assert.Equal(InputBindingRules.Actions.Count(a => a.Gamepad), pads.Count);
        Assert.DoesNotContain(GameInput.MoveForward, pads.Keys);
        Assert.Contains(GameInput.MoveForward, keys.Keys);
    }

    [Fact]
    public void Effective_LaysSavedBindingsOverTheDefaults()
    {
        string[] saved = { "jump=key:F" };
        Dictionary<string, InputBinding> keys = InputBindingRules.Effective(
            saved, BindingDevice.Keyboard, action => action == GameInput.Jump ? Space : KeyE);
        Assert.Equal(KeyF, keys[GameInput.Jump]);
        Assert.Equal(KeyE, keys[GameInput.Interact]);
    }

    // --- Writing the saved lists ------------------------------------------------------------

    [Fact]
    public void With_AddsOneLine()
    {
        string[] saved = InputBindingRules.With(null, GameInput.Jump, KeyF, Space);
        Assert.Equal(new[] { "jump=key:F" }, saved);
    }

    [Fact]
    public void With_ReplacesEveryOlderLineForTheAction_AndKeepsTheRest()
    {
        string[] before = { "jump=key:G", "interact=key:R", "jump=key:H", "garbage" };
        string[] after = InputBindingRules.With(before, GameInput.Jump, KeyF, Space);
        Assert.Equal(new[] { "interact=key:R", "garbage", "jump=key:F" }, after);
        Assert.NotSame(before, after);
    }

    [Fact]
    public void With_TheDefault_WritesNoLine()
    {
        string[] before = { "jump=key:F", "interact=key:R" };
        Assert.Equal(new[] { "interact=key:R" }, InputBindingRules.With(before, GameInput.Jump, Space, Space));
    }

    [Fact]
    public void With_Unbound_IsSavedAsNone_UnlessUnboundIsTheDefault()
    {
        Assert.Equal(new[] { "jump=none" }, InputBindingRules.With(null, GameInput.Jump, InputBinding.Unbound, Space));
        Assert.Empty(InputBindingRules.With(null, GameInput.Mount, InputBinding.Unbound, InputBinding.Unbound));
    }

    [Fact]
    public void With_DoesNotTouchAnActionThatSharesAPrefix()
    {
        string[] before = { "hotbar_1=key:Z", "hotbar_10=key:X" };
        string[] after = InputBindingRules.With(before, "hotbar_1", KeyF, InputBinding.OfKey(Key.Key1));
        Assert.Equal(new[] { "hotbar_10=key:X", "hotbar_1=key:F" }, after);
    }

    [Fact]
    public void Without_PutsAnActionBackOnItsDefault()
    {
        string[] before = { "jump=key:F", "interact=key:R" };
        string[] after = InputBindingRules.Without(before, GameInput.Jump);
        Assert.Equal(new[] { "interact=key:R" }, after);
        Assert.Equal(Space, InputBindingRules.Resolve(after, GameInput.Jump, BindingDevice.Keyboard, Space));
        Assert.Empty(InputBindingRules.Without(null, GameInput.Jump));
    }

    [Fact]
    public void WhatIsWritten_IsWhatIsReadBack()
    {
        string[] saved = InputBindingRules.With(null, GameInput.Attack, InputBinding.OfAxis(JoyAxis.TriggerLeft, 1),
            InputBinding.OfAxis(JoyAxis.TriggerRight, 1));
        Assert.Equal(InputBinding.OfAxis(JoyAxis.TriggerLeft, 1),
            InputBindingRules.Resolve(saved, GameInput.Attack, BindingDevice.Gamepad, InputBinding.OfAxis(JoyAxis.TriggerRight, 1)));
        Assert.Equal("attack=axis:TriggerLeft:+", saved[0]);
        Assert.Equal("axis:TriggerLeft:+", SettingsMath.BindingFor(saved, GameInput.Attack));
    }

    // --- Conflicts --------------------------------------------------------------------------

    private static Dictionary<string, InputBinding> Layout() => new()
    {
        [GameInput.Jump] = Space,
        [GameInput.Interact] = KeyE,
        [GameInput.CycleSpell] = KeyF,
        [GameInput.Mount] = InputBinding.Unbound,
    };

    [Fact]
    public void FindConflict_NamesTheActionHoldingTheInput()
    {
        Assert.Equal(GameInput.Interact, InputBindingRules.FindConflict(GameInput.Jump, KeyE, Layout()));
    }

    [Fact]
    public void FindConflict_AnActionDoesNotCollideWithItself()
    {
        Assert.Null(InputBindingRules.FindConflict(GameInput.Jump, Space, Layout()));
    }

    [Fact]
    public void FindConflict_AFreeInputCollidesWithNothing()
    {
        Assert.Null(InputBindingRules.FindConflict(GameInput.Jump, InputBinding.OfKey(Key.H), Layout()));
    }

    [Fact]
    public void FindConflict_UnboundNeverCollides_EvenWithAnotherUnboundAction()
    {
        Assert.Null(InputBindingRules.FindConflict(GameInput.Jump, InputBinding.Unbound, Layout()));
    }

    [Fact]
    public void FindConflict_DevicesDoNotCollideWithEachOther()
    {
        // Same enum number, different device: the kind is part of the binding.
        var pad = new Dictionary<string, InputBinding> { [GameInput.Jump] = InputBinding.OfJoy((JoyButton)(long)Key.E) };
        Assert.Null(InputBindingRules.FindConflict(GameInput.Interact, KeyE, pad));
    }

    [Fact]
    public void FindConflict_TheTwoDirectionsOfAnAxisAreDifferentInputs()
    {
        var pad = new Dictionary<string, InputBinding> { [GameInput.Attack] = InputBinding.OfAxis(JoyAxis.TriggerRight, 1) };
        Assert.Equal(GameInput.Attack, InputBindingRules.FindConflict(GameInput.Block, InputBinding.OfAxis(JoyAxis.TriggerRight, 1), pad));
        Assert.Null(InputBindingRules.FindConflict(GameInput.Block, InputBinding.OfAxis(JoyAxis.TriggerLeft, 1), pad));
    }

    [Fact]
    public void ASwap_LeavesNoConflictBehind()
    {
        // Jump takes E; Interact takes what Jump had.
        Dictionary<string, InputBinding> layout = Layout();
        string other = InputBindingRules.FindConflict(GameInput.Jump, KeyE, layout)!;
        InputBinding mine = layout[GameInput.Jump];

        string[] saved = InputBindingRules.With(null, GameInput.Jump, KeyE, Space);
        saved = InputBindingRules.With(saved, other, mine, KeyE);

        Dictionary<string, InputBinding> after = InputBindingRules.Effective(saved, BindingDevice.Keyboard, action => action switch
        {
            GameInput.Jump => Space,
            GameInput.Interact => KeyE,
            _ => InputBinding.Unbound,
        });
        Assert.Equal(KeyE, after[GameInput.Jump]);
        Assert.Equal(Space, after[GameInput.Interact]);
        Assert.Null(InputBindingRules.FindConflict(GameInput.Jump, KeyE, after));
        Assert.Null(InputBindingRules.FindConflict(GameInput.Interact, Space, after));
    }

    private static InputBinding KeyDefault(string action) => action switch
    {
        GameInput.Jump => Space,
        GameInput.Interact => KeyE,
        GameInput.CycleSpell => KeyF,
        _ => InputBinding.Unbound,
    };

    private static void AssertNoSharedInputs(string[] saved)
    {
        Dictionary<string, InputBinding> after = InputBindingRules.Effective(saved, BindingDevice.Keyboard, KeyDefault);
        foreach ((string action, InputBinding binding) in after)
        {
            Assert.Null(InputBindingRules.FindConflict(action, binding, after));
        }
    }

    [Fact]
    public void Restore_OfOneHalfOfASwap_PutsTheOtherHalfBackToo()
    {
        string[] saved =
        {
            InputBindingRules.With(null, GameInput.Jump, KeyE, Space)[0],
            InputBindingRules.With(null, GameInput.Interact, Space, KeyE)[0],
            InputBindingRules.With(null, GameInput.Sprint, InputBinding.OfKey(Key.H), InputBinding.Unbound)[0],
        };
        string[] after = InputBindingRules.Restore(saved, GameInput.Jump, BindingDevice.Keyboard, KeyDefault);
        Assert.Equal(new[] { saved[2] }, after);
        AssertNoSharedInputs(after);
    }

    [Fact]
    public void Restore_OfAnActionUnboundByAConflict_PutsBackTheOneThatTookItsKey()
    {
        // Jump took E and Interact was unbound; Interact going back to E must not leave Jump on E.
        string[] saved = InputBindingRules.With(null, GameInput.Jump, KeyE, Space);
        saved = InputBindingRules.With(saved, GameInput.Interact, InputBinding.Unbound, KeyE);
        string[] after = InputBindingRules.Restore(saved, GameInput.Interact, BindingDevice.Keyboard, KeyDefault);
        Assert.Empty(after);
        AssertNoSharedInputs(after);
    }

    [Fact]
    public void Restore_FollowsAChainOfRemaps()
    {
        // A three-way rotation: restoring one link leaves the next colliding, and so on round.
        string[] saved = InputBindingRules.With(null, GameInput.Jump, KeyE, Space);
        saved = InputBindingRules.With(saved, GameInput.Interact, KeyF, KeyE);
        saved = InputBindingRules.With(saved, GameInput.CycleSpell, Space, KeyF);
        Assert.Empty(InputBindingRules.Restore(saved, GameInput.Jump, BindingDevice.Keyboard, KeyDefault));
    }

    [Fact]
    public void Restore_WithNothingColliding_TouchesOnlyItsOwnAction()
    {
        string[] saved = InputBindingRules.With(null, GameInput.Jump, InputBinding.OfKey(Key.H), Space);
        saved = InputBindingRules.With(saved, GameInput.Interact, InputBinding.OfKey(Key.R), KeyE);
        string[] after = InputBindingRules.Restore(saved, GameInput.Jump, BindingDevice.Keyboard, KeyDefault);
        Assert.Equal(new[] { saved[1] }, after);
    }

    [Fact]
    public void Restore_LeavesAnUnremappedCollisionAlone_AndEnds()
    {
        // Two defaults that share a key cannot be fixed by restoring; it must not loop on them.
        string[] saved = InputBindingRules.With(null, GameInput.Jump, InputBinding.OfKey(Key.H), Space);
        string[] after = InputBindingRules.Restore(saved, GameInput.Jump, BindingDevice.Keyboard,
            action => action is GameInput.Jump or GameInput.Interact ? Space : InputBinding.Unbound);
        Assert.Empty(after);
    }

    [Fact]
    public void UnbindingTheOther_LeavesItClearedAndSaved()
    {
        string[] saved = InputBindingRules.With(null, GameInput.Jump, KeyE, Space);
        saved = InputBindingRules.With(saved, GameInput.Interact, InputBinding.Unbound, KeyE);
        Assert.Contains("interact=none", saved);
        Assert.True(InputBindingRules.Resolve(saved, GameInput.Interact, BindingDevice.Keyboard, KeyE).IsUnbound);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("project.godot not found");
    }
}

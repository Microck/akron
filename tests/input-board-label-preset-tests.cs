using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class InputBoardLabelPresetTests {
    [Theory]
    [InlineData(AkronInputBoardLabelPreset.Names, "Right")]
    [InlineData(AkronInputBoardLabelPreset.Keyboard, "D")]
    [InlineData(AkronInputBoardLabelPreset.Arrows, ">")]
    [InlineData(AkronInputBoardLabelPreset.Short, "R")]
    public void PresetsPreserveKeysWithoutActionsAndRenameRealActions(AkronInputBoardLabelPreset preset, string rightLabel) {
        var keyboard = new AkronInputBoardElement { Label = "F1", KeyBindings = new List<Keys> { Keys.F1 }, X = 7, Y = 120 };
        var empty = new AkronInputBoardElement { Label = "Custom" };
        var nullActions = new AkronInputBoardElement { Label = "Null actions", Bindings = null, KeyBindings = new List<Keys> { Keys.F2 } };
        var right = new AkronInputBoardElement { Label = "Custom right", Bindings = new List<AkronInputBoardBinding> { AkronInputBoardBinding.Right, AkronInputBoardBinding.Left }, KeyBindings = new List<Keys> { Keys.D } };
        var keys = keyboard.KeyBindings;
        var actions = right.Bindings;
        AkronInputBoard.ApplyLabelPreset(new[] { keyboard, empty, nullActions, right, null }, preset);
        Assert.Equal("F1", keyboard.Label);
        Assert.Equal("Custom", empty.Label);
        Assert.Equal("Null actions", nullActions.Label);
        Assert.Equal(rightLabel, right.Label);
        Assert.Same(keys, keyboard.KeyBindings);
        Assert.Same(actions, right.Bindings);
        Assert.Equal(new[] { Keys.F1 }, keyboard.KeyBindings);
        Assert.Equal(7, keyboard.X);
        Assert.Equal(120, keyboard.Y);
        Assert.Null(nullActions.Bindings);
    }

    [Fact]
    public void NullCollectionAndUnknownActionRemainSafe() {
        AkronInputBoard.ApplyLabelPreset(null, AkronInputBoardLabelPreset.Short);
        var unknown = new AkronInputBoardElement { Label = "Future action", Bindings = new List<AkronInputBoardBinding> { (AkronInputBoardBinding)999 } };
        AkronInputBoard.ApplyLabelPreset(new[] { unknown }, AkronInputBoardLabelPreset.Short);
        Assert.Equal("Future action", unknown.Label);
    }
}

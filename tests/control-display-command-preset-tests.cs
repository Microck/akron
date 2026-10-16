using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ControlDisplayCommandPresetTests {
    public static IEnumerable<object[]> LayoutStyles() {
        foreach (string layout in new[] { "default", "compact", "split", "keyboard", "bar" })
            foreach (AkronInputBoardLabelPreset style in Enum.GetValues<AkronInputBoardLabelPreset>())
                yield return new object[] { layout, style };
    }

    [Theory]
    [MemberData(nameof(LayoutStyles))]
    public void EveryBuiltLayoutAcceptsSelectedStyleWithoutChangingStructure(string layout, AkronInputBoardLabelPreset style) {
        var elements = layout switch {
            "default" => AkronInputBoard.BuildDefaultElements(),
            "compact" => AkronInputBoard.BuildCompactElements(),
            "split" => AkronInputBoard.BuildSplitElements(),
            "keyboard" => AkronInputBoard.BuildKeyboardElements(),
            _ => AkronInputBoard.BuildBarElements()
        };
        var originals = elements.Select(e => e.Clone()).ToList();
        AkronInputBoard.ApplyLabelPreset(elements, style);
        for (int i = 0; i < elements.Count; i++) {
            var e = elements[i]; var old = originals[i];
            Assert.Equal((old.Id, old.X, old.Y, old.Width, old.Height), (e.Id, e.X, e.Y, e.Width, e.Height));
            Assert.Equal((old.FillColor, old.PressedFillColor, old.StrokeColor, old.TextColor, old.Visible, old.OutlineWidth, old.TextScale),
                (e.FillColor, e.PressedFillColor, e.StrokeColor, e.TextColor, e.Visible, e.OutlineWidth, e.TextScale));
            Assert.Equal(old.Bindings, e.Bindings);
            Assert.Equal(old.KeyBindings, e.KeyBindings);
            var oracle = new AkronInputBoardElement { Label = old.Label, Bindings = old.Bindings };
            AkronInputBoard.ApplyLabelPreset(new[] { oracle }, style);
            Assert.Equal(oracle.Label, e.Label);
        }
        if (layout is "default" or "compact" && style == AkronInputBoardLabelPreset.Short)
            Assert.Equal(new[] { "X", "U", "C", "L", "D", "R", "G", "J" }, elements.Select(e => e.Label));
    }

    [Fact]
    public void CommandAppliesCurrentStyleOnlyAfterValidLayoutAndKeepsExplicitResetPolicy() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Commands/akron-input-commands.cs"));
        int start = source.IndexOf("case \"preset\":", StringComparison.Ordinal);
        int end = source.IndexOf("case \"reset\":", start, StringComparison.Ordinal);
        string preset = source[start..end];
        Assert.Contains("AkronInputBoard.ApplyLabelPreset(AkronModule.Settings.InputBoardElements, AkronModule.Settings.InputBoardLabelPreset);", preset);
        Assert.True(preset.IndexOf("return;", StringComparison.Ordinal) < preset.IndexOf("ApplyLabelPreset", StringComparison.Ordinal));
        Assert.DoesNotContain("InputBoardLabelPreset =", preset);
        Assert.DoesNotContain("ShowTaps =", preset);
        string reset = source[end..source.IndexOf("case \"corner\":", end, StringComparison.Ordinal)];
        Assert.Contains("InputBoardLabelPreset = AkronInputBoardLabelPreset.Keyboard;", reset);
        Assert.Contains("InputBoardElements = AkronInputBoard.BuildDefaultElements();", reset);
        Assert.DoesNotContain("ApplyLabelPreset", reset);
    }
}

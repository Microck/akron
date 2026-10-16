using System;
using System.IO;
using System.Collections.Generic;
using Monocle;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CursorHoldBindingTests {
    [NativeEverestFact]
    public void CaptureClearAndDefaultKeepWrapperAndNativeBindingIdentity() {
        ButtonBinding target = AkronModuleSettings.CreateLeftAltHoldBinding();
        ButtonBinding original = target;
        var nativeBinding = target.Binding;
        int replacements = 0;
        void Apply(ButtonBinding value) => AkronCursorHoldBinding.Apply(() => target, value => { target = value; replacements++; }, value);
        ButtonBinding captured = new(0, Keys.V);
        Apply(captured);
        Assert.Same(original, target); Assert.Same(nativeBinding, target.Binding);
        Assert.Equal(new[] { Keys.V }, target.Keys);
        captured.Keys.Clear(); Assert.Equal(new[] { Keys.V }, target.Keys);
        Apply(AkronModuleSettings.CreateEmptyButtonBinding());
        Assert.Empty(target.Keys); Assert.Empty(target.Buttons); Assert.Empty(target.MouseButtons);
        Apply(AkronModuleSettings.CreateLeftAltHoldBinding());
        Assert.Equal(new[] { Keys.LeftAlt }, target.Keys);
        Assert.Same(original, target); Assert.Same(nativeBinding, target.Binding);
        Assert.Equal(0, replacements);
    }
    [NativeEverestFact]
    public void CaptureCompletionResolvesTheCurrentSettingsWrapper() {
        ButtonBinding old = new(0, Keys.LeftAlt), current = old;
        Action<ButtonBinding> complete = value => AkronCursorHoldBinding.Apply(() => current, value => current = value, value);
        ButtonBinding newer = new(0, Keys.RightAlt); current = newer;
        complete(new ButtonBinding(0, Keys.V));
        Assert.Same(newer, current);
        Assert.Equal(new[] { Keys.V }, newer.Keys);
        Assert.Equal(new[] { Keys.LeftAlt }, old.Keys);
    }
    [NativeEverestFact]
    public void ControllerAndMouseListsAreCopiedWithoutAliasing() {
        ButtonBinding target = AkronModuleSettings.CreateLeftAltHoldBinding();
        ButtonBinding replacement = AkronModuleSettings.CreateEmptyButtonBinding();
        replacement.Buttons = new List<Buttons> { Buttons.A };
        replacement.MouseButtons = new List<MInput.MouseData.MouseButtons> { MInput.MouseData.MouseButtons.Left };
        AkronCursorHoldBinding.Apply(() => target, _ => throw new Exception("Must preserve wrapper"), replacement);
        replacement.Buttons.Clear(); replacement.MouseButtons.Clear();
        Assert.Equal(new[] { Buttons.A }, target.Buttons);
        Assert.Equal(new[] { MInput.MouseData.MouseButtons.Left }, target.MouseButtons);
    }
    [Fact]
    public void MissingWrapperUsesNormalSetterLifecycle() {
        ButtonBinding? current = null;
        ButtonBinding replacement = new(0, Keys.V);
        AkronCursorHoldBinding.Apply(() => current!, value => current = value, replacement);
        Assert.Same(replacement, current);
    }
    [Fact]
    public void NullReplacementDoesNotMutateSettings() {
        bool read = false, written = false;
        Assert.Throws<ArgumentNullException>(() => AkronCursorHoldBinding.Apply(
            () => { read = true; return null!; }, _ => written = true, null!));
        Assert.False(read); Assert.False(written);
    }
    [Fact]
    public void EveryCursorPopupMutationUsesIdentityPreservingPath() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Overlay/akron-overlay-runtime-popups.cs"));
        int start = source.IndexOf("private void DrawCursorHoldBindingRow", StringComparison.Ordinal);
        string body = source.Substring(start, source.IndexOf("private void DrawHitboxTrailPopupControls", start, StringComparison.Ordinal) - start);
        Assert.Contains("Func<ButtonBinding> getter", body);
        Assert.Contains("AkronCursorHoldBinding.Apply(getter, setter, value)", body);
        Assert.Contains("displayName, ApplyBinding,", body);
        Assert.Contains("ApplyBinding(AkronModuleSettings.CreateEmptyButtonBinding());", body);
        Assert.Contains("ApplyBinding(defaultBinding);", body);
        Assert.DoesNotContain("setter(defaultBinding)", body);
    }
}

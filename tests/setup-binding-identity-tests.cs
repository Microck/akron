using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class SetupBindingIdentityTests {
    [Theory]
    [InlineData("LeftAlt")]
    [InlineData("F8")]
    [InlineData(null)]
    public void KeybindImportAndRepeatPreserveExistingWrapper(string? key) {
        var settings = new AkronModuleSettings { CursorZoomHold = AkronModuleSettings.CreateLeftAltHoldBinding(), CursorToolsHold = AkronModuleSettings.CreateLeftAltHoldBinding(), ToggleOverlay = AkronModuleSettings.CreateDefaultOverlayToggleBinding() };
        var original = settings.CursorZoomHold;
        var omitted = settings.CursorToolsHold;
        Assert.NotNull(original);
        Assert.NotNull(omitted);
        var pack = new AkronSetupPack {
            Section = AkronSetupSection.Keybinds,
            State = new AkronSetupState(),
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack> {
                [nameof(AkronModuleSettings.CursorZoomHold)] = new() {
                    Keys = key == null ? new() : new() { key }
                }
            }
        };
        for (int i = 0; i < 2; i++) {
            AkronSetupPacks.Apply(settings, new AkronModuleSession(), pack);
            Assert.Same(original, settings.CursorZoomHold);
            Assert.Same(omitted, settings.CursorToolsHold);
        }
    }

    [Fact]
    public void OverlaySafetyFallbackPreservesExistingWrapper() {
        var settings = new AkronModuleSettings { CursorZoomHold = AkronModuleSettings.CreateLeftAltHoldBinding(), CursorToolsHold = AkronModuleSettings.CreateLeftAltHoldBinding(), ToggleOverlay = AkronModuleSettings.CreateDefaultOverlayToggleBinding() };
        var original = settings.ToggleOverlay;
        Assert.NotNull(original);
        var pack = new AkronSetupPack {
            Section = AkronSetupSection.Keybinds,
            State = new AkronSetupState(),
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack> {
                [nameof(AkronModuleSettings.ToggleOverlay)] = new()
            }
        };
        AkronSetupPacks.Apply(settings, new AkronModuleSession(), pack);
        Assert.Same(original, settings.ToggleOverlay);
    }

    [Fact]
    public void MissingWrapperIsCreatedWithoutRegisteringVirtualInput() {
        var settings = new AkronModuleSettings { CursorZoomHold = null! };
        var pack = new AkronSetupPack {
            Section = AkronSetupSection.Keybinds,
            State = new AkronSetupState(),
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack> {
                [nameof(AkronModuleSettings.CursorZoomHold)] = new() { Keys = new() { "LeftAlt" } }
            }
        };
        AkronSetupPacks.Apply(settings, new AkronModuleSession(), pack);
        Assert.NotNull(settings.CursorZoomHold);
        Assert.Null(settings.CursorZoomHold.Button);
    }

    [Fact]
    public void SnapshotRestorationSkipsImportOnlyOverlayNormalization() {
        // Stripped Everest getters cannot establish collection values or active input.
        // This source guard supplements the native import acceptance, not replaces it.
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Setups/akron-setup-packs.cs"));
        Assert.Contains("ApplyButtonBindings(settings, previousButtonBindings, restoreSnapshot: true);", source);
        Assert.Contains("if (!restoreSnapshot && property.Name == nameof(AkronModuleSettings.ToggleOverlay)", source);
        Assert.DoesNotContain("property.SetValue(settings, BuildButtonBinding", source);
    }
}

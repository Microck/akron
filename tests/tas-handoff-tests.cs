using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class TasHandoffTests {
    public sealed class Settings {
        public static Settings? Instance { get; set; } = new();
        public bool Enabled { get; set; }
    }
    public sealed class WrongSettings {
        public static WrongSettings Instance { get; } = new();
        public string Enabled => "yes";
    }
    public sealed class ThrowingSettings {
        public static ThrowingSettings Instance => throw new InvalidOperationException();
        public bool Enabled => true;
    }
    [Fact]
    public void EnabledReadDistinguishesDisabledAndUnavailableAndReadsFreshly() {
        Assert.False(AkronTasHandoff.TryReadEnabled(null, out _));
        Assert.False(AkronTasHandoff.TryReadEnabled(typeof(object), out _));
        Assert.False(AkronTasHandoff.TryReadEnabled(typeof(WrongSettings), out _));
        Assert.False(AkronTasHandoff.TryReadEnabled(typeof(ThrowingSettings), out _));
        Settings.Instance = null;
        Assert.False(AkronTasHandoff.TryReadEnabled(typeof(Settings), out _));
        Settings.Instance = new Settings();
        Assert.True(AkronTasHandoff.TryReadEnabled(typeof(Settings), out bool disabled));
        Assert.False(disabled);
        Settings.Instance.Enabled = true;
        Assert.True(AkronTasHandoff.TryReadEnabled(typeof(Settings), out bool enabled));
        Assert.True(enabled);
    }

    [Fact]
    public void ConfirmedStartupReleasesOverlayBeforeReturningSuccess() {
        var calls = new List<string>();
        Assert.True(AkronTasHandoff.Start(() => calls.Add("start"), () => { calls.Add("active"); return true; }, () => calls.Add("release")));
        Assert.Equal(new[] { "start", "active", "release" }, calls);
    }
    [Fact]
    public void RejectedOrEmptyFileKeepsOverlayOpen() {
        bool released = false;
        Assert.False(AkronTasHandoff.Start(() => { }, () => false, () => released = true));
        Assert.False(released);
    }
    [Fact]
    public void StartupFailureDoesNotReleaseOverlay() {
        bool checkedStatus = false, released = false;
        Assert.Throws<InvalidOperationException>(() => AkronTasHandoff.Start(
            () => throw new InvalidOperationException(), () => checkedStatus = true, () => released = true));
        Assert.False(checkedStatus); Assert.False(released);
    }
    [Fact]
    public void UnreadableStartupStatusDoesNotReleaseOverlay() {
        bool released = false;
        Assert.Throws<InvalidOperationException>(() => AkronTasHandoff.Start(
            () => { }, () => throw new InvalidOperationException(), () => released = true));
        Assert.False(released);
    }
    [Fact]
    public void ProductionPreflightsAndUsesNormalOverlayReleaseWithoutChangingPreferences() {
        string source = Read("Source/Actions/akron-actions.cs");
        int start = source.IndexOf("public static void LaunchTas()", StringComparison.Ordinal);
        string body = source.Substring(start, source.IndexOf("public static void ExportRoomTimes()", start, StringComparison.Ordinal) - start);
        Assert.True(body.IndexOf("File.Exists", StringComparison.Ordinal) < body.IndexOf("AkronTasHandoff.Start", StringComparison.Ordinal));
        Assert.True(body.IndexOf("CelesteTasInteropAvailable", StringComparison.Ordinal) < body.IndexOf("AkronTasHandoff.Start", StringComparison.Ordinal));
        Assert.Contains("AkronInterop.IsTasActive, () => AkronModule.SetOverlayVisible(Engine.Scene, false)", body);
        Assert.Contains("finally {\n                    Engine.Commands.commandHistory.RemoveAt(0);", body);
        Assert.Contains("if (!started)", body);
        Assert.True(body.IndexOf("Path.GetFullPath(path)", StringComparison.Ordinal) < body.IndexOf("string commandText", StringComparison.Ordinal));
        Assert.True(body.IndexOf("TryReadEnabled", StringComparison.Ordinal) < body.IndexOf("AkronTasHandoff.Start", StringComparison.Ordinal));
        Assert.True(body.IndexOf("if (!enabled)", StringComparison.Ordinal) < body.IndexOf("AkronTasHandoff.Start", StringComparison.Ordinal));
        Assert.Contains("TAS started, but Studio notification failed", body);
        Assert.DoesNotContain("ConsumeGameplayInputInMenu =", body);
        Assert.DoesNotContain("PauseGameplayInMenu =", body);
    }
    private static string Read(string relative) {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        Assert.NotNull(dir); return File.ReadAllText(Path.Combine(dir!.FullName, relative));
    }
}

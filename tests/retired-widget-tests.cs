using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using YamlDotNet.Serialization;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class RetiredWidgetTests {
    private static readonly string[] Retired = { "staminaWidget", "speedWidget", "dashWidget" };

    [Theory]
    [InlineData(AkronSetupSection.Hud, false)]
    [InlineData(AkronSetupSection.Hud, true)]
    [InlineData(AkronSetupSection.Whole, false)]
    [InlineData(AkronSetupSection.Whole, true)]
    public void CompleteLegacyV10ArchiveDiscardsWidgetsAndPreservesModernState(AkronSetupSection section, bool modernEnabled) {
        WithArchive(section, (root, manifest, directory) => {
            AddLegacy(root);
            foreach (string name in new[] { "speedNumber", "staminaBar", "dashBar", "dashNumber" }) root["state"]![name] = modernEnabled;
            if (section == AkronSetupSection.Whole) root["menuActionBindings"]!["Labels/Stamina Widget"] = "Q";
            string path = Write(root.ToJsonString(), manifest, directory);
            AkronSetupPack pack = AkronSetupPacks.Read(path);
            var target = new AkronModuleSettings();
            var session = new AkronModuleSession { AttemptStatus = AkronStatus.Cheat, AttemptReason = "historic" };
            AkronSetupPacks.Apply(target, session, pack);
            Assert.Equal(modernEnabled, target.SpeedNumber);
            Assert.Equal(modernEnabled, target.StaminaBar);
            Assert.Equal(modernEnabled, target.DashBar);
            Assert.Equal(modernEnabled, target.DashNumber);
            Assert.Equal("Literal", target.CustomHudLabelDefinitions[0].Text);
            Assert.Equal(AkronStatus.Cheat, session.AttemptStatus);
            Assert.DoesNotContain("Labels/Stamina Widget", pack.MenuActionBindings.Keys);
            Assert.DoesNotContain("Widget", string.Join(",", target.LabelRowOrder));
            string exported = AkronSetupPacks.SerializePackPayloadForArchive(AkronSetupPacks.Capture(target, null, "roundtrip", section));
            foreach (string name in Retired) Assert.DoesNotContain("\"" + name + "\"", exported);
        });
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("duplicate")]
    [InlineData("string")]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("nested")]
    [InlineData("version")]
    [InlineData("section")]
    [InlineData("duplicate-null-state")]
    [InlineData("duplicate-string-state")]
    [InlineData("duplicate-array-state")]
    public void MalformedLegacyArchivesStillFailBeforeApplication(string fault) {
        WithArchive(AkronSetupSection.Hud, (root, manifest, directory) => {
            AddLegacy(root);
            var state = root["state"]!.AsObject();
            if (fault == "partial") state.Remove("dashWidget");
            if (fault == "string") state["speedWidget"] = "true";
            if (fault == "null") state["speedWidget"] = null;
            if (fault == "unknown") state["unknownLegacy"] = true;
            if (fault == "missing") state.Remove("speedNumber");
            if (fault == "nested") state["customHudLabelDefinitions"]![0]!["unknownField"] = true;
            if (fault == "version") root["format"] = "akron-setup-v9";
            if (fault == "section") root["section"] = "Recorder";
            string payload = root.ToJsonString();
            if (fault == "duplicate") payload = payload.Replace("\"staminaWidget\":true", "\"staminaWidget\":true,\"staminaWidget\":false");
            if (fault == "duplicate-null-state") payload = "{\"state\":null," + payload[1..];
            if (fault == "duplicate-string-state") payload = "{\"state\":\"bad\"," + payload[1..];
            if (fault == "duplicate-array-state") payload = "{\"state\":[]," + payload[1..];
            string path = Write(payload, manifest, directory);
            Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Read(path));
        });
    }

    [Fact]
    public void LegacyYamlAndBindingsDoNotReactivateRemovedWidgets() {
        var settings = new DeserializerBuilder().IgnoreUnmatchedProperties().Build().Deserialize<AkronModuleSettings>(
            "StaminaWidget: true\nSpeedWidget: true\nDashWidget: true\nSpeedNumber: true\n");
        Assert.True(settings.SpeedNumber);
        foreach (string name in new[] { "StaminaWidget", "SpeedWidget", "DashWidget" }) {
            Assert.Null(typeof(AkronModuleSettings).GetProperty(name));
            Assert.Null(typeof(AkronSetupState).GetProperty(name));
        }
        settings.MenuActionBindings = new Dictionary<string, string> {
            ["Labels/Stamina Widget"] = "Q", ["Labels/Speed Widget"] = "W", ["Labels/Dash Widget"] = "E",
            ["Player/Speed Number"] = "R", ["custom/Stamina Widget"] = "T"
        };
        var cleaned = AkronModuleSettings.RemoveRetiredWidgetBindings(settings.MenuActionBindings);
        Assert.Equal(2, cleaned.Count);
        Assert.Equal(5, settings.MenuActionBindings.Count);
        Assert.Equal("R", cleaned["Player/Speed Number"]);
        Assert.Equal("T", cleaned["custom/Stamina Widget"]);
        var captured = AkronSetupPacks.Capture(settings, null, "bindings", AkronSetupSection.Keybinds);
        Assert.Equal(cleaned, captured.MenuActionBindings);
        var target = new AkronModuleSettings();
        captured.MenuActionBindings["Labels/Stamina Widget"] = "Q";
        AkronSetupPacks.Apply(target, null, captured);
        Assert.Equal(cleaned, target.MenuActionBindings);
    }

    [Fact]
    public void SurvivingFeatureIdsAndRegistryStayStable() {
        Assert.Equal(0, (int)AkronFeatureKind.RoomLabelOverlay);
        Assert.Equal(4, (int)AkronFeatureKind.InputViewer);
        Assert.Equal(5, (int)AkronFeatureKind.InputHistory);
        Assert.Equal(6, (int)AkronFeatureKind.ResourceBars);
        Assert.Equal(7, (int)AkronFeatureKind.RoomTimer);
        Assert.Equal(8, (int)AkronFeatureKind.DeathStats);
        Assert.Equal(9, (int)AkronFeatureKind.ReducedVisualNoise);
        Assert.Equal(10, (int)AkronFeatureKind.VisualTuning);
        Assert.Equal(11, (int)AkronFeatureKind.GrabModeHotkey);
        Assert.Equal(12, (int)AkronFeatureKind.ScreenshotTool);
        Assert.Equal(13, (int)AkronFeatureKind.RetryHotkey);
        Assert.Equal(14, (int)AkronFeatureKind.RoomReload);
        Assert.Equal(15, (int)AkronFeatureKind.ChapterReload);
        Assert.Equal(16, (int)AkronFeatureKind.DebugMapLauncher);
        Assert.Equal(18, (int)AkronFeatureKind.Savestates);
        Assert.Equal(19, (int)AkronFeatureKind.BrokeredSavestates);
        Assert.Equal(20, (int)AkronFeatureKind.TasHandoff);
        Assert.Equal(21, (int)AkronFeatureKind.SplitHelper);
        Assert.Equal(22, (int)AkronFeatureKind.DeloadSimulation);
        Assert.Equal(23, (int)AkronFeatureKind.RoomWarp);
        Assert.Equal(24, (int)AkronFeatureKind.HitboxViewer);
        Assert.Equal(25, (int)AkronFeatureKind.EntityInspector);
        Assert.Equal(26, (int)AkronFeatureKind.FlagInspector);
        Assert.Equal(27, (int)AkronFeatureKind.RespawnTime);
        Assert.Equal(28, (int)AkronFeatureKind.FrameAdvance);
        Assert.Equal(29, (int)AkronFeatureKind.Freeze);
        Assert.Equal(30, (int)AkronFeatureKind.Timescale);
        Assert.Equal(31, (int)AkronFeatureKind.AutoKill);
        Assert.Equal(32, (int)AkronFeatureKind.AutoDeafen);
        Assert.Equal(33, (int)AkronFeatureKind.TransitionSpeed);
        Assert.Equal(34, (int)AkronFeatureKind.LowVolumeBypass);
        Assert.Equal(35, (int)AkronFeatureKind.HudVisibility);
        Assert.Equal(36, (int)AkronFeatureKind.PauseMenuVisibility);
        Assert.Equal(37, (int)AkronFeatureKind.PauseCountdown);
        Assert.Equal(38, (int)AkronFeatureKind.ShowTrajectory);
        Assert.Equal(39, (int)AkronFeatureKind.FreeCamera);
        Assert.Equal(40, (int)AkronFeatureKind.AudioSpeed);
        Assert.Equal(41, (int)AkronFeatureKind.PitchShift);
        Assert.Equal(42, (int)AkronFeatureKind.FpsBypass);
        Assert.Equal(43, (int)AkronFeatureKind.TpsBypass);
        Assert.Equal(44, (int)AkronFeatureKind.SafeModeStats);
        Assert.Equal(45, (int)AkronFeatureKind.Screenshake);
        Assert.Equal(46, (int)AkronFeatureKind.TriggerViewer);
        Assert.Equal(47, (int)AkronFeatureKind.StartPosTools);
        Assert.Equal(48, (int)AkronFeatureKind.ClickTeleport);
        Assert.Equal(49, (int)AkronFeatureKind.CustomTrail);
        Assert.Equal(50, (int)AkronFeatureKind.MadelineHairLength);
        Assert.Equal(51, (int)AkronFeatureKind.MadelineEffectSync);
        Assert.Equal(52, (int)AkronFeatureKind.HidePlayer);
        Assert.Equal(53, (int)AkronFeatureKind.DeathVisuals);
        Assert.Equal(54, (int)AkronFeatureKind.RespawnAnimation);
        Assert.Equal(55, (int)AkronFeatureKind.ShowTaps);
        Assert.Equal(56, (int)AkronFeatureKind.InputsPerSecondCounter);
        Assert.Equal(57, (int)AkronFeatureKind.CustomHudLabels);
        Assert.Equal(58, (int)AkronFeatureKind.InstantComplete);
        Assert.Equal(59, (int)AkronFeatureKind.UnlockSystem);
        Assert.Equal(60, (int)AkronFeatureKind.HazardAccuracy);
        Assert.Equal(61, (int)AkronFeatureKind.Noclip);
        Assert.Equal(62, (int)AkronFeatureKind.Invincibility);
        Assert.Equal(63, (int)AkronFeatureKind.InfiniteStamina);
        Assert.Equal(64, (int)AkronFeatureKind.InfiniteDash);
        Assert.Equal(65, (int)AkronFeatureKind.DashCountOverride);
        Assert.Equal(66, (int)AkronFeatureKind.SpeedNumber);
        Assert.Equal(67, (int)AkronFeatureKind.RefillClarity);
        Assert.Equal(68, (int)AkronFeatureKind.FreezeFrames);
        Assert.Equal(69, (int)AkronFeatureKind.GroundRefillRules);
        Assert.Equal(70, (int)AkronFeatureKind.MovementStatMutation);
        Assert.Equal(71, (int)AkronFeatureKind.PauseTimerFreeze);
        Assert.Equal(72, (int)AkronFeatureKind.InputAssistShortcut);
        Assert.Equal(73, (int)AkronFeatureKind.ExtendedVariantMode);
        Assert.Equal(74, (int)AkronFeatureKind.InternalRecorder);
        Assert.Equal(75, (int)AkronFeatureKind.FastLookout);
        Assert.Equal(76, (int)AkronFeatureKind.LevelEnterSkip);
        Assert.Equal(77, (int)AkronFeatureKind.DeathPbLossRestart);
        Assert.Equal(78, (int)AkronFeatureKind.CameraOffset);
        Assert.Equal(79, (int)AkronFeatureKind.CursorTools);
        Assert.Equal(80, (int)AkronFeatureKind.CursorZoom);
        Assert.Equal(90, (int)AkronFeatureKind.SubmissionMode);
        Assert.Equal(91, (int)AkronFeatureKind.ProofRecorderGuard);
        Assert.Equal(92, (int)AkronFeatureKind.EndScreenHelper);
        Assert.Equal(93, (int)AkronFeatureKind.PauseTracker);
        Assert.Equal(94, (int)AkronFeatureKind.MapVersionStamp);
        Assert.Equal(95, (int)AkronFeatureKind.GoldenStartHelper);
        Assert.Equal(96, (int)AkronFeatureKind.GoldenTransparency);
        Assert.Equal(97, (int)AkronFeatureKind.LagPauser);
        Assert.Equal(98, (int)AkronFeatureKind.Logging);
        Assert.Equal(99, (int)AkronFeatureKind.JournalSnapshotCompare);
        Assert.Equal(100, (int)AkronFeatureKind.Backups);
        Assert.Equal(101, (int)AkronFeatureKind.DisablePlayback);
        Assert.Equal(102, (int)AkronFeatureKind.EntitySpawn);
        Assert.Equal(103, (int)AkronFeatureKind.PauseBuffering);
        Assert.Equal(104, (int)AkronFeatureKind.CutsceneSkip);
        Assert.Equal(105, (int)AkronFeatureKind.MadelineColors);
        Assert.Equal(106, (int)AkronFeatureKind.AttemptsLabel);
        Assert.Equal(107, (int)AkronFeatureKind.StatusLabels);
        Assert.Equal(108, (int)AkronFeatureKind.PracticeCounters);
        Assert.Equal(109, (int)AkronFeatureKind.RoomStatTracker);
        Assert.Equal(110, (int)AkronFeatureKind.CaptureCheatOptions);
        Assert.Equal(111, (int)AkronFeatureKind.Autosave);
        Assert.Equal(112, (int)AkronFeatureKind.DeathHitboxes);
        Assert.Equal(113, (int)AkronFeatureKind.SoundVolumeOverride);
        foreach (AkronFeatureKind kind in Enum.GetValues<AkronFeatureKind>()) Assert.Equal(kind, AkronFeatureRegistry.Get(kind).Kind);
        foreach (int retired in new[] { 1, 2, 3 }) Assert.False(Enum.IsDefined(typeof(AkronFeatureKind), retired));
    }

    private static void AddLegacy(JsonObject root) {
        foreach (string name in Retired) root["state"]![name] = true;
        foreach (string name in new[] { "Stamina Widget", "Speed Widget", "Dash Widget" }) root["state"]!["labelRowOrder"]!.AsArray().Add(name);
    }
    private static string Write(string payload, AkronArchiveManifest manifest, string directory) {
        string path = Path.Combine(directory, "legacy.akr");
        AkronArchive.WriteSinglePayloadArchive(path, manifest, AkronSetupPacks.SetupArchivePayload, payload);
        return path;
    }
    private static void WithArchive(AkronSetupSection section, Action<JsonObject, AkronArchiveManifest, string> action) {
        string directory = Path.Combine(Path.GetTempPath(), "akron-retired-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try {
            var settings = new AkronModuleSettings {
                SpeedNumber = true, StaminaBar = true, DashBar = true, DashNumber = true,
                CustomHudLabelDefinitions = new List<AkronCustomHudLabel> { new() { Text = "Literal" } }
            };
            string path = Path.Combine(directory, "current.akr");
            AkronSetupPacks.Write(settings, null, path, "current", section);
            string payload = AkronArchive.ReadSinglePayloadArchive(path, AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload, 2 * 1024 * 1024, out AkronArchiveManifest manifest);
            Assert.NotNull(AkronSetupPacks.Read(path));
            action(JsonNode.Parse(payload)!.AsObject(), manifest, directory);
        } finally { Directory.Delete(directory, true); }
    }
}

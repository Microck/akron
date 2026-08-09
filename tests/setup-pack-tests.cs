using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class SetupPackTests {
    [Fact]
    public void WholeArchiveRoundTripPreservesPortableMenuBindingsAndCurrentMapStartPositions() {
        const string areaSid = "Tests/WholeArchiveRoundTrip";
        string directory = Path.Combine(Path.GetTempPath(), "akron-whole-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "whole.akr");
        try {
            AkronModuleSettings source = new AkronModuleSettings {
                MenuActionBindings = new Dictionary<string, string> {
                    ["Shortcuts/Retry"] = "Ctrl+R"
                },
                RecordingOutputFolder = "/private/source"
            };
            AkronModuleSession sourceSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [3] = new AkronStartPos {
                        Position = new Vector2(12f, 34f),
                        Room = "a-00",
                        AreaSid = areaSid,
                        StateSlotName = SavePackSnapshot(areaSid, "a-00", 3)
                    }
                }
            };

            AkronSetupPacks.Write(source, sourceSession, path, "Whole", AkronSetupSection.Whole);
            AkronSetupPack pack = AkronSetupPacks.Read(path);
            AkronModuleSettings target = new AkronModuleSettings {
                RecordingOutputFolder = "/trusted/target"
            };
            AkronModuleSession targetSession = new AkronModuleSession();

            AkronSetupPacks.Apply(target, targetSession, pack);

            Assert.Equal("Ctrl+R", target.MenuActionBindings["Shortcuts/Retry"]);
            AkronStartPos imported = Assert.Single(targetSession.StartPositions).Value;
            Assert.Equal(3, Assert.Single(targetSession.StartPositions).Key);
            // The Everest test stub cannot preserve Vector2 component values,
            // so the room/map fields are the stable archive-boundary proof.
            AkronStartPosPackEntry serializedStartPos = Assert.Single(pack.StartPositions).Value;
            Assert.Equal("a-00", serializedStartPos.Room);
            Assert.Equal(areaSid, imported.AreaSid);
            Assert.Equal("/trusted/target", target.RecordingOutputFolder);

            string payload = AkronArchive.ReadPayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                2 * 1024 * 1024,
                AkronSetupPacks.MaxStartPositions,
                512L * 1024L * 1024L,
                out _,
                out _);
            Assert.DoesNotContain("/private/source", payload, StringComparison.Ordinal);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(AkronActions.GetStartPosStateSlotName(areaSid, 3));
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AutoDeafenImportPreservesMachineLocalHotkeyAndExcludesItFromPayload() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-auto-deafen-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "auto-deafen.akr");
        AkronModuleSettings source = new AkronModuleSettings {
            AutoDeafen = true,
            AutoDeafenHotkey = new string('A', 4096),
            AutoDeafenArea = true
        };
        try {
            AkronSetupPacks.Write(source, session: null, path, section: AkronSetupSection.AutoDeafen);
            AkronSetupPack pack = AkronSetupPacks.Read(path);
            AkronModuleSettings target = new AkronModuleSettings {
                AutoDeafenHotkey = "Ctrl+Shift+D"
            };

            AkronSetupPacks.Apply(target, session: null, pack, AkronSetupSection.AutoDeafen);

            Assert.Equal("Ctrl+Shift+D", target.AutoDeafenHotkey);
            string payload = AkronArchive.ReadSinglePayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                2 * 1024 * 1024,
                out _);
            Assert.DoesNotContain("autoDeafenHotkey", payload, StringComparison.Ordinal);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void MaplessWholeImportDoesNotClearSessionStartPositions() {
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [1] = new AkronStartPos { AreaSid = "Map/Current", Room = "keep" }
            }
        };
        AkronSetupPack pack = AkronSetupPacks.Capture(new AkronModuleSettings(), session: null, section: AkronSetupSection.Whole);

        AkronSetupPacks.Apply(new AkronModuleSettings(), session, pack);

        Assert.Equal("keep", Assert.Single(session.StartPositions).Value.Room);
    }

    [Theory]
    [InlineData("999999")]
    [InlineData("A, B")]
    [InlineData("f8")]
    public void KeybindImportRejectsNonCanonicalEnumValues(string value) {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Keybinds,
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack> {
                [nameof(AkronModuleSettings.ToggleOverlay)] = new AkronButtonBindingPack { Keys = new List<string> { value } }
            }
        };

        Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Apply(new AkronModuleSettings(), null, pack));
    }

    [Fact]
    public void KeybindImportRejectsUnknownBindingProperty() {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Keybinds,
            ButtonBindings = new Dictionary<string, AkronButtonBindingPack> {
                ["NotARealBinding"] = new AkronButtonBindingPack { Keys = new List<string> { "F8" } }
            }
        };

        Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Apply(new AkronModuleSettings(), null, pack));
    }

    [Theory]
    [InlineData(20_000_000f, 0f, -1, -1)]
    [InlineData(0f, -20_000_000f, -1, -1)]
    [InlineData(0f, 0f, 6, -1)]
    [InlineData(0f, 0f, -1, 101)]
    public void StartPosImportRejectsUnsafeRuntimeValues(float x, float y, int dashes, int staminaPercent) {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.StartPos,
            ArchiveMapSid = "Map/Current",
            StartPositions = new Dictionary<int, AkronStartPosPackEntry> {
                [1] = new AkronStartPosPackEntry {
                    X = x,
                    Y = y,
                    Room = "room",
                    AreaSid = "Map/Current",
                    Dashes = dashes,
                    StaminaPercent = staminaPercent
                }
            }
        };

        Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Apply(new AkronModuleSettings(), new AkronModuleSession(), pack));
    }

    [Fact]
    public void HudImportRejectsUndefinedInputBoardEnums() {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Hud,
            State = new AkronSetupState {
                InputBoardElements = new List<AkronInputBoardElement> {
                    new AkronInputBoardElement {
                        Id = "unsafe",
                        Bindings = new List<AkronInputBoardBinding> { (AkronInputBoardBinding) 999 },
                        KeyBindings = new List<Keys> { (Keys) 999 }
                    }
                }
            }
        };

        Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Apply(new AkronModuleSettings(), null, pack));
    }

    [Fact]
    public void ArchiveReadRejectsMissingAndUnknownNestedFields() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-nested-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "source.akr");
        try {
            AkronModuleSettings settings = new AkronModuleSettings {
                InputBoardElements = new List<AkronInputBoardElement> {
                    new AkronInputBoardElement { Id = "jump", Label = "Jump" }
                }
            };
            AkronSetupPacks.Write(settings, null, sourcePath, "HUD", AkronSetupSection.Hud);
            string payload = AkronArchive.ReadSinglePayloadArchive(
                sourcePath,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                2 * 1024 * 1024,
                out AkronArchiveManifest manifest);

            JsonObject missingRoot = JsonNode.Parse(payload)!.AsObject();
            missingRoot["state"]!["inputBoardElements"]![0]!.AsObject().Remove("label");
            string missingPath = Path.Combine(directory, "missing.akr");
            AkronArchive.WriteSinglePayloadArchive(missingPath, manifest, AkronSetupPacks.SetupArchivePayload, missingRoot.ToJsonString());
            Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Read(missingPath));

            JsonObject unknownRoot = JsonNode.Parse(payload)!.AsObject();
            unknownRoot["state"]!["inputBoardElements"]![0]!["unexpected"] = true;
            string unknownPath = Path.Combine(directory, "unknown.akr");
            AkronArchive.WriteSinglePayloadArchive(unknownPath, manifest, AkronSetupPacks.SetupArchivePayload, unknownRoot.ToJsonString());
            Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Read(unknownPath));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ArchiveReadRequiresPayloadTimestampToMatchManifest() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-timestamp-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string sourcePath = Path.Combine(directory, "source.akr");
        try {
            AkronSetupPacks.Write(new AkronModuleSettings(), null, sourcePath, "HUD", AkronSetupSection.Hud);
            string payload = AkronArchive.ReadSinglePayloadArchive(
                sourcePath,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                2 * 1024 * 1024,
                out AkronArchiveManifest manifest);
            JsonObject root = JsonNode.Parse(payload)!.AsObject();
            root["createdUtc"] = "2026-01-01T00:00:00Z";
            string mismatchPath = Path.Combine(directory, "mismatch.akr");
            AkronArchive.WriteSinglePayloadArchive(mismatchPath, manifest, AkronSetupPacks.SetupArchivePayload, root.ToJsonString());

            Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Read(mismatchPath));
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ScopedArchiveWritesOnlyOwnedCamelCaseFields() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-scoped-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "auto-kill.akr");
        try {
            AkronModuleSettings settings = new AkronModuleSettings {
                AutoKill = true,
                RecordingOutputFolder = "/private/recordings",
                AudioSplitterMainDevice = "private-device"
            };

            AkronSetupPacks.Write(settings, session: null, path, "Auto Kill", AkronSetupSection.AutoKill);
            string payload = AkronArchive.ReadSinglePayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                1024 * 1024,
                out _);
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            string[] stateKeys = root.GetProperty("state").EnumerateObject().Select(property => property.Name).OrderBy(name => name).ToArray();

            Assert.Equal(AkronSetupPacks.SetupPackFormat, root.GetProperty("format").GetString());
            Assert.Equal(new[] {
                "autoKill", "autoKillArea", "autoKillAreaHeight", "autoKillAreaWidth", "autoKillAreaX", "autoKillAreaY",
                "autoKillAreas", "autoKillDefaultAreaConditions", "autoKillSeconds", "autoKillShowArea", "autoKillShowAreaOnDeath",
                "autoKillTimer"
            }.OrderBy(name => name), stateKeys);
            Assert.False(root.TryGetProperty("buttonBindings", out _));
            Assert.False(root.TryGetProperty("menuActionBindings", out _));
            Assert.False(root.TryGetProperty("startPositions", out _));
            Assert.DoesNotContain("RecordingOutputFolder", payload, StringComparison.Ordinal);
            Assert.DoesNotContain("private-device", payload, StringComparison.Ordinal);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void RecorderImportPreservesMachineLocalAndUnsafeSettings() {
        AkronModuleSettings target = new AkronModuleSettings {
            RecordingOutputFolder = "/trusted/output",
            RecordingFilenameTemplate = "trusted-template",
            RecordingReplayAutoStart = AkronRecordingReplayAutoStart.Off,
            RecordingColorspaceArgs = "trusted-filter",
            RecordingFramerate = 60,
            RecordingResolutionX = 1920,
            RecordingResolutionY = 1080
        };
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Recorder,
            State = new AkronSetupState {
                RecordingOutputFolder = "/attacker/output",
                RecordingFilenameTemplate = "../../attacker",
                RecordingReplayAutoStart = AkronRecordingReplayAutoStart.Always,
                RecordingColorspaceArgs = "-filter_complex attacker",
                RecordingFramerate = 120,
                RecordingResolutionX = 3840,
                RecordingResolutionY = 2160
            }
        };

        AkronSetupPacks.Apply(target, session: null, pack, AkronSetupSection.Recorder);

        Assert.Equal("/trusted/output", target.RecordingOutputFolder);
        Assert.Equal("trusted-template", target.RecordingFilenameTemplate);
        Assert.Equal(AkronRecordingReplayAutoStart.Off, target.RecordingReplayAutoStart);
        Assert.Equal("trusted-filter", target.RecordingColorspaceArgs);
        Assert.Equal(120, target.RecordingFramerate);
        Assert.Equal(3840, target.RecordingResolutionX);
        Assert.Equal(2160, target.RecordingResolutionY);
    }

    [Fact]
    public void RecorderImportRejectsUnsafePortableResourceValues() {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Recorder,
            State = new AkronSetupState {
                RecordingFramerate = 121,
                RecordingResolutionX = 3840,
                RecordingResolutionY = 2160
            }
        };

        Assert.Throws<InvalidDataException>(() => AkronSetupPacks.Apply(new AkronModuleSettings(), null, pack));
    }

    [Fact]
    public void CaptureUsesCustomExportName() {
        AkronModuleSettings settings = new AkronModuleSettings();

        AkronSetupPack pack = AkronSetupPacks.Capture(settings, session: null, " Named Setup ", AkronSetupSection.Hud);

        Assert.Equal("Named Setup", pack.Name);
        Assert.Equal(AkronSetupSection.Hud, pack.Section);
    }

    [Fact]
    public void NonStartPosArchiveDoesNotRequireStartPosSnapshots() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-hud-setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "hud.akr");
        try {
            AkronModuleSession session = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [1] = new AkronStartPos {
                        Room = "a-00",
                        AreaSid = "Celeste/1-ForsakenCity",
                        StateSlotName = "missing-snapshot"
                    }
                }
            };

            AkronSetupPacks.Write(new AkronModuleSettings(), session, path, "HUD", AkronSetupSection.Hud);

            AkronSetupPack pack = AkronSetupPacks.Read(path);
            Assert.Equal(AkronSetupSection.Hud, pack.Section);
            Assert.Empty(pack.StartPositions);
        } finally {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteIncludesSingleStartPosMapSidInArchiveManifest() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-setup-map-sid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "startpos.akr");
        try {
            AkronModuleSession session = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [1] = new AkronStartPos {
                        Position = new Vector2(1f, 2f),
                        Room = "a-00",
                        AreaSid = "SpringCollab2020/1-Beginner",
                        StateSlotName = SavePackSnapshot("SpringCollab2020/1-Beginner", "a-00", 1)
                    },
                    [2] = new AkronStartPos {
                        Position = new Vector2(3f, 4f),
                        Room = "a-01",
                        AreaSid = "SpringCollab2020/1-Beginner",
                        StateSlotName = SavePackSnapshot("SpringCollab2020/1-Beginner", "a-01", 2)
                    }
                }
            };

            AkronSetupPacks.Write(new AkronModuleSettings(), session, path, "StartPos", AkronSetupSection.StartPos);

            AkronArchive.ReadPayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                1024 * 1024,
                AkronSetupPacks.MaxStartPositions,
                512L * 1024L * 1024L,
                out AkronArchiveManifest manifest,
                out _);
            Assert.Equal("SpringCollab2020/1-Beginner", manifest.Target.MapSid);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(AkronActions.GetStartPosStateSlotName("SpringCollab2020/1-Beginner", 1));
            AkronStartPosReconstruction.DeleteSnapshot(AkronActions.GetStartPosStateSlotName("SpringCollab2020/1-Beginner", 2));
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void StartPosCaptureDoesNotExportPositionsWhenCurrentMapIsAmbiguous() {
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [1] = new AkronStartPos { Room = "a", AreaSid = "Map/A" },
                [2] = new AkronStartPos { Room = "b", AreaSid = "Map/B" }
            }
        };

        AkronSetupPack pack = AkronSetupPacks.Capture(new AkronModuleSettings(), session, section: AkronSetupSection.StartPos);

        Assert.Empty(pack.StartPositions);
    }

    [Fact]
    public void WholeSetupPreservesInvincibilityModeAndAkronSideEffects() {
        AkronModuleSettings settings = new AkronModuleSettings {
            Invincibility = true,
            InvincibilityMode = AkronInvincibilityMode.Native,
            InvincibilityBottomlessFallRescue = true,
            InvincibilityCrushCollisionChanges = true,
            InvincibilityLavaIcePushback = true,
            InvincibilitySpikeGroundRefills = true
        };

        AkronSetupPack pack = AkronSetupPacks.Capture(settings, session: null, "Invincibility Setup", AkronSetupSection.Whole);
        AkronModuleSettings target = new AkronModuleSettings();

        AkronSetupPacks.Apply(target, session: null, pack, AkronSetupSection.Whole);

        Assert.True(pack.State.Invincibility);
        Assert.Equal(AkronInvincibilityMode.Native, pack.State.InvincibilityMode);
        Assert.True(pack.State.InvincibilityBottomlessFallRescue);
        Assert.True(pack.State.InvincibilityCrushCollisionChanges);
        Assert.True(pack.State.InvincibilityLavaIcePushback);
        Assert.True(pack.State.InvincibilitySpikeGroundRefills);
        Assert.True(target.Invincibility);
        Assert.Equal(AkronInvincibilityMode.Native, target.InvincibilityMode);
        Assert.True(target.InvincibilityBottomlessFallRescue);
        Assert.True(target.InvincibilityCrushCollisionChanges);
        Assert.True(target.InvincibilityLavaIcePushback);
        Assert.True(target.InvincibilitySpikeGroundRefills);
    }

    [Fact]
    public void WholeSetupPreservesCursorToolAndFreeCameraMouseSettings() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CursorZoom = false,
            CursorTools = true,
            CursorToolsClickAction = AkronCursorToolsClickAction.InspectorPin,
            CursorToolsCursorZoom = true,
            CursorToolsFreeCamera = false,
            CursorToolsFreezeGameplay = true,
            FrameStepper = true,
            FreeCamera = false,
            FreeCameraSpeed = 360,
            FreeCameraFreezeGameplay = false,
            FreeCameraMouseControl = true,
            ClickTeleport = false
        };

        AkronSetupPack pack = AkronSetupPacks.Capture(settings, session: null, "Cursor Tools", AkronSetupSection.Whole);
        AkronModuleSettings target = new AkronModuleSettings();

        AkronSetupPacks.Apply(target, session: null, pack, AkronSetupSection.Whole);

        Assert.True(pack.State.CursorTools);
        Assert.Equal(AkronCursorToolsClickAction.InspectorPin, pack.State.CursorToolsClickAction);
        Assert.True(pack.State.CursorToolsCursorZoom);
        Assert.False(pack.State.CursorToolsFreeCamera);
        Assert.True(pack.State.CursorToolsFreezeGameplay);
        Assert.True(pack.State.FrameStepper);
        Assert.True(pack.State.FreeCameraMouseControl);
        Assert.True(target.CursorTools);
        Assert.Equal(AkronCursorToolsClickAction.InspectorPin, target.CursorToolsClickAction);
        Assert.True(target.CursorToolsCursorZoom);
        Assert.False(target.CursorToolsFreeCamera);
        Assert.True(target.CursorToolsFreezeGameplay);
        Assert.True(target.FrameStepper);
        Assert.True(target.FreeCameraMouseControl);
        Assert.Equal(360, target.FreeCameraSpeed);
        Assert.False(target.FreeCameraFreezeGameplay);
    }

    [Fact]
    public void ScopedAudioImportAppliesOnlyAudioState() {
        AkronModuleSettings target = new AkronModuleSettings {
            SmartStartPos = true,
            StartPosSlotCount = 4,
            AudioSpeed = false,
            PitchShift = false,
            AudioSplitter = false
        };
        target.SoundVolumes["bird-squawk"] = 100;
        target.SoundVolumeOverrides["bird-squawk"] = false;

        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.Audio,
            State = new AkronSetupState {
                SmartStartPos = false,
                StartPosSlotCount = 9,
                AudioSpeed = true,
                AudioSpeedPolicy = AkronAudioSpeedPolicy.Independent,
                AudioSpeedMultiplier = 1.5f,
                PitchShift = true,
                PitchShiftPolicy = AkronPitchPolicy.Independent,
                PitchShiftMultiplier = 0.75f,
                SoundVolumes = new Dictionary<string, int> {
                    ["bird-squawk"] = 150
                },
                SoundVolumeOverrides = new Dictionary<string, bool> {
                    ["bird-squawk"] = true
                },
                AudioSplitter = true,
                AudioSplitterMainDevice = "Main Device",
                AudioSplitterMusicDevice = "Music Device",
                AudioSplitterSfxDevice = "SFX Device"
            }
        };

        AkronSetupPacks.Apply(target, session: null, pack, AkronSetupSection.Audio);

        Assert.True(target.SmartStartPos);
        Assert.Equal(4, target.StartPosSlotCount);
        Assert.True(target.AudioSpeed);
        Assert.Equal(AkronAudioSpeedPolicy.Independent, target.AudioSpeedPolicy);
        Assert.Equal(1.5f, target.AudioSpeedMultiplier);
        Assert.True(target.PitchShift);
        Assert.Equal(AkronPitchPolicy.Independent, target.PitchShiftPolicy);
        Assert.Equal(0.75f, target.PitchShiftMultiplier);
        Assert.Equal(150, target.SoundVolumes["bird-squawk"]);
        Assert.True(target.SoundVolumeOverrides["bird-squawk"]);
        Assert.False(target.AudioSplitter);
        Assert.Equal("Default", target.AudioSplitterMainDevice);
        Assert.Equal("Default", target.AudioSplitterMusicDevice);
        Assert.Equal("Default", target.AudioSplitterSfxDevice);
    }

    [Fact]
    public void ScopedStartPosImportPreservesMapLocalSlotsWithoutReplacingAudioState() {
        AkronModuleSettings target = new AkronModuleSettings {
            AudioSpeed = true,
            AudioSpeedMultiplier = 1.25f,
            SmartStartPos = false,
            StartPosWaitForInput = false,
            StartPosSlotCount = 3
        };
        target.SoundVolumes["bird-squawk"] = 125;
        target.SoundVolumeOverrides["bird-squawk"] = true;
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [2] = new AkronStartPos {
                    Position = new Vector2(1f, 2f),
                    Room = "old",
                    AreaSid = "Old/Map"
                },
                [3] = new AkronStartPos {
                    Position = new Vector2(3f, 4f),
                    Room = "stale",
                    AreaSid = "New/Map"
                }
            }
        };

        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.StartPos,
            State = new AkronSetupState {
                AudioSpeed = false,
                AudioSpeedMultiplier = 0.5f,
                SmartStartPos = true,
                StartPosWaitForInput = true,
                StartPosSlotCount = 7,
                StartPosConfiguredDashes = 2,
                StartPosConfiguredStaminaPercent = 80
            },
            StartPositions = new Dictionary<int, AkronStartPosPackEntry> {
                [2] = new AkronStartPosPackEntry {
                    X = 12.5f,
                    Y = 34.25f,
                    Room = "new-room",
                    AreaSid = "New/Map",
                    UsesSpawnConfig = true,
                    Dashes = 2,
                    StaminaPercent = 80,
                    Facing = AkronStartPosFacing.Left,
                    Idle = true,
                    Grab = true
                }
            }
        };

        AkronSetupPacks.Apply(target, session, pack, AkronSetupSection.StartPos);

        Assert.True(target.AudioSpeed);
        Assert.Equal(1.25f, target.AudioSpeedMultiplier);
        Assert.Equal(125, target.SoundVolumes["bird-squawk"]);
        Assert.True(target.SoundVolumeOverrides["bird-squawk"]);
        Assert.True(target.SmartStartPos);
        Assert.True(target.StartPosWaitForInput);
        Assert.Equal(7, target.StartPosSlotCount);
        Assert.Equal(2, target.StartPosConfiguredDashes);
        Assert.Equal(80, target.StartPosConfiguredStaminaPercent);
        AkronStartPos imported = Assert.Single(session.StartPositions).Value;
        Assert.Equal("new-room", imported.Room);
        Assert.Equal("New/Map", imported.AreaSid);
        Assert.True(imported.UsesSpawnConfig);
        Assert.Equal(2, imported.Dashes);
        Assert.Equal(80, imported.StaminaPercent);
        Assert.Equal(AkronStartPosFacing.Left, imported.Facing);
        Assert.True(imported.Idle);
        Assert.True(imported.Grab);
        Assert.Equal(AkronActions.GetStartPosStateSlotName("New/Map", 2), imported.StateSlotName);
    }

    [Fact]
    public void FailedStartPosMetadataSaveRestoresSettingsAndSessionState() {
        AkronModuleSettings settings = new AkronModuleSettings {
            SmartStartPos = false,
            StartPosSlotCount = 3
        };
        Dictionary<int, AkronStartPos> previousStartPositions = new Dictionary<int, AkronStartPos> {
            [2] = new AkronStartPos {
                Position = new Vector2(1f, 2f),
                Room = "old-room",
                AreaSid = "Map/A"
            }
        };
        AkronModuleSession session = new AkronModuleSession {
            LoadedStartPositionsAreaSid = "Map/A",
            StartPositions = previousStartPositions,
            LastLoadedStartPosSlot = 2
        };
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.StartPos,
            ArchiveMapSid = "Map/A",
            State = new AkronSetupState {
                SmartStartPos = true,
                StartPosSlotCount = 9
            },
            StartPositions = new Dictionary<int, AkronStartPosPackEntry> {
                [4] = new AkronStartPosPackEntry {
                    X = 40f,
                    Y = 80f,
                    Room = "new-room",
                    AreaSid = "Map/A"
                }
            }
        };

        Assert.Throws<IOException>(() => AkronSetupPacks.Apply(
            settings,
            session,
            pack,
            AkronSetupSection.StartPos,
            persistStartPosMetadata: () => false));

        Assert.False(settings.SmartStartPos);
        Assert.Equal(3, settings.StartPosSlotCount);
        Assert.Same(previousStartPositions, session.StartPositions);
        Assert.Equal("Map/A", session.LoadedStartPositionsAreaSid);
        Assert.Equal(2, session.LastLoadedStartPosSlot);
    }

    [Fact]
    public void FailedStartPosMetadataSaveRestoresSnapshotsForRemovedSlots() {
        const string areaSid = "Tests/FailedImportSnapshotRollback";
        string oldSlotName = AkronActions.GetStartPosStateSlotName(areaSid, 1);
        string importedSlotName = AkronActions.GetStartPosStateSlotName(areaSid, 2);
        string directory = Path.Combine(Path.GetTempPath(), "akron-failed-import-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "startpos.akr");
        Directory.CreateDirectory(directory);
        try {
            Assert.Equal(oldSlotName, SavePackSnapshot(areaSid, "old-room", 1));
            Assert.Equal(importedSlotName, SavePackSnapshot(areaSid, "new-room", 2));
            AkronModuleSession sourceSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [2] = new AkronStartPos {
                        Room = "new-room",
                        AreaSid = areaSid,
                        StateSlotName = importedSlotName
                    }
                }
            };
            AkronSetupPacks.Write(
                new AkronModuleSettings(),
                sourceSession,
                archivePath,
                section: AkronSetupSection.StartPos);
            AkronStartPosReconstruction.DeleteSnapshot(importedSlotName);
            AkronModuleSession targetSession = new AkronModuleSession {
                LoadedStartPositionsAreaSid = areaSid,
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [1] = new AkronStartPos {
                        Room = "old-room",
                        AreaSid = areaSid,
                        StateSlotName = oldSlotName
                    }
                }
            };

            AkronSetupPack pack = AkronSetupPacks.Read(archivePath);
            Assert.Throws<IOException>(() => AkronSetupPacks.Apply(
                new AkronModuleSettings(),
                targetSession,
                pack,
                AkronSetupSection.StartPos,
                persistStartPosMetadata: () => false));

            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                oldSlotName,
                out AkronReconstructionDocument restoredOldSnapshot,
                out string restoreError), restoreError);
            Assert.Equal("old-room", restoredOldSnapshot.Room);
            Assert.False(AkronStartPosReconstruction.HasSnapshot(importedSlotName));
            Assert.Equal("old-room", Assert.Single(targetSession.StartPositions).Value.Room);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(oldSlotName);
            AkronStartPosReconstruction.DeleteSnapshot(importedSlotName);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void EmptyScopedStartPosImportClearsActivePositionsForItsMap() {
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [1] = new AkronStartPos { Room = "clear", AreaSid = "Map/A" },
                [2] = new AkronStartPos { Room = "keep", AreaSid = "Map/B" }
            }
        };
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.StartPos,
            ArchiveMapSid = "Map/A",
            State = new AkronSetupState(),
            StartPositions = new Dictionary<int, AkronStartPosPackEntry>()
        };

        AkronSetupPacks.Apply(new AkronModuleSettings(), session, pack, AkronSetupSection.StartPos);

        AkronStartPos remaining = Assert.Single(session.StartPositions).Value;
        Assert.Equal("Map/B", remaining.AreaSid);
    }

    [Fact]
    public void StartPosArchiveRoundTripCarriesItsExactSnapshot() {
        string areaSid = "Maps/Current";
        int slot = 4;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot);
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-pack-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "startpos.akr");
        Directory.CreateDirectory(directory);
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [slot] = new AkronStartPos {
                    Position = new Vector2(12f, 34f),
                    Room = "room-a",
                    AreaSid = areaSid,
                    StateSlotName = stateSlotName
                }
            }
        };
        AkronReconstructionGraph graph = new AkronReconstructionGraph(type => false);
        AkronReconstructionCapture capture = graph.Capture(
            new PackSnapshotState { Counter = 91 },
            new PackSnapshotState());
        Assert.True(capture.Success, capture.Error);
        capture.Document.BerryProgress = new AkronBerryProgressSnapshot {
            Strawberries = new List<AkronSessionEntityId> {
                new AkronSessionEntityId { Level = "exporter-room", ID = 7 }
            }
        };

        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                stateSlotName,
                areaSid,
                "room-a",
                0,
                capture.Document,
                out string saveError), saveError);

            AkronSetupPacks.Write(new AkronModuleSettings(), session, archivePath, "Runtime StartPos", AkronSetupSection.StartPos);
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            Assert.False(AkronStartPosReconstruction.HasSnapshot(stateSlotName));

            AkronSetupPack pack = AkronSetupPacks.Read(archivePath);
            AkronModuleSession importedSession = new AkronModuleSession();
            AkronSetupPacks.Apply(new AkronModuleSettings(), importedSession, pack, AkronSetupSection.StartPos);

            AkronStartPos imported = Assert.Single(importedSession.StartPositions).Value;
            Assert.Equal(stateSlotName, imported.StateSlotName);
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                imported.StateSlotName,
                out AkronReconstructionDocument importedDocument,
                out string loadError), loadError);
            Assert.Equal(SaveData.Instance?.FileSlot ?? -1, importedDocument.FileSlot);
            Assert.Null(importedDocument.BerryProgress);
            PackSnapshotState restoredState = new PackSnapshotState();
            AkronReconstructionRestore restored = graph.Restore(importedDocument, restoredState);
            Assert.True(restored.Success, restored.Error);
            Assert.Equal(91, restoredState.Counter);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // The pack half of the v7 -> v8 format bump.
    //
    // A StartPos or Whole pack carries one reconstruction document per slot, and those
    // documents name room objects by where they sit in a clean reload of the room. Two
    // changes moved that baseline, so a pack written by an older Akron describes a room
    // this build does not produce, and a shifted index can hand one entity another
    // same-typed entity's state instead of refusing. The pack contract is versioned in
    // lockstep with the document so the refusal happens before any attachment is read.

    [Fact]
    public void ASetupPackFromAnOlderAkronIsRefusedWithAMessageThatSaysWhatToDo() {
        AkronSetupPack pack = AkronSetupPacks.Capture(
            new AkronModuleSettings(), session: null, section: AkronSetupSection.Keybinds);
        pack.Format = "akron-setup-v4";

        AkronSetupPackFormatException refusal = Assert.Throws<AkronSetupPackFormatException>(
            () => AkronSetupPacks.Apply(new AkronModuleSettings(), new AkronModuleSession(), pack));

        Assert.Contains("akron-setup-v4", refusal.Message);
        Assert.Contains(AkronSetupPacks.SetupPackFormat, refusal.Message);
        Assert.Contains("built rooms differently", refusal.Message);
        Assert.Contains("export it again from this build", refusal.Message);
    }

    [Fact]
    public void AnArchiveWhosePayloadPredatesTheFormatBumpIsRefusedWhenItIsRead() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-older-pack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, "older.akr");
        try {
            // A Keybinds pack carries no StartPos attachment at all, so this also pins
            // the deliberate part of the decision: the contract covers the whole pack,
            // not only the sections that hold room state, because one story about packs
            // from an older Akron is easier to act on than a rule about which sections
            // happen to survive.
            AkronSetupPack pack = AkronSetupPacks.Capture(
                new AkronModuleSettings(), session: null, section: AkronSetupSection.Keybinds);
            string payload = AkronSetupPacks.SerializePackPayloadForArchive(pack)
                .Replace(AkronSetupPacks.SetupPackFormat, "akron-setup-v4", StringComparison.Ordinal);
            AkronArchive.WriteSinglePayloadArchive(
                archivePath, PackManifest(pack), AkronSetupPacks.SetupArchivePayload, payload);

            AkronSetupPackFormatException refusal = Assert.Throws<AkronSetupPackFormatException>(
                () => AkronSetupPacks.Read(archivePath));

            Assert.Contains("akron-setup-v4", refusal.Message);
            Assert.Contains(AkronSetupPacks.SetupPackFormat, refusal.Message);
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void AStartPosPackNamesItsSnapshotAttachmentAfterTheCurrentDocumentFormat() {
        const string areaSid = "Maps/EntryName";
        const int slot = 6;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot);
        string directory = Path.Combine(Path.GetTempPath(), "akron-entry-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string archivePath = Path.Combine(directory, "startpos.akr");
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [slot] = new AkronStartPos {
                    Room = "room-a",
                    AreaSid = areaSid,
                    StateSlotName = stateSlotName
                }
            }
        };
        try {
            Assert.True(AkronStartPosReconstruction.SaveSnapshot(
                stateSlotName, areaSid, "room-a", 0, MinimalPackDocument(), out string saveError), saveError);
            AkronSetupPacks.Write(new AkronModuleSettings(), session, archivePath, "Entry Name", AkronSetupSection.StartPos);

            AkronSetupPack written = AkronSetupPacks.Read(archivePath);

            // The attachment name states which fresh-room baseline the document inside
            // it was measured against, so it tracks the document format rather than the
            // pack format. A stale name here would let a v7 attachment ride inside a
            // pack that claims to be current.
            Assert.Equal("startpos/6.v8.json.gz", Assert.Single(written.StartPositions).Value.SnapshotEntry);
            Assert.Equal(AkronSetupPacks.SetupPackFormat, written.Format);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static AkronReconstructionDocument MinimalPackDocument() {
        AkronReconstructionGraph graph = new AkronReconstructionGraph(type => false);
        AkronReconstructionCapture capture = graph.Capture(
            new PackSnapshotState { Counter = 5 },
            new PackSnapshotState());
        Assert.True(capture.Success, capture.Error);
        return capture.Document;
    }

    [Fact]
    public void StartPosImportRejectsAChangedSnapshotBeforeApplyingSetupState() {
        const string areaSid = "Maps/ChangedSnapshot";
        const string room = "room-a";
        const int slot = 1;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot);
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-pack-changed-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "startpos.akr");
        Directory.CreateDirectory(directory);
        try {
            Assert.Equal(stateSlotName, SavePackSnapshot(areaSid, room, slot));
            AkronModuleSettings sourceSettings = new AkronModuleSettings { SmartStartPos = true };
            AkronModuleSession sourceSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [slot] = new AkronStartPos {
                        Room = room,
                        AreaSid = areaSid,
                        StateSlotName = stateSlotName
                    }
                }
            };
            AkronSetupPacks.Write(sourceSettings, sourceSession, archivePath, section: AkronSetupSection.StartPos);
            using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update)) {
                ZipArchiveEntry snapshot = Assert.Single(archive.Entries, entry => entry.FullName.StartsWith("startpos/", StringComparison.Ordinal));
                string entryName = snapshot.FullName;
                snapshot.Delete();
                using Stream changed = archive.CreateEntry(entryName).Open();
                changed.Write(new byte[] { 1, 2, 3, 4 });
            }

            AkronSetupPack pack = AkronSetupPacks.Read(archivePath);
            AkronModuleSettings targetSettings = new AkronModuleSettings { SmartStartPos = false };
            AkronModuleSession targetSession = new AkronModuleSession();

            Assert.Throws<InvalidDataException>(() =>
                AkronSetupPacks.Apply(targetSettings, targetSession, pack, AkronSetupSection.StartPos));
            Assert.False(targetSettings.SmartStartPos);
            Assert.Empty(targetSession.StartPositions);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void FailedSnapshotInstallationRestoresThePreviousStartPosFile() {
        const string areaSid = "Maps/InstallRollback";
        const int firstSlot = 1;
        const int blockedSlot = 2;
        string firstStateSlot = AkronActions.GetStartPosStateSlotName(areaSid, firstSlot);
        string blockedStateSlot = AkronActions.GetStartPosStateSlotName(areaSid, blockedSlot);
        string firstSnapshotPath = AkronStartPosReconstruction.GetSnapshotPath(firstStateSlot);
        string blockedSnapshotPath = AkronStartPosReconstruction.GetSnapshotPath(blockedStateSlot);
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-install-rollback-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "startpos.akr");
        string oldSnapshotCopy = Path.Combine(directory, "old.json.gz");
        Directory.CreateDirectory(directory);
        try {
            Assert.Equal(firstStateSlot, SavePackSnapshot(areaSid, "old-room", firstSlot));
            File.Copy(firstSnapshotPath, oldSnapshotCopy);
            Assert.Equal(firstStateSlot, SavePackSnapshot(areaSid, "new-room", firstSlot));
            Assert.Equal(blockedStateSlot, SavePackSnapshot(areaSid, "blocked-room", blockedSlot));
            AkronModuleSession sourceSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [firstSlot] = new AkronStartPos {
                        Room = "new-room",
                        AreaSid = areaSid,
                        StateSlotName = firstStateSlot
                    },
                    [blockedSlot] = new AkronStartPos {
                        Room = "blocked-room",
                        AreaSid = areaSid,
                        StateSlotName = blockedStateSlot
                    }
                }
            };
            AkronSetupPacks.Write(
                new AkronModuleSettings { SmartStartPos = true },
                sourceSession,
                archivePath,
                section: AkronSetupSection.StartPos);
            File.Copy(oldSnapshotCopy, firstSnapshotPath, overwrite: true);
            AkronStartPosReconstruction.DeleteSnapshot(blockedStateSlot);
            Directory.CreateDirectory(blockedSnapshotPath);
            AkronModuleSession targetSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [firstSlot] = new AkronStartPos { Room = "old-room", AreaSid = areaSid, StateSlotName = firstStateSlot }
                }
            };
            AkronSetupPack pack = AkronSetupPacks.Read(archivePath);
            AkronModuleSettings targetSettings = new AkronModuleSettings { SmartStartPos = false };

            Assert.ThrowsAny<IOException>(() =>
                AkronSetupPacks.Apply(targetSettings, targetSession, pack, AkronSetupSection.StartPos));

            Assert.False(targetSettings.SmartStartPos);
            Assert.Equal("old-room", Assert.Single(targetSession.StartPositions).Value.Room);
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                firstStateSlot,
                out AkronReconstructionDocument restored,
                out string loadError), loadError);
            Assert.Equal("old-room", restored.Room);
        } finally {
            if (Directory.Exists(blockedSnapshotPath)) {
                Directory.Delete(blockedSnapshotPath, recursive: true);
            }
            AkronStartPosReconstruction.DeleteSnapshot(firstStateSlot);
            AkronStartPosReconstruction.DeleteSnapshot(blockedStateSlot);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void EmptyStartPosImportRestoresRemovedSnapshotWhenMetadataPersistenceFails() {
        const string areaSid = "Maps/EmptyImportRollback";
        const int slot = 1;
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot);
        string directory = Path.Combine(Path.GetTempPath(), "akron-empty-startpos-rollback-" + Guid.NewGuid().ToString("N"));
        string archivePath = Path.Combine(directory, "empty.akr");
        Directory.CreateDirectory(directory);
        try {
            Assert.Equal(stateSlotName, SavePackSnapshot(areaSid, "old-room", slot));
            AkronSetupPack emptyPack = new AkronSetupPack {
                Section = AkronSetupSection.StartPos,
                ArchiveMapSid = areaSid,
                CreatedUtc = DateTime.UtcNow.ToString("O")
            };
            AkronSetupPacks.WriteArchive(archivePath, emptyPack, PackManifest(emptyPack));
            AkronSetupPack pack = AkronSetupPacks.Read(archivePath);
            AkronModuleSession targetSession = new AkronModuleSession {
                StartPositions = new Dictionary<int, AkronStartPos> {
                    [slot] = new AkronStartPos {
                        Room = "old-room",
                        AreaSid = areaSid,
                        StateSlotName = stateSlotName
                    }
                }
            };

            Assert.Throws<IOException>(() => AkronSetupPacks.Apply(
                new AkronModuleSettings(),
                targetSession,
                pack,
                AkronSetupSection.StartPos,
                () => false));

            Assert.Equal("old-room", Assert.Single(targetSession.StartPositions).Value.Room);
            Assert.True(AkronStartPosReconstruction.TryLoadSnapshot(
                stateSlotName,
                out AkronReconstructionDocument restored,
                out string loadError), loadError);
            Assert.Equal("old-room", restored.Room);
        } finally {
            AkronStartPosReconstruction.DeleteSnapshot(stateSlotName);
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExportRejectsASnapshotLargerThanTheImportLimitBeforeHashingIt() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-export-size-" + Guid.NewGuid().ToString("N"));
        string snapshotPath = Path.Combine(directory, "oversized.json.gz");
        string archivePath = Path.Combine(directory, "startpos.akr");
        Directory.CreateDirectory(directory);
        try {
            using (FileStream snapshot = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                snapshot.SetLength((long) AkronSetupPacks.MaxSnapshotAttachmentBytes + 1L);
            }
            AkronSetupPack pack = PackWithSnapshotPaths(new Dictionary<int, string> { [1] = snapshotPath });

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                AkronSetupPacks.WriteArchive(archivePath, pack, PackManifest(pack)));

            Assert.Contains("snapshot is too large to export", exception.Message);
            Assert.False(File.Exists(archivePath));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ExportRejectsSnapshotTotalsLargerThanTheImportLimitBeforeHashingThem() {
        string directory = Path.Combine(Path.GetTempPath(), "akron-startpos-export-total-" + Guid.NewGuid().ToString("N"));
        string snapshotPath = Path.Combine(directory, "large.json.gz");
        string archivePath = Path.Combine(directory, "startpos.akr");
        Directory.CreateDirectory(directory);
        try {
            long snapshotBytes = AkronSetupPacks.MaxSnapshotAttachmentsBytes / 5L + 1L;
            Assert.True(snapshotBytes < AkronSetupPacks.MaxSnapshotAttachmentBytes);
            using (FileStream snapshot = new FileStream(snapshotPath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                snapshot.SetLength(snapshotBytes);
            }
            AkronSetupPack pack = PackWithSnapshotPaths(Enumerable.Range(1, 5).ToDictionary(slot => slot, _ => snapshotPath));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
                AkronSetupPacks.WriteArchive(archivePath, pack, PackManifest(pack)));

            Assert.Contains("attachments are too large to export", exception.Message);
            Assert.False(File.Exists(archivePath));
        } finally {
            if (Directory.Exists(directory)) {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private sealed class PackSnapshotState {
        public int Counter;
    }

    private static AkronSetupPack PackWithSnapshotPaths(Dictionary<int, string> snapshotPaths) {
        AkronSetupPack pack = new AkronSetupPack {
            Section = AkronSetupSection.StartPos,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ArchiveMapSid = "Maps/ExportLimits"
        };
        foreach (KeyValuePair<int, string> snapshot in snapshotPaths) {
            pack.StartPositions[snapshot.Key] = new AkronStartPosPackEntry {
                AreaSid = pack.ArchiveMapSid,
                Room = "room-" + snapshot.Key,
                SnapshotEntry = "startpos/" + snapshot.Key + ".v8.json.gz",
                SnapshotSha256 = new string('0', 64)
            };
            pack.SnapshotSourcePaths[snapshot.Key] = snapshot.Value;
        }
        return pack;
    }

    private static AkronArchiveManifest PackManifest(AkronSetupPack pack) {
        return new AkronArchiveManifest {
            Kind = AkronSetupPacks.SetupArchiveKind,
            KindVersion = 1,
            CreatedAt = pack.CreatedUtc,
            Target = new AkronArchiveTarget { Game = "Celeste", MapSid = pack.ArchiveMapSid }
        };
    }

    private static string SavePackSnapshot(string areaSid, string room, int slot) {
        string stateSlotName = AkronActions.GetStartPosStateSlotName(areaSid, slot);
        AkronReconstructionGraph graph = new AkronReconstructionGraph(type => false);
        AkronReconstructionCapture capture = graph.Capture(new PackSnapshotState { Counter = slot }, new PackSnapshotState());
        Assert.True(capture.Success, capture.Error);
        Assert.True(AkronStartPosReconstruction.SaveSnapshot(
            stateSlotName,
            areaSid,
            room,
            0,
            capture.Document,
            out string saveError), saveError);
        return stateSlotName;
    }
}

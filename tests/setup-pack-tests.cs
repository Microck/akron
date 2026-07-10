using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SetupPackTests {
    [Fact]
    public void WholeArchiveRoundTripPreservesPortableMenuBindingsAndCurrentMapStartPositions() {
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
                        AreaSid = "Celeste/1-ForsakenCity"
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
            Assert.Equal("Celeste/1-ForsakenCity", imported.AreaSid);
            Assert.Equal("/trusted/target", target.RecordingOutputFolder);

            string payload = AkronArchive.ReadSinglePayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                2 * 1024 * 1024,
                out _);
            Assert.DoesNotContain("/private/source", payload, StringComparison.Ordinal);
        } finally {
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
                        AreaSid = "SpringCollab2020/1-Beginner"
                    },
                    [2] = new AkronStartPos {
                        Position = new Vector2(3f, 4f),
                        Room = "a-01",
                        AreaSid = "SpringCollab2020/1-Beginner"
                    }
                }
            };

            AkronSetupPacks.Write(new AkronModuleSettings(), session, path, "StartPos", AkronSetupSection.StartPos);

            AkronArchive.ReadSinglePayloadArchive(
                path,
                AkronSetupPacks.SetupArchiveKind,
                AkronSetupPacks.SetupArchivePayload,
                1024 * 1024,
                out AkronArchiveManifest manifest);
            Assert.Equal("SpringCollab2020/1-Beginner", manifest.Target.MapSid);
        } finally {
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
    public void ScopedStartPosImportAppliesSlotsWithoutReplacingAudioState() {
        AkronModuleSettings target = new AkronModuleSettings {
            AudioSpeed = true,
            AudioSpeedMultiplier = 1.25f,
            SmartStartPos = false,
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
        Assert.Equal(7, target.StartPosSlotCount);
        Assert.Equal(2, target.StartPosConfiguredDashes);
        Assert.Equal(80, target.StartPosConfiguredStaminaPercent);
        Assert.Equal(2, session.StartPositions.Count);
        Assert.Equal("Old/Map", session.StartPositions[2].AreaSid);
        AkronStartPos imported = session.StartPositions[3];
        Assert.Equal("new-room", imported.Room);
        Assert.Equal("New/Map", imported.AreaSid);
        Assert.True(imported.UsesSpawnConfig);
        Assert.Equal(2, imported.Dashes);
        Assert.Equal(80, imported.StaminaPercent);
        Assert.Equal(AkronStartPosFacing.Left, imported.Facing);
        Assert.True(imported.Idle);
        Assert.True(imported.Grab);
        Assert.Empty(imported.ImportedRoomStateSnapshot);
        Assert.Empty(imported.SnapshotPath);
        Assert.Empty(imported.StateSlotName);
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
    public void StartPosExportIncludesPortableRoomStateWhenRuntimeSnapshotExists() {
        string areaSid = "Maps/Current";
        int slot = 4;
        string stateSlotName = AkronActions.GetStartPosStateSlotNameForSetupPack(areaSid, slot);
        AkronSaveLoadSlot runtimeSlot = new AkronSaveLoadSlot(stateSlotName, "room-a", areaSid, saveTimeAndDeaths: true) {
            FileSlot = -1,
            PlayerPosition = new Vector2(12f, 34f),
            Time = 12345L,
            Deaths = 6,
            DeathsInCurrentLevel = 2,
            SaveDataTime = 45678L,
            SaveDataTotalDeaths = 9,
            AreaTimePlayed = 22222L,
            AreaDeaths = 5,
            LevelTimeActive = 12.5f,
            LevelRawTimeActive = 13.5f,
            SessionFlags = new HashSet<string> { "room-state-flag" },
            SessionCounters = new Dictionary<string, int> { ["switches"] = 3 }
        };

        try {
            AkronSaveLoadService.HydrateRuntimeState(stateSlotName, runtimeSlot);
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

            AkronSetupPack pack = AkronSetupPacks.Capture(new AkronModuleSettings(), session, "Runtime StartPos", AkronSetupSection.StartPos);
            AkronStartPosPackEntry entry = pack.StartPositions[slot];

            Assert.False(string.IsNullOrWhiteSpace(entry.RoomStateSnapshot));
            Assert.True(AkronPersistentStartPosSnapshots.TryDeserializePortableRoomStateForTesting(
                entry.RoomStateSnapshot,
                stateSlotName,
                areaSid,
                out AkronSaveLoadSlot restored,
                out string error), error);
            Assert.False(restored.SaveTimeAndDeaths);
            Assert.Equal(0L, restored.Time);
            Assert.Equal(0, restored.Deaths);
            Assert.Contains("room-state-flag", restored.SessionFlags);
            Assert.Equal(3, restored.SessionCounters["switches"]);
        } finally {
            AkronSaveLoadService.ClearRuntimeState(stateSlotName);
        }
    }

    [Fact]
    public void ScopedNonStartPosExportDoesNotIncludePortableRoomState() {
        string areaSid = "Maps/Current";
        int slot = 4;
        string stateSlotName = AkronActions.GetStartPosStateSlotNameForSetupPack(areaSid, slot);
        AkronSaveLoadSlot runtimeSlot = new AkronSaveLoadSlot(stateSlotName, "room-a", areaSid, saveTimeAndDeaths: false) {
            SessionFlags = new HashSet<string> { "room-state-flag" },
            SessionCounters = new Dictionary<string, int> { ["switches"] = 3 }
        };

        try {
            AkronSaveLoadService.HydrateRuntimeState(stateSlotName, runtimeSlot);
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

            AkronSetupPack pack = AkronSetupPacks.Capture(new AkronModuleSettings(), session, "Audio Only", AkronSetupSection.Audio);

            Assert.True(pack.StartPositions.ContainsKey(slot));
            Assert.Empty(pack.StartPositions[slot].RoomStateSnapshot);
        } finally {
            AkronSaveLoadService.ClearRuntimeState(stateSlotName);
        }
    }

    [Fact]
    public void SetupPackArchiveOmitsPortableRoomStateBeforeWritingUnreadablePayload() {
        string path = Path.Combine(Path.GetTempPath(), "akron-large-startpos-" + Guid.NewGuid().ToString("N") + ".akr");
        AkronSetupPack pack = new AkronSetupPack {
            Name = "Large StartPos",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            Section = AkronSetupSection.StartPos,
            State = new AkronSetupState(),
            StartPositions = new Dictionary<int, AkronStartPosPackEntry> {
                [1] = new AkronStartPosPackEntry {
                    X = 1f,
                    Y = 2f,
                    Room = "room-a",
                    AreaSid = "Maps/Current",
                    RoomStateSnapshot = new string('A', 1400000)
                },
                [2] = new AkronStartPosPackEntry {
                    X = 3f,
                    Y = 4f,
                    Room = "room-a",
                    AreaSid = "Maps/Current",
                    RoomStateSnapshot = new string('B', 1400000)
                }
            }
        };

        try {
            AkronArchive.WriteSinglePayloadArchive(
                path,
                new AkronArchiveManifest {
                    Kind = AkronSetupPacks.SetupArchiveKind,
                    KindVersion = 1,
                    CreatedAt = pack.CreatedUtc,
                    Target = new AkronArchiveTarget {
                        Game = "Celeste",
                        MapSid = "Maps/Current"
                    }
                },
                AkronSetupPacks.SetupArchivePayload,
                AkronSetupPacks.SerializePackPayloadForArchive(pack));

            AkronSetupPack restored = AkronSetupPacks.Read(path);

            Assert.Equal(2, restored.StartPositions.Count);
            Assert.Contains(restored.StartPositions.Values, entry => string.IsNullOrEmpty(entry.RoomStateSnapshot));
            Assert.Contains(restored.StartPositions.Values, entry => !string.IsNullOrEmpty(entry.RoomStateSnapshot));
        } finally {
            if (File.Exists(path)) {
                File.Delete(path);
            }
        }
    }
}

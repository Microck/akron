using System;
using System.Collections.Generic;
using System.IO;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SetupPackTests {
    [Fact]
    public void CaptureUsesCustomExportName() {
        AkronModuleSettings settings = new AkronModuleSettings();

        AkronSetupPack pack = AkronSetupPacks.Capture(settings, session: null, " Named Setup ", AkronSetupSection.Hud);

        Assert.Equal("Named Setup", pack.Name);
        Assert.Equal(AkronSetupSection.Hud, pack.Section);
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
        Assert.True(target.AudioSplitter);
        Assert.Equal("Main Device", target.AudioSplitterMainDevice);
        Assert.Equal("Music Device", target.AudioSplitterMusicDevice);
        Assert.Equal("SFX Device", target.AudioSplitterSfxDevice);
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

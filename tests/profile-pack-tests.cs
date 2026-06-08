using System.Collections.Generic;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ProfilePackTests {
    [Fact]
    public void CaptureUsesCustomExportName() {
        AkronModuleSettings settings = new AkronModuleSettings {
            ActiveProfile = AkronProfile.Practice
        };

        AkronProfilePack pack = AkronProfilePacks.Capture(settings, session: null, " Named Profile ", AkronProfileSection.Hud);

        Assert.Equal("Named Profile", pack.Name);
        Assert.Equal(AkronProfileSection.Hud, pack.Section);
    }

    [Fact]
    public void ScopedAudioImportAppliesOnlyAudioState() {
        AkronModuleSettings target = new AkronModuleSettings {
            ActiveProfile = AkronProfile.Casual,
            SmartStartPos = true,
            StartPosSlotCount = 4,
            AudioSpeed = false,
            PitchShift = false,
            AudioSplitter = false
        };
        target.SoundVolumes["bird-squawk"] = 100;
        target.SoundVolumeOverrides["bird-squawk"] = false;

        AkronProfilePack pack = new AkronProfilePack {
            ActiveProfile = AkronProfile.Practice,
            Section = AkronProfileSection.Audio,
            State = new AkronProfileState {
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

        AkronProfilePacks.Apply(target, session: null, pack, AkronProfileSection.Audio);

        Assert.Equal(AkronProfile.Casual, target.ActiveProfile);
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
            ActiveProfile = AkronProfile.Accessibility,
            AudioSpeed = true,
            AudioSpeedMultiplier = 1.25f,
            SmartStartPos = false,
            StartPosSlotCount = 3
        };
        target.SoundVolumes["bird-squawk"] = 125;
        target.SoundVolumeOverrides["bird-squawk"] = true;
        AkronModuleSession session = new AkronModuleSession {
            StartPositions = new Dictionary<int, AkronStartPos> {
                [1] = new AkronStartPos {
                    Position = new Vector2(1f, 2f),
                    Room = "old",
                    AreaSid = "Old/Map"
                }
            }
        };

        AkronProfilePack pack = new AkronProfilePack {
            ActiveProfile = AkronProfile.MapMaker,
            Section = AkronProfileSection.StartPos,
            State = new AkronProfileState {
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

        AkronProfilePacks.Apply(target, session, pack, AkronProfileSection.StartPos);

        Assert.Equal(AkronProfile.Accessibility, target.ActiveProfile);
        Assert.True(target.AudioSpeed);
        Assert.Equal(1.25f, target.AudioSpeedMultiplier);
        Assert.Equal(125, target.SoundVolumes["bird-squawk"]);
        Assert.True(target.SoundVolumeOverrides["bird-squawk"]);
        Assert.True(target.SmartStartPos);
        Assert.Equal(7, target.StartPosSlotCount);
        Assert.Equal(2, target.StartPosConfiguredDashes);
        Assert.Equal(80, target.StartPosConfiguredStaminaPercent);
        Assert.False(session.StartPositions.ContainsKey(1));
        AkronStartPos imported = Assert.Single(session.StartPositions).Value;
        Assert.Equal("new-room", imported.Room);
        Assert.Equal("New/Map", imported.AreaSid);
        Assert.True(imported.UsesSpawnConfig);
        Assert.Equal(2, imported.Dashes);
        Assert.Equal(80, imported.StaminaPercent);
        Assert.Equal(AkronStartPosFacing.Left, imported.Facing);
        Assert.True(imported.Idle);
        Assert.True(imported.Grab);
    }
}

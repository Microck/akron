using System;
using System.Collections.Generic;
using System.IO;
using Celeste.Mod.Akron;
using Xunit;

namespace Akron.Tests;

public sealed class StartPosConfigurationTests {
    private static AkronStartPos Runtime() => new() {
        Dashes = 5, StaminaPercent = 50, Facing = AkronStartPosFacing.Left,
        Idle = true, Grab = false, UsesSpawnConfig = true, AreaSid = "map", Room = "room"
    };
    private static AkronPersistedStartPos Metadata(string format = "old-format") => new() {
        X = 12, Y = 34, AreaSid = "map", Room = "room", SnapshotFormat = format,
        Dashes = 5, StaminaPercent = 50, Facing = AkronStartPosFacing.Left, Idle = true,
        UsesSpawnConfig = true
    };
    private static AkronStartPos Rebuild(AkronPersistedStartPos p) => new() {
        Dashes = p.Dashes, StaminaPercent = p.StaminaPercent, Facing = p.Facing,
        Idle = p.Idle, Grab = p.Grab, UsesSpawnConfig = p.UsesSpawnConfig
    };

    [Theory]
    [InlineData("")]
    [InlineData("old-format")]
    [InlineData("future-format")]
    public void CommittedEditPreservesSnapshotIdentityAndSurvivesRebuilds(string format) {
        var runtime = Runtime(); var previous = Metadata(format); var other = Metadata();
        var slots = new Dictionary<int, AkronStartPos> { [2] = runtime };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [2] = previous, [7] = other };
        int writes = 0;
        Assert.True(AkronActions.TryApplyStartPosConfigurationEdit(slots, 2, runtime, null!, persisted,
            () => { writes++; Assert.Equal(5, runtime.Dashes); Assert.Equal(2, persisted[2].Dashes); return true; },
            dashes: 2, staminaPercent: 25, facing: AkronStartPosFacing.Right, idle: false, grab: true));
        Assert.Equal(1, writes); Assert.Same(other, persisted[7]); Assert.NotSame(previous, persisted[2]);
        Assert.Equal(5, previous.Dashes); Assert.Equal(format, persisted[2].SnapshotFormat);
        Assert.Equal(12, persisted[2].X); Assert.Equal(34, persisted[2].Y);
        Assert.Equal("room", persisted[2].Room); Assert.Equal("map", persisted[2].AreaSid);
        for (int i = 0; i < 2; i++) {
            var loaded = Rebuild(persisted[2]);
            Assert.Equal(2, loaded.Dashes); Assert.Equal(25, loaded.StaminaPercent);
            Assert.Equal(AkronStartPosFacing.Right, loaded.Facing); Assert.False(loaded.Idle);
            Assert.True(loaded.Grab); Assert.True(loaded.UsesSpawnConfig);
        }
    }

    [Fact]
    public void SingleFieldEditPreservesOtherSlotSpecificValues() {
        var runtime = Runtime(); var slots = new Dictionary<int, AkronStartPos> { [3] = runtime };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [3] = Metadata() };
        Assert.True(AkronActions.TryApplyStartPosConfigurationEdit(slots, 3, runtime, null!, persisted,
            () => true, dashes: 1));
        Assert.Equal(1, runtime.Dashes); Assert.Equal(50, runtime.StaminaPercent);
        Assert.Equal(AkronStartPosFacing.Left, runtime.Facing); Assert.True(runtime.Idle); Assert.False(runtime.Grab);
        Assert.Equal(50, persisted[3].StaminaPercent);
    }

    [Fact]
    public void PendingEditMutatesExactObjectWithoutPublishingMetadata() {
        var runtime = Runtime(); var previous = Metadata();
        var slots = new Dictionary<int, AkronStartPos> { [2] = runtime };
        var pending = new Dictionary<int, AkronStartPos> { [2] = runtime };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [2] = previous };
        Assert.True(AkronActions.TryApplyStartPosConfigurationEdit(slots, 2, runtime, pending, persisted,
            () => throw new Exception("Pending edits must not write"), dashes: 2));
        Assert.Same(runtime, pending[2]); Assert.Equal(2, runtime.Dashes);
        Assert.Same(previous, persisted[2]); Assert.Equal(5, previous.Dashes);
    }

    [Fact]
    public void StaleRuntimeObjectCannotEditReplacement() {
        var stale = Runtime(); var current = Runtime(); var previous = Metadata();
        var slots = new Dictionary<int, AkronStartPos> { [1] = current };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [1] = previous };
        Assert.False(AkronActions.TryApplyStartPosConfigurationEdit(slots, 1, stale, null!, persisted,
            () => throw new Exception("No write expected"), dashes: 2));
        Assert.Equal(5, stale.Dashes); Assert.Equal(5, current.Dashes); Assert.Same(previous, persisted[1]);
    }

    [Fact]
    public void DifferentPendingOwnerRefusesEdit() {
        var runtime = Runtime(); var slots = new Dictionary<int, AkronStartPos> { [1] = runtime };
        var pending = new Dictionary<int, AkronStartPos> { [1] = Runtime() };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [1] = Metadata() };
        Assert.False(AkronActions.TryApplyStartPosConfigurationEdit(slots, 1, runtime, pending, persisted,
            () => throw new Exception("No write expected"), dashes: 2));
        Assert.Equal(5, runtime.Dashes);
    }

    [Fact]
    public void MissingCommittedMetadataDoesNotInventSnapshot() {
        var runtime = Runtime(); var slots = new Dictionary<int, AkronStartPos> { [1] = runtime };
        var persisted = new Dictionary<int, AkronPersistedStartPos>();
        Assert.False(AkronActions.TryApplyStartPosConfigurationEdit(slots, 1, runtime, null!, persisted,
            () => throw new Exception("No write expected"), dashes: 2));
        Assert.Empty(persisted); Assert.Equal(5, runtime.Dashes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSaveRestoresCatalogAndLeavesRuntimeUnchanged(bool throws) {
        var runtime = Runtime(); var previous = Metadata();
        var slots = new Dictionary<int, AkronStartPos> { [1] = runtime };
        var persisted = new Dictionary<int, AkronPersistedStartPos> { [1] = previous };
        bool Edit() => AkronActions.TryApplyStartPosConfigurationEdit(slots, 1, runtime, null!, persisted,
            () => throws ? throw new IOException("test") : false, dashes: 2, staminaPercent: 25);
        if (throws) Assert.Throws<IOException>(() => Edit()); else Assert.False(Edit());
        Assert.Same(previous, persisted[1]); Assert.Equal(5, runtime.Dashes); Assert.Equal(50, runtime.StaminaPercent);
    }

    [Theory]
    [InlineData(-100, -100, -1, -1)]
    [InlineData(100, 1000, 5, 100)]
    public void SuppliedNumericEditsAreClamped(int dash, int stamina, int expectedDash, int expectedStamina) {
        var runtime = Runtime(); var slots = new Dictionary<int, AkronStartPos> { [1] = runtime };
        var pending = new Dictionary<int, AkronStartPos> { [1] = runtime };
        Assert.True(AkronActions.TryApplyStartPosConfigurationEdit(slots, 1, runtime, pending, null!, null!, dash, stamina));
        Assert.Equal(expectedDash, runtime.Dashes); Assert.Equal(expectedStamina, runtime.StaminaPercent);
    }

    [Fact]
    public void ProductionValidatesIdentityAndRoutesFieldSpecificUiEdits() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source");
        string source = File.ReadAllText(Path.Combine(root, "Actions/akron-startpos-configuration.cs"));
        Assert.Contains("FindStartPosSlot(startPos)", source);
        Assert.Contains("IsOriginatingSaveFileActive(fileSlot, startPos.ProfileId)", source);
        Assert.Contains("GetLoadedAreaSid()", source);
        Assert.Contains("GetStartPosStateSlotName(areaSid, slot, fileSlot)", source);
        Assert.DoesNotContain("ToPersistedStartPos(", source);
        Assert.DoesNotContain("SaveRuntimeState(", source);
        foreach (string file in new[] { "Overlay/akron-overlay-startpos-popups.cs", "Commands/akron-startpos-commands.cs" }) {
            string callsites = File.ReadAllText(Path.Combine(root, file));
            Assert.DoesNotContain("AkronActions.ApplyStartPosConfiguration(", callsites);
            foreach (string field in new[] { "dashes", "staminaPercent", "facing", "idle", "grab" })
                Assert.Contains(field + ": AkronModule.Settings.StartPosConfigured", callsites);
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class SnapshotVisualBaselineTests
{
    private sealed class Light { public float Alpha = 0.15f; }
    private sealed class Bloom { public float Base; public float Strength = 1f; }
    private sealed class Graph
    {
        public Light Lighting = new();
        public Bloom Bloom = new();
        public List<object> Renderers = new();
    }
    private sealed class CountingLight
    {
        public int Reads;
        public int Writes;
        public float Alpha { get { Reads++; return 1f; } set { Writes++; } }
    }
    private static void On(AkronVisualTuningOverrides state, object owner, object light, object bloom) =>
        state.Apply(owner, "room", light, bloom, true, 1f, true, 1f, 3f);
    private static void Off(AkronVisualTuningOverrides state, object owner, object light, object bloom) =>
        state.Apply(owner, "room", light, bloom, false, 1f, false, 1f, 3f);

    [Fact]
    public void CaptureNormalizesOwnedDetachedFieldsWithoutChangingLiveOverrides()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var live = new Light(); var bloom = new Bloom(); On(state, owner, live, bloom);
        var captured = new Light { Alpha = live.Alpha };
        var capturedBloom = new Bloom { Base = bloom.Base, Strength = bloom.Strength };
        state.CopyBaselinesToSnapshot(owner, live, bloom, captured, capturedBloom);
        Assert.Equal(0.15f, captured.Alpha); Assert.Equal(0f, capturedBloom.Base); Assert.Equal(1f, capturedBloom.Strength);
        Assert.Equal(1f, live.Alpha); Assert.Equal(1f, bloom.Base); Assert.Equal(3f, bloom.Strength);
        Off(state, owner, live, bloom);
        Assert.Equal(0.15f, live.Alpha); Assert.Equal(0f, bloom.Base); Assert.Equal(1f, bloom.Strength);
    }

    [Fact]
    public void RollbackReacquiresUnderlyingBaselineBeforeOnAndOff()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var live = new Light(); var bloom = new Bloom(); On(state, owner, live, bloom);
        var snapshot = new Light { Alpha = 1f }; var savedBloom = new Bloom { Base = 1f, Strength = 3f };
        state.CopyBaselinesToSnapshot(owner, live, bloom, snapshot, savedBloom);
        state.BeforeLevelLoad(owner, owner);
        On(state, owner, snapshot, savedBloom);
        Assert.Equal(1f, snapshot.Alpha); Assert.Equal(3f, savedBloom.Strength);
        Off(state, owner, snapshot, savedBloom);
        Assert.Equal(0.15f, snapshot.Alpha); Assert.Equal(0f, savedBloom.Base); Assert.Equal(1f, savedBloom.Strength);
    }

    [Fact]
    public void EnabledCaptureRestoredWhileDisabledDoesNotRestoreForcedValues()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var live = new Light(); var bloom = new Bloom(); On(state, owner, live, bloom);
        var snapshot = new Light { Alpha = 1f }; var savedBloom = new Bloom { Base = 1f, Strength = 3f };
        state.CopyBaselinesToSnapshot(owner, live, bloom, snapshot, savedBloom);
        Off(state, owner, live, bloom);
        Off(state, owner, snapshot, savedBloom);
        Assert.Equal(0.15f, snapshot.Alpha); Assert.Equal(0f, savedBloom.Base); Assert.Equal(1f, savedBloom.Strength);
    }

    [Fact]
    public void DifferentSourceOrCapturedValuesArePreserved()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var live = new Light(); var bloom = new Bloom(); On(state, owner, live, bloom);
        live.Alpha = 0.6f;
        var snapshot = new Light { Alpha = 0.6f }; var savedBloom = new Bloom { Base = 0.4f, Strength = 3f };
        state.CopyBaselinesToSnapshot(owner, live, bloom, snapshot, savedBloom);
        Assert.Equal(0.6f, snapshot.Alpha); Assert.Equal(0.4f, savedBloom.Base); Assert.Equal(1f, savedBloom.Strength);
    }

    [Fact]
    public void WrongOwnerRendererAndSharedDestinationCannotNormalize()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var live = new Light(); var bloom = new Bloom(); On(state, owner, live, bloom);
        var snapshot = new Light { Alpha = 1f };
        state.CopyBaselinesToSnapshot(new object(), live, bloom, snapshot, null!);
        Assert.Equal(1f, snapshot.Alpha);
        state.CopyBaselinesToSnapshot(owner, new Light { Alpha = 1f }, bloom, snapshot, null!);
        Assert.Equal(1f, snapshot.Alpha);
        state.CopyBaselinesToSnapshot(owner, live, bloom, live, bloom);
        Assert.Equal(1f, live.Alpha); Assert.Equal(3f, bloom.Strength);
    }

    [Fact]
    public void UnownedPropertiesAreNotReadOrWrittenDuringCapture()
    {
        var state = new AkronVisualTuningOverrides(); var owner = new object();
        var source = new CountingLight(); var captured = new CountingLight();
        Off(state, owner, source, null!);
        state.CopyBaselinesToSnapshot(owner, source, null!, captured, null!);
        Assert.Equal(0, source.Reads); Assert.Equal(0, source.Writes);
        Assert.Equal(0, captured.Reads); Assert.Equal(0, captured.Writes);
    }

    [Fact]
    public void DormantGraphCopyPreservesAliasesAndNormalizesDetachedRenderers()
    {
        AkronDeepClone.Initialize(); AkronDeepClone.ClearSharedState();
        try
        {
            var state = new AkronVisualTuningOverrides(); var owner = new object();
            var source = new Graph(); source.Renderers.Add(source.Lighting); source.Renderers.Add(source.Bloom);
            On(state, owner, source.Lighting, source.Bloom);
            var captured = new Graph(); AkronDeepClone.CopyIntoDormant(source, captured);
            Assert.NotSame(source.Lighting, captured.Lighting); Assert.NotSame(source.Bloom, captured.Bloom);
            Assert.Same(captured.Lighting, captured.Renderers[0]); Assert.Same(captured.Bloom, captured.Renderers[1]);
            state.CopyBaselinesToSnapshot(owner, source.Lighting, source.Bloom, captured.Lighting, captured.Bloom);
            Assert.Equal(1f, source.Lighting.Alpha); Assert.Equal(3f, source.Bloom.Strength);
            state.BeforeLevelLoad(owner, owner); AkronDeepClone.ClearSharedState();
            AkronDeepClone.CopyIntoDormant(captured, source);
            On(state, owner, source.Lighting, source.Bloom); Off(state, owner, source.Lighting, source.Bloom);
            Assert.Equal(0.15f, source.Lighting.Alpha); Assert.Equal(1f, source.Bloom.Strength);
        }
        finally { AkronDeepClone.Reset(); }
    }

    [Fact]
    public void ProductionNormalizesAfterNativeGraphCopyAndBeforeCallbackCleanup()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/SaveLoad/AkronSaveLoad.cs"));
        int copy = source.IndexOf("saveSlot.SavedLevelEventInstances = AkronDeepClone.CopyIntoDormant(level, saveSlot.SavedLevel);", StringComparison.Ordinal);
        int normalize = source.IndexOf("AkronRuntimeOptions.PrepareVisualTuningSnapshot(level, saveSlot.SavedLevel);", copy, StringComparison.Ordinal);
        int cleanup = source.IndexOf("ClearDeadCutsceneSkipCallback(saveSlot.SavedLevel);", copy, StringComparison.Ordinal);
        Assert.True(copy >= 0 && normalize > copy && cleanup > normalize);
    }
}

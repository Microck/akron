using System;
using System.IO;
using Celeste.Mod.Akron;
using Xunit;

namespace Akron.Tests;

public sealed class VisualTuningRestorationTests
{
    [Fact]
    public void ProductionRoutesEffectiveIndependentFlagsAndResetToOwnership()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Runtime/akron-runtime-options.cs"));
        Assert.Contains("allowed && AkronModule.Settings.LightLevel", source);
        Assert.Contains("allowed && AkronModule.Settings.BloomLevel", source);
        Assert.Contains("VisualTuningOverrides.Apply(level, level.Session?.Level", source);
        Assert.Contains("VisualTuningOverrides.Reset();", source);
        Assert.Contains("VisualTuningOverrides.BeforeLevelLoad(level, Engine.Scene);", source);
    }

    [Fact]
    public void NativeLoadHookReleasesOnlyVisualOwnershipBeforeOrig()
    {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "../../../../Source/Module/akron-module-map-policy.cs"));
        int start = source.IndexOf("private static void LevelOnLoadLevelForMapPolicy", StringComparison.Ordinal);
        int end = source.IndexOf("private static void LeaveMapPolicy", start, StringComparison.Ordinal);
        string hook = source[start..end];
        int release = hook.IndexOf("AkronRuntimeOptions.BeforeVisualTuningLevelLoad(self);", StringComparison.Ordinal);
        int original = hook.IndexOf("orig(self, intro, isFromLoader);", StringComparison.Ordinal);
        Assert.True(release >= 0 && original > release);
        Assert.DoesNotContain("AkronRuntimeOptions.Reset()", hook);
    }

    private sealed class Light { public float Alpha = 0.15f; }
    private sealed class Bloom { public float Base = 0f; public float Strength = 1f; }
    private sealed class PartialBloom { public float Base = 0f; public float Strength => 1f; }
    private sealed class PropertyLight { public float Alpha { get; set; } = 0.2f; }
    private sealed class ReadonlyLight { public readonly float Alpha = 0.2f; }
    private sealed class WrongTypeLight { public int Alpha = 2; }
    private sealed class CountingLight
    {
        public int Reads;
        public int Writes;
        public float Alpha { get { Reads++; return 0.2f; } set { Writes++; } }
    }

    [Fact]
    public void TintOnlyNeverReadsOrWritesAnOverrideProperty()
    {
        var state = new AkronVisualTuningOverrides(); var light = new CountingLight();
        Apply(state, new object(), light, null, false, false);
        state.Reset();
        Assert.Equal(0, light.Reads); Assert.Equal(0, light.Writes);
    }

    [Fact]
    public void MissingReadonlyAndWrongTypePropertiesRemainUnowned()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var readOnly = new ReadonlyLight(); var wrongType = new WrongTypeLight();
        Apply(state, level, new object(), null, true, false);
        Apply(state, level, readOnly, null, true, false);
        Apply(state, level, wrongType, null, true, false);
        state.Reset();
        Assert.Equal(0.2f, readOnly.Alpha); Assert.Equal(2, wrongType.Alpha);
    }

    [Fact]
    public void EqualExternalWriteCannotBeDistinguishedByValueGuard()
    {
        var state = new AkronVisualTuningOverrides(); var light = new Light();
        Apply(state, new object(), light, null, true, false);
        light.Alpha = 1f; // Same numeric value does not establish writer identity.
        state.Reset();
        Assert.Equal(0.15f, light.Alpha);
    }

    private static void Apply(AkronVisualTuningOverrides state, object? level, object? light, object? bloom,
        bool lightOn, bool bloomOn, string room = "a") =>
        state.Apply(level, room, light, bloom, lightOn, 1f, bloomOn, 1f, 3f);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LightOffRestoresWhileOtherVisualEffectsRemain(bool bloomOn)
    {
        var state = new AkronVisualTuningOverrides();
        var level = new object(); var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, bloomOn);
        Assert.Equal(1f, light.Alpha);
        Apply(state, level, light, bloom, false, bloomOn);
        Assert.Equal(0.15f, light.Alpha);
        Assert.Equal(bloomOn ? 3f : 1f, bloom.Strength);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BloomOffRestoresWhileOtherVisualEffectsRemain(bool lightOn)
    {
        var state = new AkronVisualTuningOverrides();
        var level = new object(); var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, lightOn, true);
        Apply(state, level, light, bloom, lightOn, false);
        Assert.Equal(0f, bloom.Base);
        Assert.Equal(1f, bloom.Strength);
        Assert.Equal(lightOn ? 1f : 0.15f, light.Alpha);
    }

    [Fact]
    public void TintOnlyDoesNotAcquireNativeProperties()
    {
        var state = new AkronVisualTuningOverrides();
        var level = new object(); var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, false, false);
        light.Alpha = 0.4f; bloom.Base = 0.3f; bloom.Strength = 2f;
        state.Reset();
        Assert.Equal(0.4f, light.Alpha);
        Assert.Equal(0.3f, bloom.Base);
        Assert.Equal(2f, bloom.Strength);
    }

    [Fact]
    public void ReenableCapturesFreshBaselineAndRepeatedApplyDoesNotRecapture()
    {
        var state = new AkronVisualTuningOverrides();
        var level = new object(); var light = new Light();
        Apply(state, level, light, null, true, false);
        Apply(state, level, light, null, true, false);
        Apply(state, level, light, null, false, false);
        Assert.Equal(0.15f, light.Alpha);
        light.Alpha = 0.7f;
        Apply(state, level, light, null, true, false);
        state.Reset();
        Assert.Equal(0.7f, light.Alpha);
        light.Alpha = 0.8f;
        state.Reset();
        Assert.Equal(0.8f, light.Alpha);
    }

    [Fact]
    public void RendererReplacementRestoresOldAndCapturesNewIndependently()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var oldLight = new Light(); var nextLight = new Light { Alpha = 0.6f };
        var oldBloom = new Bloom(); var nextBloom = new Bloom { Base = 0.4f, Strength = 2f };
        Apply(state, level, oldLight, oldBloom, true, true);
        Apply(state, level, nextLight, oldBloom, true, true);
        Assert.Equal(0.15f, oldLight.Alpha);
        Assert.Equal(3f, oldBloom.Strength);
        Apply(state, level, nextLight, nextBloom, true, true);
        Assert.Equal(0f, oldBloom.Base); Assert.Equal(1f, oldBloom.Strength);
        state.Reset();
        Assert.Equal(0.6f, nextLight.Alpha);
        Assert.Equal(0.4f, nextBloom.Base); Assert.Equal(2f, nextBloom.Strength);
    }

    [Fact]
    public void MissingRendererCanArriveLater()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        Apply(state, level, null, null, true, true);
        var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, true);
        state.Reset();
        Assert.Equal(0.15f, light.Alpha); Assert.Equal(1f, bloom.Strength);
    }

    [Fact]
    public void UnwritableBloomPropertyIsNotOwnedButWritableBaseIsRestored()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var bloom = new PartialBloom();
        Apply(state, level, null, bloom, false, true);
        Assert.Equal(1f, bloom.Base); Assert.Equal(1f, bloom.Strength);
        state.Reset(); Assert.Equal(0f, bloom.Base);
    }

    [Fact]
    public void WritableFloatPropertiesAreSupported()
    {
        var state = new AkronVisualTuningOverrides(); var light = new PropertyLight();
        Apply(state, new object(), light, null, true, false);
        Assert.Equal(1f, light.Alpha);
        state.Reset(); Assert.Equal(0.2f, light.Alpha);
    }

    [Fact]
    public void ReleasePreservesDifferentLaterExternalValues()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, true);
        light.Alpha = 0.6f; bloom.Base = 0.7f; bloom.Strength = 2f;
        state.Reset();
        Assert.Equal(0.6f, light.Alpha); Assert.Equal(0.7f, bloom.Base); Assert.Equal(2f, bloom.Strength);
    }

    [Fact]
    public void DisabledEffectiveFlagsReleaseRegardlessOfConfiguredTint()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, true);
        Apply(state, level, light, bloom, false, false);
        Assert.Equal(0.15f, light.Alpha); Assert.Equal(0f, bloom.Base); Assert.Equal(1f, bloom.Strength);
        Apply(state, level, light, bloom, true, true);
        state.Reset(); Assert.Equal(0.15f, light.Alpha);
    }

    [Fact]
    public void NewLevelAndNullLevelReleaseOwnedProperties()
    {
        var state = new AkronVisualTuningOverrides(); var light = new Light(); var next = new Light { Alpha = 0.4f };
        Apply(state, new object(), light, null, true, false);
        Apply(state, new object(), next, null, true, false);
        Assert.Equal(0.15f, light.Alpha);
        Apply(state, null, next, null, true, false);
        Assert.Equal(0.4f, next.Alpha);
    }

    [Fact]
    public void ActiveOwnedPreloadReleasesBeforeIncomingRoomWrites()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, true);
        state.BeforeLevelLoad(level, level);
        Assert.Equal(0.15f, light.Alpha); Assert.Equal(1f, bloom.Strength);
        light.Alpha = 0.5f; bloom.Base = 0.2f; // Native incoming room values.
        Apply(state, level, light, bloom, true, true, "destination");
        state.Reset();
        Assert.Equal(0.5f, light.Alpha); Assert.Equal(0.2f, bloom.Base); Assert.Equal(1f, bloom.Strength);
    }

    [Fact]
    public void DetachedOrWrongOwnerPreloadCannotReleaseActiveState()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object(); var detached = new object();
        var light = new Light();
        Apply(state, level, light, null, true, false);
        state.BeforeLevelLoad(detached, level);
        state.BeforeLevelLoad(level, detached);
        Assert.Equal(1f, light.Alpha);
        state.BeforeLevelLoad(level, level);
        state.BeforeLevelLoad(level, level);
        Assert.Equal(0.15f, light.Alpha);
    }

    [Fact]
    public void RoomFallbackPreservesIncomingAlphaBaseAndReleasesLingeringStrength()
    {
        var state = new AkronVisualTuningOverrides(); var level = new object();
        var light = new Light(); var bloom = new Bloom();
        Apply(state, level, light, bloom, true, true);
        light.Alpha = 0.8f; bloom.Base = 0.3f;
        Apply(state, level, light, bloom, true, true, "b");
        state.Reset();
        Assert.Equal(0.8f, light.Alpha); Assert.Equal(0.3f, bloom.Base); Assert.Equal(1f, bloom.Strength);
    }
}

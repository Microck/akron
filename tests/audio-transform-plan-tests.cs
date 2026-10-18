using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class AudioTransformPlanTests {
    [Fact]
    public void EntireControlRangeUsesBoundedCompensationStages() {
        for (int speed = 1; speed <= 40; speed++) {
            for (int pitch = 1; pitch <= 40; pitch++) {
                var plan = AkronAudioTransform.Resolve(speed / 10f, true, AkronPitchPolicy.Independent, pitch / 10f);
                float[] stages = plan.Stages;
                Assert.InRange(stages.Length, 0, 6);
                foreach (float stage in stages) Assert.InRange(stage, .5f, 2f);
                double compensation = stages.Aggregate(1d, (value, stage) => value * stage);
                Assert.InRange(Math.Abs(plan.Speed * compensation - plan.TargetPitch), 0, .00001);
            }
        }
    }

    [NativeEverestFact]
    public void AttachedDspIdentitySupportsHigh64BitHandlesAndAliases() {
        Type ownerType = typeof(AkronAudioTransform).GetNestedType("Owner", BindingFlags.NonPublic)!;
        object owner = Activator.CreateInstance(ownerType, nonPublic: true)!;
        MethodInfo track = ownerType.GetMethod("TrackAttached", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var handles = (HashSet<IntPtr>) ownerType.GetField("Attached", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
        IntPtr address = new IntPtr(0x123456789ABC);
        var first = new FMOD.DSP(address);
        var alias = new FMOD.DSP(address);
        track.Invoke(owner, new object[] { first });
        track.Invoke(owner, new object[] { alias });
        Assert.Single(handles);
        Assert.Contains(address, handles);
        Assert.True(handles.Remove(address));
    }

    [Theory]
    [InlineData(false, AkronPitchPolicy.Preserve, .5f)]
    [InlineData(true, AkronPitchPolicy.FollowSpeed, .5f)]
    [InlineData(true, AkronPitchPolicy.Preserve, 1f)]
    [InlineData(true, AkronPitchPolicy.Independent, 1.5f)]
    public void SpeedAndPitchAreResolvedIndependently(bool enabled, AkronPitchPolicy policy, float target) {
        var plan = AkronAudioTransform.Resolve(.5f, enabled, policy, 1.5f);
        Assert.Equal(.5f, plan.Speed);
        Assert.Equal(target, plan.TargetPitch);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void InvalidSettingsBecomeNeutral(float value) {
        Assert.Equal(1f, AkronAudioTransform.Normalize(value));
    }

    [Fact]
    public void NeutralPlanNeedsNoDsp() {
        Assert.Empty(AkronAudioTransform.Resolve(1f, true, AkronPitchPolicy.Preserve, 2f).Stages);
        Assert.Empty(AkronAudioTransform.Resolve(.5f, true, AkronPitchPolicy.FollowSpeed, 2f).Stages);
    }
}

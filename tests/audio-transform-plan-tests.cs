using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
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

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, true)]
    [InlineData(false, true, false, true)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, true)]
    public void ReleasedCurrentAndYieldedEventsRemainExcluded(bool owned, bool yielded, bool current, bool expected) {
        Assert.Equal(expected, AkronAudioTransform.ShouldRememberRelease(owned, yielded, current));
    }

    [Fact]
    public void DetachingCreationTrackingClearsReleasedHandleHistory() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronAudioTransform).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronAudioTransform));
        var method = type.Methods.Single(m => m.Name == "DetachHooks");
        var instructions = method.Body.Instructions;
        Assert.Contains(instructions, i => i.Operand is MethodReference guard && guard.Name == "CanDetachHooks");
        int field = instructions.ToList().FindIndex(i => i.OpCode == OpCodes.Ldsfld &&
            i.Operand is FieldReference f && f.Name == "ReleasedEvents" &&
            i.Next?.Operand is MethodReference next && next.Name == "Clear");
        Assert.True(field >= 0);
        Assert.True(instructions[field + 1].Operand is MethodReference clear && clear.Name == "Clear");
        Assert.Contains(instructions.Take(field), i => i.Operand is MethodReference call && call.Name == "remove_CreateInstance");
    }

    [Fact]
    public void ReleaseHookUsesCurrentAndYieldedStateBeforeCallingNativeRelease() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronAudioTransform).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronAudioTransform));
        var method = type.Methods.Single(m => m.Name == "EventOnRelease");
        foreach (string member in new[] { "ShouldRememberRelease", "get_CurrentMusicEventInstance", "get_CurrentAmbienceEventInstance" })
            Assert.Contains(method.Body.Instructions, i => i.Operand is MethodReference call && call.Name == member);
        Assert.Contains(method.Body.Instructions, i => i.Operand is FieldReference field && field.Name == "YieldedOwners");
        Assert.Contains(method.Body.Instructions, i => i.Operand is FieldReference field && field.Name == "currentAltMusicEvent");
    }

    [Theory]
    [InlineData(4f, .1f, .15811388f)]
    [InlineData(2f, 1f, .70710678f)]
    [InlineData(.5f, 1f, 1f)]
    [InlineData(.1f, 4f, 1f)]
    [InlineData(1f, 1f, 1f)]
    public void DownwardCompensationNormalizesEnergyWithoutBoosting(float speed, float pitch, float expected) {
        var plan = new AkronAudioTransform.Plan(speed, pitch);
        Assert.InRange(Math.Abs(plan.CompensationGain - expected), 0, .000001f);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 1, false)]
    [InlineData(1, 0, false)]
    [InlineData(1, 1, false)]
    public void CreationTrackingStaysUntilOwnersAndLiveTombstonesRetire(int owners, int released, bool expected) {
        Assert.Equal(expected, AkronAudioTransform.CanDetachHooks(owners, released));
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

using System;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class GroundRefillHairTests {
    [Theory]
    [InlineData(true, false, false, true, false, 0, 1, true)]
    [InlineData(true, false, false, false, true, 0, 1, true)]
    [InlineData(true, false, false, true, true, 1, 2, true)]
    [InlineData(false, false, false, true, true, 0, 1, false)]
    [InlineData(true, true, false, true, true, 0, 1, false)]
    [InlineData(true, false, true, true, true, 0, 1, false)]
    [InlineData(true, false, false, false, false, 0, 1, false)]
    [InlineData(true, false, false, true, true, 0, 0, false)]
    [InlineData(true, false, false, true, true, 1, 0, false)]
    public void OnlyBlockedGroundRefillIncreasesAreCorrected(bool enabled, bool allow,
        bool dead, bool beforeGround, bool afterGround, int before, int current, bool expected) {
        Assert.Equal(expected, AkronModule.ShouldCorrectGroundDashBeforeHair(
            enabled, allow, dead, beforeGround, afterGround, before, current));
    }

    [Fact]
    public void HairRoutingCorrectsBeforeSingleNativeHairCallAndRetainsVisualOrder() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var module = assembly.MainModule.GetType(typeof(AkronModule).FullName);
        var hair = module.Methods.Single(m => m.Name == "PlayerOnUpdateHair");
        var calls = hair.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).FullName).ToArray();
        int correction = Array.FindIndex(calls, x => x.Contains("ApplyGroundDashRuleBeforeHair"));
        int restore = Array.FindIndex(calls, x => x.Contains("RestoreMadelineVisualOverrides"));
        int native = Array.FindIndex(calls, x => x.Contains("orig_UpdateHair::Invoke"));
        int apply = Array.FindIndex(calls, x => x.Contains("ApplyMadelineVisualOverrides"));
        Assert.True(correction >= 0 && correction < restore && restore < native && native < apply);
        Assert.Single(calls, x => x.Contains("orig_UpdateHair::Invoke"));
        var update = module.Methods.Single(m => m.Name == "PlayerOnUpdate");
        Assert.Contains(update.Body.ExceptionHandlers, h => h.HandlerType == ExceptionHandlerType.Finally &&
            update.Body.Instructions.SkipWhile(i => i != h.HandlerStart).TakeWhile(i => i != h.HandlerEnd)
                .Any(i => i.OpCode == OpCodes.Stsfld && i.Operand is FieldReference f && f.Name == "groundRefillUpdateContext"));
        Assert.Contains(update.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "ApplyGroundRefillRulesAfterPlayerUpdate");
        var correctionMethod = module.Methods.Single(m => m.Name == "ApplyGroundDashRuleBeforeHair");
        Assert.Contains(correctionMethod.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "CanUse");
        Assert.DoesNotContain(correctionMethod.Body.Instructions, i => i.Operand is MethodReference m &&
            (m.Name == "TryUseRuntime" || m.Name == "RecordFeatureUse" || m.Name == "RecordPolicyCheck"));
        var fallback = module.Methods.Single(m => m.Name == "ApplyGroundRefillRulesAfterPlayerUpdate");
        Assert.Single(fallback.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "TryUseRuntime");
        Assert.DoesNotContain(correctionMethod.Body.Instructions, i => i.OpCode == OpCodes.Stfld &&
            i.Operand is FieldReference f && f.Name != "Dashes");
    }
}

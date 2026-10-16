using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class InputCounterEscapeTests {
    [Fact]
    public void NativeSamplerReadsBothPauseSources() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronModule).Assembly.Location);
        MethodDefinition sampler = module.GetType(typeof(AkronInputHistory).FullName).Methods
            .Single(method => method.Name == "RecordInputsPerSecondFrame");
        var inputFields = sampler.Body.Instructions.Select(instruction => instruction.Operand).OfType<FieldReference>()
            .Where(field => field.DeclaringType.FullName == "Celeste.Input").Select(field => field.Name).ToList();
        Assert.Contains("Pause", inputFields);
        Assert.Contains("ESC", inputFields);
    }

    [Fact]
    public void EscapeSharesExistingPauseEdgeAndResetChannel() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Input/akron-input-history.cs"));
        Assert.Contains("bool currentPause = Input.Pause.Check || Input.ESC.Check;", source);
        Assert.Contains("pressedThisFrame += CountRisingEdge(currentPause, previousPause);", source);
        Assert.Contains("previousPause = currentPause;", source);
        Assert.Contains("previousPause = false;", source);
        Assert.DoesNotContain("CountRisingEdge(Input.ESC", source);
        // One logical pause channel: overlapping sources and a continuous handoff
        // stay held, rather than creating additional rising-edge counts.
        bool[] pause = { false, true, true, false, false, false, false };
        bool[] escape = { false, false, true, true, false, true, false };
        bool previous = false;
        int edges = 0;
        for (int i = 0; i < pause.Length; i++) {
            bool current = pause[i] || escape[i];
            if (current && !previous) edges++;
            previous = current;
        }
        Assert.Equal(2, edges);
    }
}

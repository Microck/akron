using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class FrameStepRepeatDelayTests {
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 4)]
    [InlineData(18, 4)]
    [InlineData(120, 60)]
    public void WaitsOneDelayThenIndependentIntervals(int delay, int interval) {
        var session = new AkronModuleSession();
        Assert.False(AkronModule.AdvanceStepHoldRepeat(session, true, true, delay, interval));
        for (int elapsed = 1; elapsed <= delay + 3 * interval; elapsed++) {
            bool expected = elapsed >= delay && (elapsed - delay) % interval == 0;
            Assert.Equal(expected, AkronModule.AdvanceStepHoldRepeat(session, true, false, delay, interval));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleaseOrDisabledGuardResetsAndNewHoldGetsFreshDelay(bool newPress) {
        var session = new AkronModuleSession();
        AkronModule.AdvanceStepHoldRepeat(session, true, true, 18, 4);
        for (int i = 0; i < 10; i++) AkronModule.AdvanceStepHoldRepeat(session, true, false, 18, 4);
        Assert.False(AkronModule.AdvanceStepHoldRepeat(session, false, false, 18, 4));
        Assert.Equal(0, session.StepFrameHoldFrames);
        Assert.Equal(0, session.StepFrameRepeatCountdown);
        Assert.False(AkronModule.AdvanceStepHoldRepeat(session, true, newPress, 18, 4));
        for (int i = 1; i < 18; i++) Assert.False(AkronModule.AdvanceStepHoldRepeat(session, true, false, 18, 4));
        Assert.True(AkronModule.AdvanceStepHoldRepeat(session, true, false, 18, 4));
    }

    [Theory]
    [InlineData(-10, -10, 1, 1)]
    [InlineData(1000, 1000, 120, 60)]
    public void PersistedBoundsAreClamped(int delay, int interval, int expectedDelay, int expectedInterval) {
        var session = new AkronModuleSession();
        Assert.False(AkronModule.AdvanceStepHoldRepeat(session, true, true, delay, interval));
        Assert.Equal(expectedDelay, session.StepFrameRepeatCountdown);
        for (int i = 1; i < expectedDelay; i++) Assert.False(AkronModule.AdvanceStepHoldRepeat(session, true, false, delay, interval));
        Assert.True(AkronModule.AdvanceStepHoldRepeat(session, true, false, delay, interval));
        Assert.Equal(expectedInterval, session.StepFrameRepeatCountdown);
    }

    [Fact]
    public void ProductionPollKeepsRawHeldInputAndUsesCounter() {
        using var asm = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var type = asm.MainModule.Types.Single(t => t.Name == nameof(AkronModule));
        var poll = type.Methods.Single(m => m.Name == "UpdateStepHoldRepeat");
        foreach (string name in new[] { "CanStepGameplay", "IsKeyboardBindingHeld", "IsBindingPressed", "AdvanceStepHoldRepeat" })
            Assert.Contains(poll.Body.Instructions, i => i.Operand is MethodReference m && m.Name == name);
    }
}

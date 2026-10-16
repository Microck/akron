using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class AirJumpConsumptionTests {
    [Theory]
    [InlineData(0f, true, true)]
    [InlineData(-0.01f, true, true)]
    [InlineData(0.1f, true, true)]
    [InlineData(0.1f, false, true)]
    [InlineData(0f, false, false)]
    [InlineData(-0.01f, false, false)]
    public void RespectsNativeConsumptionEvenAfterGraceWasCleared(float currentGrace, bool consumed, bool preserve) {
        Assert.Equal(preserve, AkronModule.ShouldPreserveVanillaJumpForAirJump(currentGrace, consumed));
    }

    [Fact]
    public void AirJumpRunsInsidePlayerBoundaryAfterScopedNativeConsumption() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/akron-module-player-runtime.cs"));
        int start = source.IndexOf("private static void PlayerOnUpdate(", StringComparison.Ordinal);
        int end = source.IndexOf("internal static bool ShouldPreserveNativePlayerUpdateForNoclip", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        string body = source.Substring(start, end - start);
        Assert.Contains("nativeJumpConsumed = airJumpUpdateContext.JumpConsumed", body);
        int restore = body.IndexOf("airJumpUpdateContext = previousAirJumpContext", StringComparison.Ordinal);
        int apply = body.IndexOf("ApplyJumpHackAfterPlayerUpdate(self, wasGrounded, nativeJumpConsumed)", StringComparison.Ordinal);
        Assert.True(restore >= 0 && apply > restore);
        Assert.Contains("ReferenceEquals(currentLevel.Session, nativeSessionBefore)", body);
        Assert.Contains("ReferenceEquals(currentLevel.Tracker.GetEntity<Player>(), self)", body);
        Assert.Contains("ReferenceEquals(self, Input.Jump)", source);
        Assert.DoesNotContain("ApplyJumpHackAfterPlayerUpdate", File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs")));
    }

    [Fact]
    public void ConsumptionHookIsPairedAndQuotaResetsAcrossPlayerAndGroundBoundaries() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module");
        string module = File.ReadAllText(Path.Combine(root, "AkronModule.cs"));
        Assert.Contains("On.Monocle.VirtualButton.ConsumeBuffer += VirtualButtonOnConsumeBufferForAirJumps", module);
        Assert.Contains("On.Monocle.VirtualButton.ConsumeBuffer -= VirtualButtonOnConsumeBufferForAirJumps", module);
        string source = File.ReadAllText(Path.Combine(root, "akron-module-player-runtime.cs"));
        Assert.Contains("player.Get<AkronAirJumpQuota>()", source);
        Assert.Contains("if (wasGrounded || grounded) quota.Used = 0", source);
        Assert.Contains("quota.Used++", source);
        Assert.Contains("[ThreadStatic]", source);
        Assert.Contains("airJumpUpdateContext = default", source);
        Assert.Contains("airJumpUpdateContext = previousContext", source);
        Assert.Contains("ResetAirJumpQuota((Engine.Scene as Level)?.Tracker.GetEntity<Player>())", module);
        int death = source.IndexOf("if (deadBody != null || self.Dead)", StringComparison.Ordinal);
        Assert.True(death >= 0);
        Assert.Contains("ResetAirJumpQuota(self);", source.Substring(death, 150));
    }
    [Fact]
    public void QuotaIsInertPlayerStateRatherThanAModuleStatic() {
        var quota = new AkronAirJumpQuota();
        Assert.False(quota.Active);
        Assert.False(quota.Visible);
        Assert.Equal(0, quota.Used);
        quota.Used = 2;
        Assert.Equal(2, quota.Used);
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        Assert.Null(typeof(AkronModule).GetField("jumpHackPlayer", flags));
        Assert.Null(typeof(AkronModule).GetField("jumpHackAirJumpsUsed", flags));
        var used = typeof(AkronAirJumpQuota).GetField("Used", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(used);
        Assert.False(used.IsStatic);
    }
}

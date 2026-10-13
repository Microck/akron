using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class NoclipRespawnTests
{
    [Fact]
    public void NativeRespawnKeepsItsPlayerUpdate()
    {
        Assert.True(AkronModule.ShouldPreserveNativePlayerUpdateForNoclip(Player.StIntroRespawn));
    }

    [Theory]
    [InlineData(Player.StNormal)]
    [InlineData(Player.StClimb)]
    [InlineData(Player.StDash)]
    [InlineData(Player.StDummy)]
    public void ExistingNonRespawnNoclipMovementIsUnchanged(int state)
    {
        Assert.False(AkronModule.ShouldPreserveNativePlayerUpdateForNoclip(state));
    }
}

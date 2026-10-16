using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RespawnSoundEventTests {
    [Theory]
    [InlineData("event:/char/madeline/revive", "respawn")]
    [InlineData("EVENT:/CHAR/MADELINE/REVIVE", "respawn")]
    [InlineData("event:/mod/player_respawn", "respawn")]
    [InlineData("event:/char/madeline/death", "death")]
    [InlineData("event:/game/05_mirror_temple/seeker_revive", "seeker")]
    [InlineData("event:/char/madeline/revive_extra", null)]
    [InlineData("event:/unrelated/revive", null)]
    public void VanillaReviveUsesRespawnWithoutStealingOtherEvents(string path, string? expected) {
        Assert.Equal(expected, AkronEarAid.FindBestMatch(path)?.Key);
    }
}

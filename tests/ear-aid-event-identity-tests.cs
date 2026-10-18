using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class EarAidEventIdentityTests {
    [Theory]
    [InlineData("event:/new_content/char/madeline/death_golden", "golden-death")]
    [InlineData("event:/game/05_mirror_temple/seeker_death", "seeker")]
    [InlineData("event:/env/local/09_core/fireballs_idle", "fireball")]
    [InlineData("event:/env/local/09_core/lavagate_idle", "lava-barrier")]
    [InlineData("event:/game/04_cliffside/arrowblock_move", "move-block")]
    [InlineData("event:/game/04_cliffside/arrowblock_activate", "move-block")]
    [InlineData("event:/game/09_core/bounceblock_touch", "core-block")]
    [InlineData("event:/game/09_core/iceblock_reappear", "core-block")]
    [InlineData("event:/env/amb/04_main", "ridge-wind")]
    [InlineData("event:/new_content/env/10_voidspiral", "farewell-wind")]
    [InlineData("event:/mod/ridge_wind", "ridge-wind")]
    [InlineData("event:/mod/farewell_wind", "farewell-wind")]
    [InlineData("event:/music/lvl0/bridge", null)]
    [InlineData("event:/music/lvl4/heavy_winds", null)]
    [InlineData("event:/new_content/music/lvl10/farewell", null)]
    [InlineData("event:/new_content/game/10_farewell/fusebox_hit_1", null)]
    [InlineData("event:/new_content/game/10_farewell/fusebox_hit_2", null)]
    [InlineData("event:/new_content/env/10_rushingvoid", null)]
    [InlineData("event:/game/09_core/frontdoor_unlock", null)]
    [InlineData("event:/game/09_core/switch_to_hot", null)]
    [InlineData("event:/mod/bridge_wind", null)]
    [InlineData("event:/mod/wind", null)]
    [InlineData("event:/ridge/unrelated", null)]
    [InlineData("event:/farewell/unrelated", null)]
    [InlineData("", null)]
    public void MatchesSoundIdentityRatherThanChapterOrCompetingGenericSound(string path, string? expected) {
        Assert.Equal(expected, AkronEarAid.FindBestMatch(path)?.Key);
        Assert.Equal(expected, AkronEarAid.FindBestMatch(path.ToUpperInvariant())?.Key);
    }
}

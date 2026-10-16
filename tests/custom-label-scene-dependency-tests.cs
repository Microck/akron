using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CustomLabelSceneDependencyTests {
    [Theory]
    [InlineData("Room practice")]
    [InlineData("speed and stamina")]
    [InlineData("Map chapter deaths attempt")]
    [InlineData("Practice {fps} in this room")]
    [InlineData("{room")]
    [InlineData("{if:status:Room practice:Map practice}")]
    [InlineData("{upper(fps,room)}")]
    [InlineData("{precision(fps,room)}")]
    [InlineData("{upper(round(speed))}")]
    [InlineData("{unknown(room)}")]
    [InlineData("{upper(Room practice)}")]
    [InlineData("{cfg:room}")]
    [InlineData("{ room practice }")]
    [InlineData("{ upper:room}")]
    [InlineData("{precision(speed)}")]
    [InlineData("")]
    [InlineData(null)]
    public void LiteralWordsAndNonLevelLookupsRemainVisible(string text) {
        var label = new AkronCustomHudLabel { Text = text, Anchor = AkronHudAnchor.TopLeft, AbsolutePosition = false };
        Assert.True(AkronCustomHudLabels.CanRenderInScene(label, false));
        Assert.True(AkronCustomHudLabels.CanRenderInScene(label, true));
    }

    [Theory]
    [InlineData("{room}")]
    [InlineData("Room { room }")]
    [InlineData("{ROOM}")]
    [InlineData("{upper:room}")]
    [InlineData("{lower:stamina}")]
    [InlineData("{toUpper(room)}")]
    [InlineData("{ lower ( room ) }")]
    [InlineData("{precision(speed,1)}")]
    [InlineData("{if:room:yes:no}")]
    [InlineData("{round:player_x:2}")]
    [InlineData("{round(speed)}")]
    [InlineData("{fps} / {map_time}")]
    [InlineData("{if:status:{room:other}}")]
    public void ActualLevelLookupsStayGatedAndAbsoluteExceptionsRemain(string text) {
        var label = new AkronCustomHudLabel { Text = text, Anchor = AkronHudAnchor.TopLeft, AbsolutePosition = false };
        Assert.False(AkronCustomHudLabels.CanRenderInScene(label, false));
        Assert.True(AkronCustomHudLabels.CanRenderInScene(label, true));
        label.AbsolutePosition = true;
        Assert.True(AkronCustomHudLabels.CanRenderInScene(label, false));
        label.AbsolutePosition = false;
        label.Anchor = AkronHudAnchor.Absolute;
        Assert.True(AkronCustomHudLabels.CanRenderInScene(label, false));
    }

    [Fact]
    public void NullLabelRemainsHidden() => Assert.False(AkronCustomHudLabels.CanRenderInScene(null, false));
}

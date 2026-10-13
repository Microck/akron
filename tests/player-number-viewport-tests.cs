using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PlayerNumberViewportTests
{
    [Theory]
    [InlineData(81, 692, 0, 129, 1364, 766, false, false, 81, 563)]
    [InlineData(300, 400, 142, 0, 1080, 1080, false, false, 158, 400)]
    [InlineData(300, 400, 142, 129, 1080, 766, false, false, 158, 271)]
    [InlineData(300, 400, 142, 129, 1080, 766, true, false, 922, 271)]
    [InlineData(300, 400, 142, 129, 1080, 766, false, true, 158, 591)]
    [InlineData(300, 400, 142, 129, 1080, 766, true, true, 922, 591)]
    [InlineData(300, 400, 0, 0, 1920, 1080, false, false, 300, 400)]
    [InlineData(300, 400, 0, 0, 1920, 1080, true, false, 1620, 400)]
    [InlineData(300, 400, 0, 0, 1920, 1080, false, true, 300, 776)]
    [InlineData(300, 400, 0, 0, 1920, 1080, true, true, 1620, 776)]
    [InlineData(300, 400, -20, -30, 1280, 720, false, false, 320, 430)]
    [InlineData(300, 400, -20, -30, 1280, 720, true, true, 960, 386)]
    public void WindowProjectionIsConvertedToViewportLocalCoordinates(
        float x, float y, float originX, float originY, float width, float height,
        bool mirror, bool inverted, float expectedX, float expectedY)
    {
        Assert.Equal((expectedX, expectedY), AkronHudRenderer.PlayerNumberViewportAnchor(
            x, y, originX, originY, width, height, mirror, inverted));
    }
}

using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class TriggerViewportTests
{
    [Theory]
    [InlineData(102.3f, 299.5f, 238.7f, 204.6f, 0, 129, 102.3f, 170.5f)]
    [InlineData(300, 400, 56, 48, 142, 0, 158, 400)]
    [InlineData(300, 400, 56, 48, 142, 129, 158, 271)]
    [InlineData(300, 400, 56, 48, 0, 0, 300, 400)]
    [InlineData(300, 400, 112, 96, -20, -30, 320, 430)]
    public void FinalHudRectRemovesOnlyTheViewportOrigin(
        float x, float y, float width, float height, float originX, float originY,
        float expectedX, float expectedY)
    {
        AkronHudRect rect = AkronHudRenderer.TriggerViewportRect(new AkronHudRect(x, y, width, height), originX, originY);
        Assert.Equal(expectedX, rect.X);
        Assert.Equal(expectedY, rect.Y);
        Assert.Equal(width, rect.Width);
        Assert.Equal(height, rect.Height);
    }
}

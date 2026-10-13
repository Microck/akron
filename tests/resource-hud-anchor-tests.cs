using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ResourceHudAnchorTests
{
    [Theory]
    [InlineData(1920, 1080, 810, 1572, 997)]
    [InlineData(1280, 720, 490, 932, 637)]
    [InlineData(960, 540, 330, 612, 457)]
    [InlineData(640, 360, 170, 292, 277)]
    [InlineData(2560, 1440, 1130, 2212, 1357)]
    public void SixAnchorsUseTheActiveViewportSize(float width, float height, float center, float right, float bottom)
    {
        Assert.Equal((48f, 48f), Anchor(AkronStaminaHudPosition.TopLeft, width, height));
        Assert.Equal((center, 48f), Anchor(AkronStaminaHudPosition.TopCenter, width, height));
        Assert.Equal((right, 48f), Anchor(AkronStaminaHudPosition.TopRight, width, height));
        Assert.Equal((48f, bottom), Anchor(AkronStaminaHudPosition.BottomLeft, width, height));
        Assert.Equal((center, bottom), Anchor(AkronStaminaHudPosition.BottomCenter, width, height));
        Assert.Equal((right, bottom), Anchor(AkronStaminaHudPosition.BottomRight, width, height));
    }

    [Fact]
    public void ClockClearanceChangesOnlyTheTopLeftAnchor()
    {
        foreach (AkronStaminaHudPosition position in System.Enum.GetValues<AkronStaminaHudPosition>())
        {
            var off = AkronHudRenderer.ResourceHudAnchorCoordinates(position, 1280, 720, false);
            var on = AkronHudRenderer.ResourceHudAnchorCoordinates(position, 1280, 720, true);
            Assert.Equal(position == AkronStaminaHudPosition.TopLeft ? (48f, 146f) : off, on);
        }
    }

    [Fact]
    public void NominalFootprintIsPreservedEvenInAnUndersizedViewport()
    {
        var anchor = Anchor(AkronStaminaHudPosition.BottomRight, 1280, 720);
        Assert.Equal(48f, 1280 - anchor.X - 300);
        Assert.Equal(48f, 720 - anchor.Y - 35);
        Assert.Equal((-148f, 17f), Anchor(AkronStaminaHudPosition.BottomRight, 200, 100));
    }

    private static (float X, float Y) Anchor(AkronStaminaHudPosition position, float width, float height)
    {
        return AkronHudRenderer.ResourceHudAnchorCoordinates(position, width, height, false);
    }
}

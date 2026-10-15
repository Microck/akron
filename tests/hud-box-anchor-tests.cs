using Xunit;
namespace Celeste.Mod.Akron.Tests;
public sealed class HudBoxAnchorTests
{
    [Theory]
    [InlineData(1920,1080,860,530,1715,1055)]
    [InlineData(1364,766,582,373,1159,741)]
    [InlineData(1280,720,540,350,1075,695)]
    [InlineData(960,540,380,260,755,515)]
    [InlineData(2560,1440,1180,710,2355,1415)]
    public void NineAnchorsUseViewportDimensions(float w,float h,float center,float middle,float right,float bottom)
    {
        Assert.Equal((5f,5f),At(AkronHudAnchor.TopLeft,w,h));
        Assert.Equal((center,5f),At(AkronHudAnchor.TopCenter,w,h));
        Assert.Equal((right,5f),At(AkronHudAnchor.TopRight,w,h));
        Assert.Equal((5f,middle),At(AkronHudAnchor.MiddleLeft,w,h));
        Assert.Equal((center,middle),At(AkronHudAnchor.Center,w,h));
        Assert.Equal((right,middle),At(AkronHudAnchor.MiddleRight,w,h));
        Assert.Equal((5f,bottom),At(AkronHudAnchor.BottomLeft,w,h));
        Assert.Equal((center,bottom),At(AkronHudAnchor.BottomCenter,w,h));
        Assert.Equal((right,bottom),At(AkronHudAnchor.BottomRight,w,h));
    }
    [Fact]
    public void UnknownAnchorRetainsTopRightFallback()
    {
        Assert.Equal(At(AkronHudAnchor.TopRight,1364,766),At((AkronHudAnchor)999,1364,766));
    }
    [Fact]
    public void SmallViewportPreservesFootprintAndMargin()
    {
        Assert.Equal((-105f,-15f),At(AkronHudAnchor.BottomRight,100,10));
    }
    private static (float X,float Y) At(AkronHudAnchor anchor,float w,float h)
        => AkronHudRenderer.HudBoxAnchorCoordinates(anchor,200,20,w,h);
}

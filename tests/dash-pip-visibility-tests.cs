using Xunit;
namespace Celeste.Mod.Akron.Tests;
public sealed class DashPipVisibilityTests
{
    [Theory]
    [InlineData(0, 1, false, true)]
    [InlineData(1, 1, false, false)]
    [InlineData(1, 1, true, true)]
    [InlineData(0, 0, false, false)]
    [InlineData(0, 0, true, true)]
    [InlineData(4, 5, false, true)]
    [InlineData(4, 4, false, false)]
    public void EmptyVisibilityControlsWholePip(int index, int dashes, bool showEmpty, bool expected)
    {
        Assert.Equal(expected, AkronHudRenderer.ShouldDrawDashPip(index, dashes, showEmpty));
    }
}

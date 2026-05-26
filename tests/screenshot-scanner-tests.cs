using System.Linq;
using System.Text.Json;
using Celeste.Mod.Akron;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ScreenshotScannerTests {
    [Fact]
    public void ScanTilesUseReferenceCenterRelativeDeltas() {
        AkronScreenshotScanTile[] tiles = AkronScreenshotScanner
            .BuildScanTiles(528, -88, 944, 180, 320f, 180f, 160, 120)
            .ToArray();

        Assert.Equal(5, tiles.Length);
        Assert.Equal(0, tiles[0].Index);
        Assert.Equal(528f, tiles[0].CameraX);
        Assert.Equal(-88f, tiles[0].CameraY);
        Assert.Equal(160, tiles[0].DeltaX);
        Assert.Equal(90, tiles[0].DeltaY);

        Assert.Equal(4, tiles[4].Index);
        Assert.Equal(1152f, tiles[4].CameraX);
        Assert.Equal(-88f, tiles[4].CameraY);
        Assert.Equal(784, tiles[4].DeltaX);
        Assert.Equal(90, tiles[4].DeltaY);
    }

    [Fact]
    public void ScanTilesAppendBottomAndRightEdgesWithoutDuplicates() {
        AkronScreenshotScanTile[] tiles = AkronScreenshotScanner
            .BuildScanTiles(0, 0, 650, 430, 320f, 180f, 160, 120)
            .ToArray();

        Assert.Equal(new[] { 160, 320, 480, 490 }, tiles.Select(tile => tile.DeltaX).Distinct().ToArray());
        Assert.Equal(new[] { 90, 210, 330, 340 }, tiles.Select(tile => tile.DeltaY).Distinct().ToArray());
        Assert.Equal(16, tiles.Length);
    }

    [Fact]
    public void RoomMetadataMatchesScreenshotToolShape() {
        string json = AkronScreenshotScanner.BuildRoomMetadataJson(
            528,
            -88,
            944,
            180,
            320f,
            180f,
            1920,
            1080);

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(new[] { 528, -88 }, root.GetProperty("roomPosition").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.Equal(new[] { 944, 180 }, root.GetProperty("roomSize").EnumerateArray().Select(value => value.GetInt32()).ToArray());
        Assert.Equal(new[] { 320f, 180f }, root.GetProperty("cameraSize").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        Assert.Equal(new[] { 1920, 1080 }, root.GetProperty("viewPort").EnumerateArray().Select(value => value.GetInt32()).ToArray());
    }

    [Fact]
    public void TransparentBackgroundInferenceRecoversAlphaFromBlackAndWhitePasses() {
        Assert.Equal((255, 0, 0, 128), AkronCapture.InferTransparentChannels(255, 128, 0, 0));
        Assert.Equal((0, 0, 0, 0), AkronCapture.InferTransparentChannels(255, 0, 0, 0));
    }
}

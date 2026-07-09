using System;
using System.Collections.Generic;
using System.IO;
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

    [Theory]
    [InlineData(528, -88, 944, 180, 320f, 180f, 160, 120)]
    [InlineData(0, 0, 650, 430, 320f, 180f, 160, 120)]
    [InlineData(-40, 32, 128, 96, 320f, 180f, 160, 120)]
    public void ScanTilesCoverEveryRoomEdgeWithoutCutoff(int roomLeft, int roomTop, int roomWidth, int roomHeight, float cameraWidth, float cameraHeight, int stepX, int stepY) {
        AkronScreenshotScanTile[] tiles = AkronScreenshotScanner
            .BuildScanTiles(roomLeft, roomTop, roomWidth, roomHeight, cameraWidth, cameraHeight, stepX, stepY)
            .ToArray();

        Assert.NotEmpty(tiles);
        Assert.True(tiles.Min(tile => tile.CameraX) <= roomLeft);
        Assert.True(tiles.Min(tile => tile.CameraY) <= roomTop);
        Assert.True(tiles.Max(tile => tile.CameraX + cameraWidth) >= roomLeft + roomWidth);
        Assert.True(tiles.Max(tile => tile.CameraY + cameraHeight) >= roomTop + roomHeight);
    }

    [Fact]
    public void RoomMetadataMatchesReferenceShape() {
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
    public void MergedRoomCanvasUsesReferenceProjection() {
        var canvas = AkronScreenshotScanner.BuildMergedImageSize(
            roomWidth: 650,
            roomHeight: 430,
            cameraWidth: 320f,
            cameraHeight: 180f,
            viewportWidth: 1600,
            viewportHeight: 900);

        Assert.Equal(3250, canvas.X);
        Assert.Equal(2150, canvas.Y);
    }

    [Fact]
    public void MergedTilePositionUsesCenterRelativeDeltas() {
        var position = AkronScreenshotScanner.BuildMergedTilePosition(
            deltaX: 288,
            deltaY: 90,
            cameraWidth: 320f,
            cameraHeight: 180f,
            viewportWidth: 1600,
            viewportHeight: 900);

        Assert.Equal(640, position.X);
        Assert.Equal(0, position.Y);
    }

    [Theory]
    [InlineData("0003,dx=288,dy=90.png", true, 288, 90)]
    [InlineData("0003,dy=90,dx=-40.jpg", true, -40, 90)]
    [InlineData("merged.png", false, 0, 0)]
    [InlineData("map.png", false, 0, 0)]
    public void ScanTileFilenameParserOnlyAcceptsCaptureTiles(string fileName, bool expected, int expectedDeltaX, int expectedDeltaY) {
        bool parsed = AkronScreenshotScanner.TryParseScanTileFileName(fileName, out int deltaX, out int deltaY);

        Assert.Equal(expected, parsed);
        Assert.Equal(expectedDeltaX, deltaX);
        Assert.Equal(expectedDeltaY, deltaY);
    }

    [Fact]
    public void MergedImageGuardRejectsTexturesTooLargeForRuntimeComposition() {
        Assert.False(AkronScreenshotScanner.CanCreateMergedImage(16385, 100, out string dimensionReason));
        Assert.Contains("exceed", dimensionReason);

        Assert.False(AkronScreenshotScanner.CanCreateMergedImage(15000, 15000, out string pixelReason));
        Assert.Contains("pixel count", pixelReason);

        Assert.True(AkronScreenshotScanner.CanCreateMergedImage(3250, 2150, out string okReason));
        Assert.Equal(string.Empty, okReason);
    }

    [Fact]
    public void MapLayoutPreservesRoomWorldPositions() {
        AkronScreenshotRoomImage[] rooms = new[] {
            new AkronScreenshotRoomImage("a", 100, -50, 320, 180, 1600, 900),
            new AkronScreenshotRoomImage("b", 740, -50, 320, 180, 1600, 900),
            new AkronScreenshotRoomImage("c", 100, 310, 640, 180, 3200, 900)
        };

        IReadOnlyList<AkronScreenshotMapPlacement> placements = AkronScreenshotScanner.BuildMapWorldLayout(rooms, out int width, out int height);

        Assert.Equal(3, placements.Count);
        Assert.Equal(1024, width);
        Assert.Equal(604, height);
        Assert.Equal((32, 32, 320, 180), (placements[0].X, placements[0].Y, placements[0].Width, placements[0].Height));
        Assert.Equal((672, 32, 320, 180), (placements[1].X, placements[1].Y, placements[1].Width, placements[1].Height));
        Assert.Equal((32, 392, 640, 180), (placements[2].X, placements[2].Y, placements[2].Width, placements[2].Height));
        Assert.All(placements, placement => Assert.InRange(placement.Scale, 0.19f, 0.21f));
    }

    [Fact]
    public void MapLayoutUsesSharedRoundedEdgesForAdjacentRooms() {
        AkronScreenshotRoomImage[] rooms = new[] {
            new AkronScreenshotRoomImage("a", 0, 0, 7000, 180, 7000, 180),
            new AkronScreenshotRoomImage("b", 7000, 0, 7000, 180, 7000, 180)
        };

        IReadOnlyList<AkronScreenshotMapPlacement> placements = AkronScreenshotScanner.BuildMapWorldLayout(rooms, out _, out _);

        Assert.Equal(placements[0].X + placements[0].Width, placements[1].X);
    }

    [Fact]
    public void MapLayoutKeepsLargeWorldMapsAtScreenshotPixelScale() {
        AkronScreenshotRoomImage[] rooms = new[] {
            new AkronScreenshotRoomImage("bounds", -176, -18360, 15152, 18600, 75760, 93000)
        };

        IReadOnlyList<AkronScreenshotMapPlacement> placements = AkronScreenshotScanner.BuildMapWorldLayout(rooms, out int width, out int height, projectionX: 5f, projectionY: 5f);

        Assert.Equal(76080, width);
        Assert.Equal(93320, height);
        Assert.Equal((160, 160, 75760, 93000), (placements[0].X, placements[0].Y, placements[0].Width, placements[0].Height));
        Assert.Equal(1f, placements[0].Scale);
    }

    [Fact]
    public void OversizedRoomCollagesDownscaleBeforeAllocation() {
        var fullSize = AkronScreenshotScanner.BuildMergedImageSize(
            roomWidth: 2344,
            roomHeight: 2696,
            cameraWidth: 320f,
            cameraHeight: 180f,
            viewportWidth: 1600,
            viewportHeight: 900);
        var safeSize = AkronScreenshotScanner.BuildRuntimeSafeImageSize(fullSize.X, fullSize.Y);

        Assert.Equal(11720, fullSize.X);
        Assert.Equal(13480, fullSize.Y);
        Assert.Equal(7122, safeSize.Width);
        Assert.Equal(8192, safeSize.Height);
        Assert.True(safeSize.Scale < 1f);
        Assert.True(AkronScreenshotScanner.CanCreateMergedImage(safeSize.Width, safeSize.Height, out _));
    }

    [Fact]
    public void TransparentBackgroundInferenceRecoversAlphaFromBlackAndWhitePasses() {
        Assert.Equal((255, 0, 0, 128), AkronCapture.InferTransparentChannels(255, 128, 0, 0));
        Assert.Equal((0, 0, 0, 0), AkronCapture.InferTransparentChannels(255, 0, 0, 0));
    }

    [Fact]
    public void CaptureDoesNotDrawScannerMarkersIntoRawTiles() {
        string captureSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-capture.cs"));
        string moduleSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Module/AkronModule.cs"));
        string hudSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Hud/AkronHudRenderer.cs"));

        Assert.DoesNotContain("IsCapturingScannerOverlays", captureSource);
        Assert.DoesNotContain("pendingScannerOverlays", captureSource);
        Assert.DoesNotContain("includeScannerOverlays", captureSource);
        Assert.DoesNotContain("RenderCaptureScannerOverlays", captureSource);
        Assert.DoesNotContain("RenderCaptureScannerOverlays", moduleSource);
        Assert.DoesNotContain("RenderScannerExportOverlays", hudSource);
    }

    [Fact]
    public void RoomScanWritesMergedCollageAfterTiles() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));

        int tileCaptureIndex = source.IndexOf("AkronCapture.CaptureToPath(level, lastExportPath);", StringComparison.Ordinal);
        int mergeIndex = source.IndexOf("TryWriteMergedRoomImage(level, bounds, cameraWidth, cameraHeight, viewportWidth, viewportHeight", StringComparison.Ordinal);
        int mapMergeIndex = source.IndexOf("TryWriteMergedChapterImage(current ?? Engine.Scene as Level, mergedRooms);", StringComparison.Ordinal);

        Assert.True(tileCaptureIndex >= 0);
        Assert.True(mergeIndex > tileCaptureIndex);
        Assert.True(mapMergeIndex >= 0);
        Assert.Contains("BuildPath(level, \"merged\" + ImageExtension())", source);
        Assert.Contains("BuildSidePath(level, \"map\" + ImageExtension())", source);
        Assert.Contains("SaveMergedMapImageStreaming", source);
        Assert.Contains("LoadMergedImagePixels(rooms[i].ImagePath", source);
        Assert.Contains("BuildMapWorldLayout", source);
        Assert.Contains("DrawScannerExportMarkers", source);
        Assert.Contains("mergedRooms.Count > 0", source);
        Assert.Contains("LargeMapCaptureWarningPixels", source);
        Assert.Contains("temporarily freeze or crash the game", source);
        Assert.DoesNotContain("mergedRooms.Count == scannedRoomCount", source);
        Assert.DoesNotContain("AkronModule.Settings.ScreenshotScannerExportMarkers);", source);
    }

    [Fact]
    public void RoomMergeSkipsUnreadableTilesWithoutBlockingMapOutput() {
        string scannerSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        string captureSource = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-capture.cs"));

        Assert.Contains("Skipping unreadable scan tile", scannerSource);
        Assert.Contains("blendedTileCount == 0", scannerSource);
        Assert.Contains("Writing map collage with ", scannerSource);
        Assert.Contains("SaveCapturedTexture", captureSource);
        Assert.Contains("outputPath + \".tmp\"", captureSource);
        Assert.Contains("SaveMergedTexture", scannerSource);
        Assert.Contains("path + \".tmp\"", scannerSource);
    }

    [Fact]
    public void UploadHostStopsMapCaptureWhenRemoved() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Community/akron-community-pack-uploads.cs"));
        int removedStart = source.IndexOf("public override void Removed(Scene scene)", StringComparison.Ordinal);
        int removedEnd = source.IndexOf("private IEnumerator Run()", removedStart, StringComparison.Ordinal);

        Assert.True(removedStart >= 0);
        Assert.True(removedEnd > removedStart);
        string removedSource = source[removedStart..removedEnd];

        Assert.Contains("Tags.Persistent", source);
        Assert.Contains("private bool ownsCaptureScan;", source);
        Assert.Contains("CheckUploadEndpointAsync(", source);
        Assert.Contains("ownsCaptureScan = true;", source);
        Assert.Contains("ownsCaptureScan = false;", source);
        Assert.Contains("uploadCancellation.Cancel();", removedSource);
        Assert.Contains("ownsCaptureScan && AkronScreenshotScanner.IsScanning", removedSource);
        Assert.Contains("CleanupPack();", removedSource);
    }

    [Fact]
    public void MapCaptureExposesRoomProgressForUploadStatus() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));

        Assert.Contains("public static int ScanRoomsCompleted", source);
        Assert.Contains("public static int ScanRoomsTotal", source);
        Assert.Contains("public static float ScanProgressFraction", source);
        Assert.Contains("public static string DescribeProgress()", source);
        Assert.Contains("scanRoomsTotal = rooms.Count;", source);
        Assert.Contains("scanRoomsCompleted = scannedRoomCount;", source);
    }

    [Fact]
    public void ChapterScanStartupExceptionsResetScannerReservation() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        int scanChapterStart = source.IndexOf("public static bool ScanChapter(Level level)", StringComparison.Ordinal);
        int scanMapDataStart = source.IndexOf("private static MapData GetScanMapData", scanChapterStart, StringComparison.Ordinal);

        Assert.True(scanChapterStart >= 0);
        Assert.True(scanMapDataStart > scanChapterStart);
        string scanChapterSource = source[scanChapterStart..scanMapDataStart];

        Assert.Contains("catch {", scanChapterSource);
        Assert.Contains("ResetFailedStart();", scanChapterSource);
        Assert.Contains("throw;", scanChapterSource);
        Assert.Contains("private static void ResetFailedStart()", source);
        Assert.Contains("isScanning = false;", source);
        Assert.Contains("scannerHost = null;", source);
    }

    [Fact]
    public void MapCaptureQueuesNoSpawnRoomsAndSkipsOnlyFillerByName() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        int filterStart = source.IndexOf("private static bool CanScanChapterRoom", StringComparison.Ordinal);
        int filterEnd = source.IndexOf("private static void EnsureScannerHostInLevel", filterStart, StringComparison.Ordinal);
        Assert.True(filterStart >= 0);
        Assert.True(filterEnd > filterStart);
        string filterSource = source[filterStart..filterEnd];

        Assert.DoesNotContain("room.Spawns.Count == 0", filterSource);
        Assert.Contains("room == null", filterSource);
        Assert.Contains("!room.Name.StartsWith(\"FILLER\"", filterSource);
        Assert.Contains("Skipping room '\" + roomName + \"' because no scan spawn could be resolved", source);
    }

    [Fact]
    public void MapCaptureUsesFullAreaModeMapWhenSessionMapIsPartial() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));

        Assert.Contains("MapData mapData = GetScanMapData(level);", source);
        Assert.Contains("(mapData?.Levels ?? new List<LevelData>())", source);
        Assert.Contains(".Where(CanScanChapterRoom)", source);
        Assert.Contains("AreaData.Get(area.Value)", source);
        Assert.Contains("modeMap.Reload();", source);
        Assert.Contains("Starting map capture with ", source);
        Assert.Contains("Map capture has no scannable rooms queued.", source);
        Assert.Contains("modeMap.Levels.Count > sessionMap.Levels.Count", source);
        Assert.Contains("GetScanMapData(level)?.Get(roomName)", source);
    }

    [Fact]
    public void StartPosMarkedRoomsSortByLowestSlotBeforeCaptureLimit() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        Dictionary<int, AkronStartPos> startPositions = new Dictionary<int, AkronStartPos> {
            [7] = new AkronStartPos { AreaSid = "Celeste/1-ForsakenCity", Room = "a-00" },
            [1] = new AkronStartPos { AreaSid = "Celeste/1-ForsakenCity", Room = "a-03" },
            [4] = new AkronStartPos { AreaSid = "Other/Map", Room = "a-01" }
        };

        Assert.Equal(7, AkronScreenshotScanner.FirstStartPosSlotInRoom(
            "Celeste/1-ForsakenCity", "a-00", startPositions));
        Assert.Equal(1, AkronScreenshotScanner.FirstStartPosSlotInRoom(
            "Celeste/1-ForsakenCity", "a-03", startPositions));
        Assert.Equal(int.MaxValue, AkronScreenshotScanner.FirstStartPosSlotInRoom(
            "Celeste/1-ForsakenCity", "a-01", startPositions));

        int orderingIndex = source.IndexOf(".OrderBy(room => FirstStartPosSlotInRoom", StringComparison.Ordinal);
        int captureLimitIndex = source.IndexOf(".Take(maxMarkedRooms)", StringComparison.Ordinal);
        Assert.True(orderingIndex >= 0);
        Assert.True(captureLimitIndex > orderingIndex);
    }

    [Fact]
    public void ScannerOwnedRoomLoadsDoNotCancelMapCapture() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));

        Assert.Contains("if (!isScanning)", source);
        Assert.Contains("mode != LevelExit.Mode.Restart && !isScanning", source);
    }

    [Fact]
    public void ScannerExportMarkersUseExactWorldGeometryWithOneGamePixelBorders() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        int exportStart = source.IndexOf("private static void DrawScannerExportMarkers", StringComparison.Ordinal);
        int exportEnd = source.IndexOf("private static void DrawScannerExportAreaMarker", exportStart, StringComparison.Ordinal);
        Assert.True(exportStart >= 0);
        Assert.True(exportEnd > exportStart);
        string exportSource = source[exportStart..exportEnd];

        Assert.Contains("Color.OrangeRed", exportSource);
        Assert.Contains("Color.DeepSkyBlue", exportSource);
        Assert.Contains("Color.Magenta", exportSource);
        Assert.Contains("ScannerExportMarkerBorder = 1", source);
        Assert.DoesNotContain("ExpandRectToMinimum", source);
        Assert.DoesNotContain("ScannerExportStartPosMinWidth", source);
        Assert.DoesNotContain("ScannerExportAreaMinSize", source);
        Assert.Contains("new Rectangle((int) Math.Round(startPos.Position.X - 4f), (int) Math.Round(startPos.Position.Y - 11f), 8, 11)", exportSource);
        Assert.Contains("WorldRectToCanvasRect(roomBounds, roomTargetX, roomTargetY, projectionX, projectionY, visibleHitbox)", exportSource);
        Assert.Contains("ScannerExportBorderPixels(projectionX, marker.Width)", source);
        Assert.Contains("ScannerExportBorderPixels(projectionY, marker.Height)", source);
        Assert.Contains("Math.Round(ScannerExportMarkerBorder * projection)", source);
        Assert.Contains("DrawFilledRect(canvas, canvasWidth, canvasHeight, marker, WithAlpha(color, fillOpacity));", source);
        Assert.Contains("if (availableWidth < glyphWidth || availableHeight < glyphHeight)", source);
        Assert.Contains("DrawScannerExportStartPosLabel", exportSource);
        Assert.Contains("DigitGlyph", source);
        Assert.DoesNotContain("DrawStartPosPlayerPreview", exportSource);
        Assert.DoesNotContain("ActiveFont", source);
    }

    [Fact]
    public void ScannerExportAreaMarkersDoNotRequireLiveAutomationFlags() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));
        int exportStart = source.IndexOf("private static void DrawScannerExportMarkers", StringComparison.Ordinal);
        int exportEnd = source.IndexOf("private static void DrawScannerExportAreaMarker", exportStart, StringComparison.Ordinal);

        Assert.True(exportStart >= 0);
        Assert.True(exportEnd > exportStart);
        string exportSource = source[exportStart..exportEnd];

        Assert.Contains("settings.ScreenshotScannerExportAutoKillAreas", exportSource);
        Assert.Contains("settings.ScreenshotScannerExportAutoDeafenAreas", exportSource);
        Assert.DoesNotContain("settings.ScreenshotScannerExportAutoKillAreas && settings.AutoKillArea", exportSource);
        Assert.DoesNotContain("settings.ScreenshotScannerExportAutoDeafenAreas && settings.AutoDeafenArea", exportSource);
    }

    [Fact]
    public void MapCompositeFillsExactPlacementRectangles() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Tools/akron-screenshot-scanner.cs"));

        Assert.Contains("BlendScaledTileNearestToRect", source);
        Assert.Contains("SaveMergedMapImageStreaming", source);
        Assert.Contains("BuildMapWorldLayout(roomImagesForLayout", source);
        Assert.Contains("projectionX, projectionY", source);
        Assert.Contains("ScreenshotScannerDownscaleMapCapture", source);
        Assert.Contains("projectionX *= safeSize.Scale", source);
        Assert.Contains("projectionY *= safeSize.Scale", source);
        Assert.DoesNotContain("roomImage.Room.ProjectionX * roomImage.Room.MergedScale", source);
        Assert.DoesNotContain("projectionX * roomScale", source);
    }
}

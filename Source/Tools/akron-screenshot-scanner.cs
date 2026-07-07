using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text.Json;
using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

internal readonly struct AkronScreenshotScanTile {
    public AkronScreenshotScanTile(int index, float cameraX, float cameraY, int deltaX, int deltaY) {
        Index = index;
        CameraX = cameraX;
        CameraY = cameraY;
        DeltaX = deltaX;
        DeltaY = deltaY;
    }

    public int Index { get; }
    public float CameraX { get; }
    public float CameraY { get; }
    public int DeltaX { get; }
    public int DeltaY { get; }
}

internal readonly struct AkronScreenshotMergedRoom {
    public AkronScreenshotMergedRoom(string roomName, string imagePath, string roomDirectory, Rectangle bounds, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight, float mergedScale) {
        RoomName = roomName;
        ImagePath = imagePath;
        RoomDirectory = roomDirectory;
        Bounds = bounds;
        CameraWidth = cameraWidth;
        CameraHeight = cameraHeight;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        MergedScale = mergedScale;
        ProjectionX = viewportWidth / cameraWidth;
        ProjectionY = viewportHeight / cameraHeight;
    }

    public string RoomName { get; }
    public string ImagePath { get; }
    public string RoomDirectory { get; }
    public Rectangle Bounds { get; }
    public float CameraWidth { get; }
    public float CameraHeight { get; }
    public int ViewportWidth { get; }
    public int ViewportHeight { get; }
    public float MergedScale { get; }
    public float ProjectionX { get; }
    public float ProjectionY { get; }
}

internal readonly struct AkronScreenshotRoomImage {
    public AkronScreenshotRoomImage(string roomName, Rectangle bounds, int width, int height)
        : this(roomName, bounds.X, bounds.Y, bounds.Width, bounds.Height, width, height) {
    }

    public AkronScreenshotRoomImage(string roomName, int boundsX, int boundsY, int boundsWidth, int boundsHeight, int width, int height) {
        RoomName = roomName;
        BoundsX = boundsX;
        BoundsY = boundsY;
        BoundsWidth = boundsWidth;
        BoundsHeight = boundsHeight;
        Width = width;
        Height = height;
    }

    public string RoomName { get; }
    public int BoundsX { get; }
    public int BoundsY { get; }
    public int BoundsWidth { get; }
    public int BoundsHeight { get; }
    public int Width { get; }
    public int Height { get; }
}

internal readonly struct AkronScreenshotMapPlacement {
    public AkronScreenshotMapPlacement(string roomName, int x, int y, int width, int height, float scale) {
        RoomName = roomName;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Scale = scale;
    }

    public string RoomName { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public float Scale { get; }
}

public static class AkronScreenshotScanner {
    private const int TileSize = 8;
    private const int RoomLoadSettleFrames = 6;
    private const int MaxMergedImageDimension = 16384;
    private const int MaxDownscaledImageDimension = 8192;
    private const long MaxMergedImagePixels = 100_000_000L;
    private const long LargeMapCaptureWarningPixels = 1_000_000_000L;
    private const int MapWorldPadding = 32;
    private const int ScannerExportMarkerBorder = 1;
    private static Entity scannerHost;
    private static bool isScanning;
    private static bool scanCancelled;
    private static bool allowScanRoomSetupTriggers;
    private static string lastExportPath = string.Empty;
    private static int scanRoomsCompleted;
    private static int scanRoomsTotal;
    private static bool hasInitialPlayerState;
    private static bool initialPlayerVisible;
    private static bool initialPlayerCollidable;
    private static Collider initialPlayerCollider;
    private static int initialPlayerState;

    public static bool IsScanning => isScanning;
    public static string LastExportPath => lastExportPath;
    public static int ScanRoomsCompleted => scanRoomsCompleted;
    public static int ScanRoomsTotal => scanRoomsTotal;
    public static float ScanProgressFraction => scanRoomsTotal <= 0
        ? isScanning ? 0f : 1f
        : Math.Min(1f, Math.Max(0f, scanRoomsCompleted / (float) scanRoomsTotal));

    public static void ScanRoom(Level level) {
        if (!TryStart(level, out Player player)) {
            return;
        }

        scanRoomsCompleted = 0;
        scanRoomsTotal = 1;
        scannerHost.Add(new Coroutine(ScanRooms(player, new Queue<string>(new[] { level.Session.Level }), buildMapComposite: false)));
    }

    public static bool ScanChapter(Level level) {
        if (!TryStart(level, out Player player)) {
            return false;
        }

        try {
            MapData mapData = GetScanMapData(level);
            int sessionRoomCount = level.Session?.MapData?.Levels?.Count ?? 0;
            int scanRoomCount = mapData?.Levels?.Count ?? 0;
            AkronLog.Normal(nameof(AkronScreenshotScanner), "Starting map capture with " + scanRoomCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " queued room candidates; session-map rooms=" + sessionRoomCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            long estimatedPixels = EstimateMapOutputPixels(level, mapData);
            if (!AkronModule.Settings.ScreenshotScannerDownscaleMapCapture && estimatedPixels >= LargeMapCaptureWarningPixels) {
                string estimate = FormatPixelCount(estimatedPixels);
                string message = "Huge map capture: about " + estimate + " output pixels. This may take several minutes, create a very large file, and temporarily freeze or crash the game.";
                AkronLog.Warn(nameof(AkronScreenshotScanner), message);
                Engine.Scene?.Add(new AkronToast(message));
            }

            Queue<string> rooms = new Queue<string>();
            foreach (LevelData room in mapData?.Levels ?? new List<LevelData>()) {
                if (CanScanChapterRoom(room)) {
                    rooms.Enqueue(room.Name);
                }
            }
            scanRoomsCompleted = 0;
            scanRoomsTotal = rooms.Count;
            if (rooms.Count == 0) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Map capture has no scannable rooms queued.");
                isScanning = false;
                scanRoomsTotal = 0;
                return false;
            }

            scannerHost.Add(new Coroutine(ScanRooms(player, rooms, buildMapComposite: true)));
            return true;
        } catch {
            ResetFailedStart();
            throw;
        }
    }

    private static MapData GetScanMapData(Level level) {
        MapData sessionMap = level?.Session?.MapData;
        AreaKey? area = level?.Session?.Area;
        if (area == null) {
            return sessionMap;
        }

        AreaData areaData = AreaData.Get(area.Value);
        int modeIndex = (int) area.Value.Mode;
        MapData modeMap = areaData?.Mode != null &&
                          modeIndex >= 0 &&
                          modeIndex < areaData.Mode.Length
            ? areaData.Mode[modeIndex]?.MapData
            : null;
        if (modeMap?.Levels != null && modeMap.Levels.Count <= 1) {
            try {
                modeMap.Reload();
            } catch (Exception e) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Could not reload area mode map data before map capture: " + e.Message);
            }
        }

        if (modeMap?.Levels != null &&
            (sessionMap?.Levels == null || modeMap.Levels.Count > sessionMap.Levels.Count)) {
            return modeMap;
        }

        return sessionMap;
    }

    public static void Stop() {
        if (!isScanning) {
            return;
        }

        scanCancelled = true;
        RestoreActivePlayerScanState(Engine.Scene as Level);
        Engine.Scene?.Add(new AkronToast("Stopping screenshot scan..."));
    }

    public static string Describe() {
        return isScanning ? "Scanning" : string.IsNullOrWhiteSpace(lastExportPath) ? "Ready" : Path.GetFileName(lastExportPath);
    }

    public static string DescribeProgress() {
        if (!isScanning) {
            return Describe();
        }

        if (scanRoomsTotal <= 0) {
            return "Scanning";
        }

        return "Scanning room " +
               Math.Min(scanRoomsCompleted + 1, scanRoomsTotal).ToString(System.Globalization.CultureInfo.InvariantCulture) +
               "/" +
               scanRoomsTotal.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static void MaintainActiveScanHost(Level level) {
        if (!isScanning || level == null || scannerHost == null) {
            return;
        }

        // Room reloads can remove the persistent scanner host from the active
        // entity list while leaving its Scene reference pointed at the level.
        // Check real list membership, not just Scene, so the coroutine keeps
        // receiving updates through multi-room map capture.
        EnsureScannerHostInLevel(level);
    }

    private static bool TryStart(Level level, out Player player) {
        player = level?.Tracker.GetEntity<Player>();
        if (level == null || player == null) {
            Engine.Scene?.Add(new AkronToast("Screenshot scanner needs an active level."));
            return false;
        }

        if (isScanning) {
            Engine.Scene?.Add(new AkronToast("Screenshot scan already running."));
            return false;
        }

        isScanning = true;
        scanCancelled = false;
        if (scannerHost?.Scene != level) {
            scannerHost?.RemoveSelf();
            scannerHost = new Entity { Tag = Tags.Persistent };
            level.Add(scannerHost);
        }

        return true;
    }

    private static void ResetFailedStart() {
        // TryStart reserves global scanner state before ScanChapter finishes
        // building the room queue. If startup throws before a coroutine owns
        // cleanup, unwind that reservation so later captures are not blocked.
        isScanning = false;
        scanCancelled = false;
        scanRoomsCompleted = 0;
        scanRoomsTotal = 0;
        scannerHost?.RemoveSelf();
        scannerHost = null;
    }

    private static IEnumerator ScanRooms(Player initialPlayer, Queue<string> rooms, bool buildMapComposite) {
        string initialRoom = initialPlayer.level.Session.Level;
        Vector2 initialPosition = initialPlayer.Position;
        Vector2 initialSpeed = initialPlayer.Speed;
        bool initialVisible = initialPlayer.Visible;
        bool initialCollidable = initialPlayer.Collidable;
        Collider initialCollider = initialPlayer.Collider;
        int initialState = initialPlayer.StateMachine.State;
        List<AkronScreenshotMergedRoom> mergedRooms = buildMapComposite ? new List<AkronScreenshotMergedRoom>() : null;
        int scannedRoomCount = 0;
        CaptureInitialPlayerState(initialVisible, initialCollidable, initialCollider, initialState);
        try {
            while (rooms.Count > 0 && isScanning && !scanCancelled) {
                Level level = Engine.Scene as Level;
                Player player = level?.Tracker.GetEntity<Player>();
                if (level == null || player == null) {
                    yield return null;
                    continue;
                }

                string room = rooms.Dequeue();
                if (!string.Equals(level.Session.Level, room, StringComparison.Ordinal)) {
                    yield return ChangeRoom(level, room);
                    level = Engine.Scene as Level;
                    player = level?.Tracker.GetEntity<Player>();
                    if (level == null || player == null || !string.Equals(level.Session.Level, room, StringComparison.Ordinal)) {
                        continue;
                    }
                }

                int mergedRoomCountBeforeScan = mergedRooms?.Count ?? 0;
                yield return ScanCurrentRoom(level, player, mergedRooms);
                if (!scanCancelled) {
                    scannedRoomCount++;
                    scanRoomsCompleted = scannedRoomCount;
                    if (buildMapComposite && mergedRooms.Count == mergedRoomCountBeforeScan) {
                        AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping map collage because room '" + room + "' could not be merged.");
                    }
                }
            }
            if (scanCancelled) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Screenshot scan cancelled after " + scannedRoomCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " scanned rooms; remaining queued rooms=" + rooms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            } else if (!isScanning && rooms.Count > 0) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Screenshot scan stopped after " + scannedRoomCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " scanned rooms; remaining queued rooms=" + rooms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".");
            }

            Level current = Engine.Scene as Level;
            Player currentPlayer = current?.Tracker.GetEntity<Player>();
            if (current != null && currentPlayer != null && !string.Equals(current.Session.Level, initialRoom, StringComparison.Ordinal)) {
                yield return ChangeRoom(current, initialRoom);
                current = Engine.Scene as Level;
                currentPlayer = current?.Tracker.GetEntity<Player>();
            }

            if (currentPlayer != null) {
                currentPlayer.Position = initialPosition;
                currentPlayer.Speed = initialSpeed;
                currentPlayer.Visible = initialVisible;
                currentPlayer.Collidable = initialCollidable;
                currentPlayer.Collider = initialCollider;
                if (currentPlayer.Scene != null) {
                    currentPlayer.StateMachine.State = initialState;
                }
            }

            if (!scanCancelled) {
                if (buildMapComposite && mergedRooms != null && mergedRooms.Count > 0) {
                    if (mergedRooms.Count != scannedRoomCount) {
                        AkronLog.Warn(nameof(AkronScreenshotScanner), "Writing map collage with " + mergedRooms.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " of " + scannedRoomCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " scanned room collages.");
                    }
                    TryWriteMergedChapterImage(current ?? Engine.Scene as Level, mergedRooms);
                }
                Engine.Scene?.Add(new AkronToast("Screenshot scan finished."));
            }
        } finally {
            isScanning = false;
            scanCancelled = false;
            allowScanRoomSetupTriggers = false;
            scannerHost = null;
            ClearInitialPlayerState();
        }
    }

    private static void CaptureInitialPlayerState(bool visible, bool collidable, Collider collider, int state) {
        hasInitialPlayerState = true;
        initialPlayerVisible = visible;
        initialPlayerCollidable = collidable;
        initialPlayerCollider = collider;
        initialPlayerState = state;
    }

    private static void ClearInitialPlayerState() {
        hasInitialPlayerState = false;
        initialPlayerVisible = false;
        initialPlayerCollidable = false;
        initialPlayerCollider = null;
        initialPlayerState = 0;
    }

    private static void RestoreActivePlayerScanState(Level level) {
        if (!hasInitialPlayerState) {
            return;
        }

        Player player = level?.Tracker.GetEntity<Player>();
        if (player == null) {
            return;
        }

        player.Visible = initialPlayerVisible;
        player.Collidable = initialPlayerCollidable;
        player.Collider = initialPlayerCollider;
        if (player.Scene != null) {
            player.StateMachine.State = initialPlayerState;
        }
    }

    private static IEnumerator ChangeRoom(Level level, string roomName) {
        LevelData nextRoom = GetScanMapData(level)?.Get(roomName);
        if (!CanScanChapterRoom(nextRoom)) {
            yield break;
        }

        float previousTime = level.TimeActive;
        float previousRawTime = level.RawTimeActive;
        Vector2 probe = new Vector2(nextRoom.Bounds.Left, nextRoom.Bounds.Bottom);
        Vector2 respawnPoint = Vector2.Zero;
        string previousSessionLevel = level.Session.Level;
        bool canLoadRoom = true;
        try {
            level.Session.Level = roomName;
            respawnPoint = level.Session.GetSpawnPoint(probe);
        } catch (Exception e) {
            canLoadRoom = false;
            AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping room '" + roomName + "' because no scan spawn could be resolved: " + e.Message);
        } finally {
            level.Session.Level = previousSessionLevel;
        }
        if (!canLoadRoom) {
            yield break;
        }

        bool roomLoaded = false;
        allowScanRoomSetupTriggers = true;
        try {
            level.OnEndOfFrame += () => {
                if (Engine.Scene != level) {
                    return;
                }

                level.Session.Level = roomName;
                level.Session.RespawnPoint = respawnPoint;
                level.Session.FirstLevel = false;
                level.Session.StartedFromBeginning = false;
                level.StartPosition = null;
                level.Tracker.GetEntitiesCopy<Player>().ForEach(player => player.RemoveSelf());
                level.UnloadLevel();
                level.Completed = false;
                level.InCutscene = false;
                level.SkippingCutscene = false;
                level.LoadLevel(Player.IntroTypes.Respawn);
                level.Entities.UpdateLists();
                AkronLevelRenderState.RelinkRendererCameras(level);
                EnsureScannerHostInLevel(level);
                level.Entities.UpdateLists();
                roomLoaded = true;
            };

            for (int i = 0; i < 30 && !roomLoaded; i++) {
                if (AkronModule.Settings.ScreenshotScannerFreezeTime) {
                    level.TimeActive = previousTime;
                    level.RawTimeActive = previousRawTime;
                }
                yield return null;
            }

            for (int i = 0; i < RoomLoadSettleFrames; i++) {
                if (AkronModule.Settings.ScreenshotScannerFreezeTime) {
                    level.TimeActive = previousTime;
                    level.RawTimeActive = previousRawTime;
                }

                EnsureScannerHostInLevel(level);
                yield return null;
            }
        } finally {
            allowScanRoomSetupTriggers = false;
        }
    }

    private static bool CanScanChapterRoom(LevelData room) {
        // Full-map capture moves through rooms by respawn-loading them. Custom
        // maps often include no-spawn FILLER rooms as editor/runtime utility
        // space; those are not playable rooms and can make tile generation or
        // loading pathological.
        if (room == null) {
            return false;
        }

        return !room.Name.StartsWith("FILLER", StringComparison.OrdinalIgnoreCase);
    }

    private static long EstimateMapOutputPixels(Level level, MapData mapData) {
        List<LevelData> rooms = (mapData?.Levels ?? new List<LevelData>())
            .Where(CanScanChapterRoom)
            .ToList();
        if (level == null || rooms.Count == 0) {
            return 0L;
        }

        int left = rooms.Min(room => room.Bounds.Left);
        int top = rooms.Min(room => room.Bounds.Top);
        int right = rooms.Max(room => room.Bounds.Right);
        int bottom = rooms.Max(room => room.Bounds.Bottom);
        int worldWidth = Math.Max(1, right - left);
        int worldHeight = Math.Max(1, bottom - top);
        float cameraWidth = level.Camera.Right - level.Camera.Left;
        float cameraHeight = level.Camera.Bottom - level.Camera.Top;
        double projectionX = AkronCapture.ScaledCaptureDimension(Engine.Viewport.Width) / Math.Max(1.0, cameraWidth);
        double projectionY = AkronCapture.ScaledCaptureDimension(Engine.Viewport.Height) / Math.Max(1.0, cameraHeight);
        double width = Math.Round((worldWidth + MapWorldPadding * 2) * projectionX);
        double height = Math.Round((worldHeight + MapWorldPadding * 2) * projectionY);
        double pixels = width * height;
        return pixels >= long.MaxValue ? long.MaxValue : (long) pixels;
    }

    private static string FormatPixelCount(long pixels) {
        if (pixels >= 1_000_000_000L) {
            return (pixels / 1_000_000_000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "B";
        }

        if (pixels >= 1_000_000L) {
            return (pixels / 1_000_000d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "M";
        }

        return pixels.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void EnsureScannerHostInLevel(Level level) {
        if (level == null || scannerHost == null || LevelHasScannerHost(level)) {
            return;
        }

        level.Add(scannerHost);
    }

    private static bool LevelHasScannerHost(Level level) {
        foreach (Entity entity in level.Entities) {
            if (ReferenceEquals(entity, scannerHost)) {
                return true;
            }
        }

        foreach (Entity entity in level.Entities.ToAdd) {
            if (ReferenceEquals(entity, scannerHost)) {
                return true;
            }
        }

        return false;
    }

    private static IEnumerator ScanCurrentRoom(Level level, Player player, List<AkronScreenshotMergedRoom> mergedRooms) {
        Rectangle bounds = level.Bounds;
        float cameraWidth = level.Camera.Right - level.Camera.Left;
        float cameraHeight = level.Camera.Bottom - level.Camera.Top;
        int viewportWidth = AkronCapture.ScaledCaptureDimension(Engine.Viewport.Width);
        int viewportHeight = AkronCapture.ScaledCaptureDimension(Engine.Viewport.Height);
        int stepX = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(AkronModule.Settings.ScreenshotScannerHorizontalOffsetTiles) * TileSize;
        int stepY = AkronModuleSettings.ClampScreenshotScannerOffsetTiles(AkronModule.Settings.ScreenshotScannerVerticalOffsetTiles) * TileSize;
        int waitFrames = AkronModuleSettings.ClampScreenshotScannerWaitFrames(AkronModule.Settings.ScreenshotScannerWaitFrames);
        float previousTime = level.TimeActive;
        float previousRawTime = level.RawTimeActive;
        Vector2 previousPlayerPosition = player.Position;
        Vector2 previousPlayerSpeed = player.Speed;
        bool previousPlayerVisible = player.Visible;
        bool previousPlayerCollidable = player.Collidable;
        Collider previousPlayerCollider = player.Collider;
        int previousPlayerState = player.StateMachine.State;
        BackdropRenderer previousBackground = level.Background;
        BackdropRenderer previousForeground = level.Foreground;
        Level.CameraLockModes previousCameraLockMode = level.CameraLockMode;
        bool suppressMadeline = AkronModule.Settings.ScreenshotScannerNoclipHideMadeline;
        bool freezeTime = AkronModule.Settings.ScreenshotScannerFreezeTime;
        Entity timeStopEntity = null;

        try {
            // Keep capture suppression local. Reusing global Hide Player/Noclip
            // would leave persistent user-facing state behind after export.
            if (suppressMadeline) {
                SuppressPlayerForScan(player);
            }
            level.CameraLockMode = Level.CameraLockModes.None;
            if (freezeTime) {
                timeStopEntity = new Entity();
                timeStopEntity.Add(new TimeRateModifier(0f));
                level.Add(timeStopEntity);
            }
            if (AkronModule.Settings.ScreenshotScannerRemoveBackground) {
                level.Background = new BackdropRenderer();
            }
            if (AkronModule.Settings.ScreenshotScannerRemoveForeground) {
                level.Foreground = new BackdropRenderer();
            }

            WriteMetadata(level, bounds, cameraWidth, cameraHeight, viewportWidth, viewportHeight);
            foreach (AkronScreenshotScanTile tile in BuildScanTiles(bounds, cameraWidth, cameraHeight, stepX, stepY)) {
                if (!isScanning || scanCancelled) {
                    break;
                }

                Vector2 camera = new Vector2(tile.CameraX, tile.CameraY);
                level.Camera.Position = camera;
                player.Position = camera + new Vector2(cameraWidth / 2f, cameraHeight / 2f);
                player.Speed = Vector2.Zero;
                if (suppressMadeline) {
                    SuppressPlayerForScan(player);
                }

                for (int i = 0; i < waitFrames; i++) {
                    if (freezeTime) {
                        level.TimeActive = previousTime;
                        level.RawTimeActive = previousRawTime;
                    }
                    yield return null;
                    level.Camera.Position = camera;
                    player.Position = camera + new Vector2(cameraWidth / 2f, cameraHeight / 2f);
                    player.Speed = Vector2.Zero;
                    if (suppressMadeline) {
                        SuppressPlayerForScan(player);
                    }
                }

                if (freezeTime) {
                    level.TimeActive = previousTime;
                    level.RawTimeActive = previousRawTime;
                }

                string fileName = tile.Index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) +
                                  ",dx=" + tile.DeltaX.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                  ",dy=" + tile.DeltaY.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                  ImageExtension();
                lastExportPath = BuildPath(level, fileName);
                // Keep raw tiles clean and draw scanner markers after stitching.
                // Drawing markers into each camera screenshot makes large room
                // or map collages resample 5px borders down to subpixel noise.
                AkronCapture.CaptureToPath(level, lastExportPath);
            }

            if (isScanning && !scanCancelled && TryWriteMergedRoomImage(level, bounds, cameraWidth, cameraHeight, viewportWidth, viewportHeight, out AkronScreenshotMergedRoom mergedRoom)) {
                mergedRooms?.Add(mergedRoom);
            }
        } finally {
            player.Position = previousPlayerPosition;
            player.Speed = previousPlayerSpeed;
            player.Visible = previousPlayerVisible;
            player.Collidable = previousPlayerCollidable;
            player.Collider = previousPlayerCollider;
            if (player.Scene != null && player.StateMachine.State == Player.StDummy) {
                player.StateMachine.State = previousPlayerState;
            }
            level.Background = previousBackground;
            level.Foreground = previousForeground;
            level.CameraLockMode = previousCameraLockMode;
            timeStopEntity?.RemoveSelf();
            level.TimeActive = previousTime;
            level.RawTimeActive = previousRawTime;
        }
    }

    private static void SuppressPlayerForScan(Player player) {
        player.Visible = false;
        player.Collidable = false;
        player.Collider = new Hitbox(0f, 0f);
        player.StateMachine.State = Player.StDummy;
    }

    internal static IReadOnlyList<AkronScreenshotScanTile> BuildScanTiles(Rectangle bounds, float cameraWidth, float cameraHeight, int stepX, int stepY) {
        return BuildScanTiles(bounds.X, bounds.Y, bounds.Width, bounds.Height, cameraWidth, cameraHeight, stepX, stepY);
    }

    internal static IReadOnlyList<AkronScreenshotScanTile> BuildScanTiles(int roomLeft, int roomTop, int roomWidth, int roomHeight, float cameraWidth, float cameraHeight, int stepX, int stepY) {
        List<AkronScreenshotScanTile> tiles = new List<AkronScreenshotScanTile>();
        IReadOnlyList<float> centersX = BuildAxisCenters(roomWidth, cameraWidth, stepX);
        IReadOnlyList<float> centersY = BuildAxisCenters(roomHeight, cameraHeight, stepY);
        int index = 0;
        foreach (float centerY in centersY) {
            foreach (float centerX in centersX) {
                tiles.Add(new AkronScreenshotScanTile(
                    index++,
                    roomLeft + centerX - cameraWidth / 2f,
                    roomTop + centerY - cameraHeight / 2f,
                    (int) centerX,
                    (int) centerY));
            }
        }

        return tiles;
    }

    private static IReadOnlyList<float> BuildAxisCenters(int roomSize, float cameraSize, int step) {
        List<float> centers = new List<float>();
        float firstCenter = cameraSize / 2f;
        float lastCenter = roomSize - cameraSize / 2f;
        if (lastCenter <= firstCenter) {
            centers.Add(firstCenter);
            return centers;
        }

        for (float center = firstCenter; center < lastCenter; center += step) {
            centers.Add(center);
        }

        if (centers.Count == 0 || Math.Abs(centers[centers.Count - 1] - lastCenter) > 0.01f) {
            centers.Add(lastCenter);
        }

        return centers;
    }

    private static string BuildPath(Level level, string fileName) {
        string root = AkronModuleSettings.NormalizeScreenshotScannerExportPath(AkronModule.Settings.ScreenshotScannerExportPath);
        string sid = level.Session.Area.GetSID();
        char side = (char) ('A' + (int) level.Session.Area.Mode);
        string path = Path.Combine(Everest.PathGame, root, sid, side.ToString(), level.Session.Level, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        return path;
    }

    private static string BuildSidePath(Level level, string fileName) {
        string root = AkronModuleSettings.NormalizeScreenshotScannerExportPath(AkronModule.Settings.ScreenshotScannerExportPath);
        string sid = level.Session.Area.GetSID();
        char side = (char) ('A' + (int) level.Session.Area.Mode);
        string path = Path.Combine(Everest.PathGame, root, sid, side.ToString(), fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        return path;
    }

    private static void WriteMetadata(Level level, Rectangle bounds, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight) {
        string path = BuildPath(level, "room.json");
        File.WriteAllText(path, BuildRoomMetadataJson(bounds, cameraWidth, cameraHeight, viewportWidth, viewportHeight));
        lastExportPath = path;
    }

    internal static string BuildRoomMetadataJson(Rectangle bounds, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight) {
        return BuildRoomMetadataJson(bounds.X, bounds.Y, bounds.Width, bounds.Height, cameraWidth, cameraHeight, viewportWidth, viewportHeight);
    }

    internal static string BuildRoomMetadataJson(int roomLeft, int roomTop, int roomWidth, int roomHeight, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight) {
        var metadata = new {
            roomPosition = new[] { roomLeft, roomTop },
            roomSize = new[] { roomWidth, roomHeight },
            cameraSize = new[] { cameraWidth, cameraHeight },
            viewPort = new[] { viewportWidth, viewportHeight }
        };
        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
    }

    internal static (int X, int Y) BuildMergedImageSize(int roomWidth, int roomHeight, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight) {
        float projectionX = viewportWidth / cameraWidth;
        float projectionY = viewportHeight / cameraHeight;
        return ((int) (roomWidth * projectionX), (int) (roomHeight * projectionY));
    }

    internal static (int X, int Y) BuildMergedTilePosition(int deltaX, int deltaY, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight) {
        float projectionX = viewportWidth / cameraWidth;
        float projectionY = viewportHeight / cameraHeight;
        return ((int) ((deltaX - cameraWidth / 2f) * projectionX), (int) ((deltaY - cameraHeight / 2f) * projectionY));
    }

    internal static (int Width, int Height, float Scale) BuildRuntimeSafeImageSize(int fullWidth, int fullHeight) {
        float scale = 1f;
        long fullPixels = (long) fullWidth * fullHeight;
        bool needsDownscale = fullWidth > MaxMergedImageDimension ||
                              fullHeight > MaxMergedImageDimension ||
                              fullPixels > MaxMergedImagePixels;
        if (!needsDownscale) {
            return (fullWidth, fullHeight, scale);
        }

        if (fullWidth > MaxDownscaledImageDimension || fullHeight > MaxDownscaledImageDimension) {
            scale = Math.Min(MaxDownscaledImageDimension / (float) fullWidth, MaxDownscaledImageDimension / (float) fullHeight);
        }

        long pixels = (long) (fullWidth * scale) * (long) (fullHeight * scale);
        if (pixels > MaxMergedImagePixels) {
            scale *= (float) Math.Sqrt(MaxMergedImagePixels / (double) pixels);
        }

        return (Math.Max(1, (int) (fullWidth * scale)), Math.Max(1, (int) (fullHeight * scale)), scale);
    }

    internal static bool TryParseScanTileFileName(string fileName, out int deltaX, out int deltaY) {
        deltaX = 0;
        deltaY = 0;
        string stem = Path.GetFileNameWithoutExtension(fileName);
        string[] chunks = stem.Split(',');
        if (chunks.Length < 3) {
            return false;
        }

        bool hasDeltaX = false;
        bool hasDeltaY = false;
        for (int i = 1; i < chunks.Length; i++) {
            string chunk = chunks[i];
            if (chunk.StartsWith("dx=", StringComparison.Ordinal) && int.TryParse(chunk.Substring(3), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedDeltaX)) {
                deltaX = parsedDeltaX;
                hasDeltaX = true;
            } else if (chunk.StartsWith("dy=", StringComparison.Ordinal) && int.TryParse(chunk.Substring(3), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int parsedDeltaY)) {
                deltaY = parsedDeltaY;
                hasDeltaY = true;
            }
        }

        return hasDeltaX && hasDeltaY;
    }

    internal static bool CanCreateMergedImage(int width, int height, out string reason) {
        if (width <= 0 || height <= 0) {
            reason = "invalid dimensions " + width + "x" + height;
            return false;
        }

        long pixels = (long) width * height;
        if (width > MaxMergedImageDimension || height > MaxMergedImageDimension) {
            reason = "dimensions " + width + "x" + height + " exceed " + MaxMergedImageDimension + "px";
            return false;
        }

        if (pixels > MaxMergedImagePixels) {
            reason = "pixel count " + pixels + " exceeds " + MaxMergedImagePixels;
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool CanAllocatePixelBuffer(int width, int height, out string reason) {
        if (width <= 0 || height <= 0) {
            reason = "invalid dimensions " + width + "x" + height;
            return false;
        }

        long pixels = (long) width * height;
        if (pixels > int.MaxValue) {
            reason = "pixel buffer " + pixels + " exceeds the maximum supported in-memory room collage size";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsPngExport() {
        return AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat) == AkronScreenshotImageFormat.Png;
    }

    private static bool TryWriteMergedRoomImage(Level level, Rectangle bounds, float cameraWidth, float cameraHeight, int viewportWidth, int viewportHeight, out AkronScreenshotMergedRoom mergedRoom) {
        mergedRoom = default;
        string outputPath = BuildPath(level, "merged" + ImageExtension());
        string roomDirectory = Path.GetDirectoryName(outputPath) ?? ".";
        try {
            List<string> tileFiles = Directory.EnumerateFiles(roomDirectory, "*" + ImageExtension())
                .Where(file => TryParseScanTileFileName(Path.GetFileName(file), out _, out _))
                .OrderBy(Path.GetFileName, StringComparer.Ordinal)
                .ToList();
            if (tileFiles.Count == 0) {
                return false;
            }

            var fullCanvasSize = BuildMergedImageSize(bounds.Width, bounds.Height, cameraWidth, cameraHeight, viewportWidth, viewportHeight);
            var canvasSize = IsPngExport()
                ? (Width: fullCanvasSize.X, Height: fullCanvasSize.Y, Scale: 1f)
                : BuildRuntimeSafeImageSize(fullCanvasSize.X, fullCanvasSize.Y);
            if (!CanCreateMergedImage(canvasSize.Width, canvasSize.Height, out string reason) && !IsPngExport()) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping room collage for '" + level.Session.Level + "': " + reason + ".");
                return false;
            }
            if (!CanAllocatePixelBuffer(canvasSize.Width, canvasSize.Height, out reason)) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping room collage for '" + level.Session.Level + "': " + reason + ".");
                return false;
            }
            if (canvasSize.Scale < 1f) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Writing downscaled room collage for '" + level.Session.Level + "' at " + canvasSize.Width + "x" + canvasSize.Height + " because the full room exceeds runtime texture limits.");
            }

            Color[] canvas = new Color[canvasSize.Width * canvasSize.Height];
            int blendedTileCount = 0;
            foreach (string tilePath in tileFiles) {
                if (!TryParseScanTileFileName(Path.GetFileName(tilePath), out int deltaX, out int deltaY)) {
                    continue;
                }

                Color[] tilePixels;
                int tileWidth;
                int tileHeight;
                try {
                    tilePixels = LoadTexturePixels(tilePath, out tileWidth, out tileHeight);
                } catch (Exception e) {
                    AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping unreadable scan tile '" + Path.GetFileName(tilePath) + "' for room '" + level.Session.Level + "': " + e.Message);
                    continue;
                }

                var position = BuildMergedTilePosition(deltaX, deltaY, cameraWidth, cameraHeight, viewportWidth, viewportHeight);
                if (canvasSize.Scale >= 0.999f) {
                    BlendTile(canvas, canvasSize.Width, canvasSize.Height, tilePixels, tileWidth, tileHeight, position.X, position.Y);
                } else {
                    int targetX = (int) Math.Round(position.X * canvasSize.Scale);
                    int targetY = (int) Math.Round(position.Y * canvasSize.Scale);
                    int targetRight = (int) Math.Round((position.X + tileWidth) * canvasSize.Scale);
                    int targetBottom = (int) Math.Round((position.Y + tileHeight) * canvasSize.Scale);
                    BlendScaledTileNearestToRect(canvas, canvasSize.Width, canvasSize.Height, tilePixels, tileWidth, tileHeight, targetX, targetY, Math.Max(1, targetRight - targetX), Math.Max(1, targetBottom - targetY));
                }
                blendedTileCount++;
            }

            if (blendedTileCount == 0) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping room collage for '" + level.Session.Level + "': no readable scan tiles.");
                return false;
            }

            DrawScannerExportMarkers(canvas, canvasSize.Width, canvasSize.Height, level.Session.Area.GetSID(), level.Session.Level, bounds, 0, 0, projectionX: viewportWidth / cameraWidth * canvasSize.Scale, projectionY: viewportHeight / cameraHeight * canvasSize.Scale);
            SaveMergedImage(outputPath, canvas, canvasSize.Width, canvasSize.Height);
            lastExportPath = outputPath;
            mergedRoom = new AkronScreenshotMergedRoom(level.Session.Level, outputPath, roomDirectory, bounds, cameraWidth, cameraHeight, viewportWidth, viewportHeight, canvasSize.Scale);
            return true;
        } catch (Exception e) {
            AkronLog.Warn(nameof(AkronScreenshotScanner), "Failed to write room collage for '" + level.Session.Level + "': " + e.Message);
            return false;
        }
    }

    private static bool TryWriteMergedChapterImage(Level level, IReadOnlyList<AkronScreenshotMergedRoom> rooms) {
        if (level == null || rooms == null || rooms.Count == 0) {
            return false;
        }

        try {
            float projectionX = rooms[0].ProjectionX;
            float projectionY = rooms[0].ProjectionY;
            List<AkronScreenshotRoomImage> roomImagesForLayout = rooms
                .Select(room => new AkronScreenshotRoomImage(
                    room.RoomName,
                    room.Bounds,
                    Math.Max(1, (int) Math.Round(room.Bounds.Width * room.ProjectionX * room.MergedScale)),
                    Math.Max(1, (int) Math.Round(room.Bounds.Height * room.ProjectionY * room.MergedScale))))
                .ToList();

            BuildMapWorldLayout(roomImagesForLayout, out int fullWidth, out int fullHeight, projectionX, projectionY);
            if (AkronModule.Settings.ScreenshotScannerDownscaleMapCapture) {
                var safeSize = BuildRuntimeSafeImageSize(fullWidth, fullHeight);
                projectionX *= safeSize.Scale;
                projectionY *= safeSize.Scale;
                if (safeSize.Scale < 0.999f) {
                    AkronLog.Warn(nameof(AkronScreenshotScanner), "Writing downscaled map collage at " + safeSize.Width + "x" + safeSize.Height + " because Downscale is enabled.");
                }
            }

            IReadOnlyList<AkronScreenshotMapPlacement> placements = BuildMapWorldLayout(roomImagesForLayout, out int width, out int height, projectionX, projectionY);
            string outputPath = BuildSidePath(level, "map" + ImageExtension());
            if (IsPngExport()) {
                SaveMergedMapImageStreaming(outputPath, rooms, placements, width, height);
                lastExportPath = outputPath;
                return true;
            }

            if (!CanCreateMergedImage(width, height, out string reason)) {
                AkronLog.Warn(nameof(AkronScreenshotScanner), "Skipping map collage: " + reason + ".");
                return false;
            }

            List<(AkronScreenshotMergedRoom Room, Color[] Pixels, int Width, int Height)> roomImages = new List<(AkronScreenshotMergedRoom Room, Color[] Pixels, int Width, int Height)>();
            foreach (AkronScreenshotMergedRoom room in rooms) {
                Color[] pixels = LoadMergedImagePixels(room.ImagePath, out int imageWidth, out int imageHeight);
                roomImages.Add((room, pixels, imageWidth, imageHeight));
            }

            Color[] canvas = new Color[width * height];
            for (int i = 0; i < roomImages.Count; i++) {
                var roomImage = roomImages[i];
                AkronScreenshotMapPlacement placement = placements[i];
                if (placement.Width == roomImage.Width && placement.Height == roomImage.Height) {
                    BlendTile(canvas, width, height, roomImage.Pixels, roomImage.Width, roomImage.Height, placement.X, placement.Y);
                } else {
                    BlendScaledTileNearestToRect(canvas, width, height, roomImage.Pixels, roomImage.Width, roomImage.Height, placement.X, placement.Y, placement.Width, placement.Height);
                }

            }

            SaveMergedImage(outputPath, canvas, width, height);
            lastExportPath = outputPath;
            return true;
        } catch (Exception e) {
            AkronLog.Warn(nameof(AkronScreenshotScanner), "Failed to write map collage: " + e.Message);
            return false;
        }
    }

    internal static IReadOnlyList<AkronScreenshotMapPlacement> BuildMapWorldLayout(IReadOnlyList<AkronScreenshotRoomImage> rooms, out int width, out int height, float projectionX = 1f, float projectionY = 1f) {
        if (rooms == null || rooms.Count == 0) {
            width = 0;
            height = 0;
            return Array.Empty<AkronScreenshotMapPlacement>();
        }

        int left = rooms.Min(room => room.BoundsX);
        int top = rooms.Min(room => room.BoundsY);
        int right = rooms.Max(room => room.BoundsX + room.BoundsWidth);
        int bottom = rooms.Max(room => room.BoundsY + room.BoundsHeight);
        int worldWidth = Math.Max(1, right - left);
        int worldHeight = Math.Max(1, bottom - top);
        width = Math.Max(1, (int) Math.Round((worldWidth + MapWorldPadding * 2) * projectionX));
        height = Math.Max(1, (int) Math.Round((worldHeight + MapWorldPadding * 2) * projectionY));

        List<AkronScreenshotMapPlacement> placements = new List<AkronScreenshotMapPlacement>(rooms.Count);
        foreach (AkronScreenshotRoomImage room in rooms) {
            int roomX = Math.Max(0, Math.Min(width, (int) Math.Round((MapWorldPadding + room.BoundsX - left) * projectionX)));
            int roomY = Math.Max(0, Math.Min(height, (int) Math.Round((MapWorldPadding + room.BoundsY - top) * projectionY)));
            int roomRight = Math.Max(0, Math.Min(width, (int) Math.Round((MapWorldPadding + room.BoundsX + room.BoundsWidth - left) * projectionX)));
            int roomBottom = Math.Max(0, Math.Min(height, (int) Math.Round((MapWorldPadding + room.BoundsY + room.BoundsHeight - top) * projectionY)));
            int roomWidth = Math.Max(1, roomRight - roomX);
            int roomHeight = Math.Max(1, roomBottom - roomY);
            float imageScaleX = roomWidth / (float) Math.Max(1, room.Width);
            float imageScaleY = roomHeight / (float) Math.Max(1, room.Height);
            placements.Add(new AkronScreenshotMapPlacement(
                room.RoomName,
                roomX,
                roomY,
                roomWidth,
                roomHeight,
                Math.Min(imageScaleX, imageScaleY)));
        }

        return placements;
    }

    private static void DrawScannerExportMarkers(Color[] canvas, int canvasWidth, int canvasHeight, string areaSid, string roomName, Rectangle roomBounds, int roomTargetX, int roomTargetY, float projectionX, float projectionY) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (settings == null || !settings.ScreenshotScannerExportMarkers) {
            return;
        }

        if (projectionX <= 0f || projectionY <= 0f) {
            return;
        }

        if (settings.ScreenshotScannerExportAutoKillAreas) {
            foreach (Rectangle area in AkronModule.GetAutoKillAreas()) {
                Rectangle visibleArea = Rectangle.Intersect(area, roomBounds);
                if (visibleArea.Width > 0 && visibleArea.Height > 0) {
                    DrawScannerExportAreaMarker(canvas, canvasWidth, canvasHeight, roomBounds, roomTargetX, roomTargetY, projectionX, projectionY, visibleArea, Color.OrangeRed);
                }
            }
        }

        if (settings.ScreenshotScannerExportAutoDeafenAreas) {
            foreach (Rectangle area in AkronModule.GetAutoDeafenAreas()) {
                Rectangle visibleArea = Rectangle.Intersect(area, roomBounds);
                if (visibleArea.Width > 0 && visibleArea.Height > 0) {
                    DrawScannerExportAreaMarker(canvas, canvasWidth, canvasHeight, roomBounds, roomTargetX, roomTargetY, projectionX, projectionY, visibleArea, Color.DeepSkyBlue);
                }
            }
        }

        if (!settings.ScreenshotScannerExportStartPositions) {
            return;
        }

        foreach (KeyValuePair<int, AkronStartPos> pair in AkronActions.GetStartPositionsForArea(areaSid)) {
            AkronStartPos startPos = pair.Value;
            if (startPos == null ||
                !string.Equals(startPos.AreaSid, areaSid, StringComparison.Ordinal) ||
                !string.Equals(startPos.Room, roomName, StringComparison.Ordinal)) {
                continue;
            }

            Rectangle hitbox = new Rectangle((int) Math.Round(startPos.Position.X - 4f), (int) Math.Round(startPos.Position.Y - 11f), 8, 11);
            Rectangle visibleHitbox = Rectangle.Intersect(hitbox, roomBounds);
            if (visibleHitbox.Width <= 0 || visibleHitbox.Height <= 0) {
                continue;
            }

            Rectangle marker = WorldRectToCanvasRect(roomBounds, roomTargetX, roomTargetY, projectionX, projectionY, visibleHitbox);
            DrawScannerExportMarkerRect(canvas, canvasWidth, canvasHeight, marker, projectionX, projectionY, Color.Magenta, 0.16f);
            DrawScannerExportStartPosLabel(canvas, canvasWidth, canvasHeight, marker, projectionX, projectionY, pair.Key);
        }
    }

    private static void DrawScannerExportAreaMarker(Color[] canvas, int canvasWidth, int canvasHeight, Rectangle roomBounds, int roomTargetX, int roomTargetY, float projectionX, float projectionY, Rectangle area, Color color) {
        Rectangle marker = WorldRectToCanvasRect(roomBounds, roomTargetX, roomTargetY, projectionX, projectionY, area);
        DrawScannerExportMarkerRect(canvas, canvasWidth, canvasHeight, marker, projectionX, projectionY, color, 0.16f);
    }

    private static Rectangle WorldRectToCanvasRect(Rectangle roomBounds, int roomTargetX, int roomTargetY, float projectionX, float projectionY, Rectangle worldRect) {
        int left = roomTargetX + (int) Math.Round((worldRect.Left - roomBounds.Left) * projectionX);
        int top = roomTargetY + (int) Math.Round((worldRect.Top - roomBounds.Top) * projectionY);
        int right = roomTargetX + (int) Math.Round((worldRect.Right - roomBounds.Left) * projectionX);
        int bottom = roomTargetY + (int) Math.Round((worldRect.Bottom - roomBounds.Top) * projectionY);
        return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static void DrawScannerExportMarkerRect(Color[] canvas, int canvasWidth, int canvasHeight, Rectangle marker, float projectionX, float projectionY, Color color, float fillOpacity) {
        int borderWidth = ScannerExportBorderPixels(projectionX, marker.Width);
        int borderHeight = ScannerExportBorderPixels(projectionY, marker.Height);
        DrawFilledRect(canvas, canvasWidth, canvasHeight, marker, WithAlpha(color, fillOpacity));
        DrawFilledRect(canvas, canvasWidth, canvasHeight, new Rectangle(marker.X, marker.Y, marker.Width, borderHeight), color);
        DrawFilledRect(canvas, canvasWidth, canvasHeight, new Rectangle(marker.X, marker.Bottom - borderHeight, marker.Width, borderHeight), color);
        DrawFilledRect(canvas, canvasWidth, canvasHeight, new Rectangle(marker.X, marker.Y, borderWidth, marker.Height), color);
        DrawFilledRect(canvas, canvasWidth, canvasHeight, new Rectangle(marker.Right - borderWidth, marker.Y, borderWidth, marker.Height), color);
    }

    private static int ScannerExportBorderPixels(float projection, int maxSize) {
        return Math.Min(maxSize, Math.Max(1, (int) Math.Round(ScannerExportMarkerBorder * projection)));
    }

    private static void DrawScannerExportStartPosLabel(Color[] canvas, int canvasWidth, int canvasHeight, Rectangle marker, float projectionX, float projectionY, int slot) {
        string label = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int glyphWidth = label.Length * 3 + Math.Max(0, label.Length - 1);
        int glyphHeight = 5;
        int borderWidth = ScannerExportBorderPixels(projectionX, marker.Width);
        int borderHeight = ScannerExportBorderPixels(projectionY, marker.Height);
        int availableWidth = marker.Width - borderWidth * 2 - 4;
        int availableHeight = marker.Height - borderHeight * 2 - 4;
        if (availableWidth < glyphWidth || availableHeight < glyphHeight) {
            return;
        }

        int scale = Math.Max(1, Math.Min(availableWidth / glyphWidth, availableHeight / glyphHeight));
        int textWidth = glyphWidth * scale;
        int textHeight = glyphHeight * scale;
        int x = marker.X + (marker.Width - textWidth) / 2;
        int y = marker.Y + (marker.Height - textHeight) / 2;

        for (int oy = -1; oy <= 1; oy++) {
            for (int ox = -1; ox <= 1; ox++) {
                if (ox != 0 || oy != 0) {
                    DrawScannerExportDigits(canvas, canvasWidth, canvasHeight, label, x + ox, y + oy, scale, Color.Black);
                }
            }
        }

        DrawScannerExportDigits(canvas, canvasWidth, canvasHeight, label, x, y, scale, Color.Magenta);
    }

    private static void DrawScannerExportDigits(Color[] canvas, int canvasWidth, int canvasHeight, string label, int x, int y, int scale, Color color) {
        int cursorX = x;
        foreach (char digit in label) {
            string[] glyph = DigitGlyph(digit);
            if (glyph.Length == 0) {
                cursorX += scale * 4;
                continue;
            }

            for (int gy = 0; gy < glyph.Length; gy++) {
                for (int gx = 0; gx < glyph[gy].Length; gx++) {
                    if (glyph[gy][gx] == '1') {
                        DrawFilledRect(canvas, canvasWidth, canvasHeight, new Rectangle(cursorX + gx * scale, y + gy * scale, scale, scale), color);
                    }
                }
            }

            cursorX += scale * 4;
        }
    }

    private static string[] DigitGlyph(char digit) {
        switch (digit) {
            case '0':
                return new[] { "111", "101", "101", "101", "111" };
            case '1':
                return new[] { "010", "110", "010", "010", "111" };
            case '2':
                return new[] { "111", "001", "111", "100", "111" };
            case '3':
                return new[] { "111", "001", "111", "001", "111" };
            case '4':
                return new[] { "101", "101", "111", "001", "001" };
            case '5':
                return new[] { "111", "100", "111", "001", "111" };
            case '6':
                return new[] { "111", "100", "111", "101", "111" };
            case '7':
                return new[] { "111", "001", "010", "010", "010" };
            case '8':
                return new[] { "111", "101", "111", "101", "111" };
            case '9':
                return new[] { "111", "101", "111", "001", "111" };
            default:
                return Array.Empty<string>();
        }
    }

    private static Color WithAlpha(Color color, float opacity) {
        int alpha = (int) MathHelper.Clamp(opacity * 255f, 0f, 255f);
        return new Color(color.R, color.G, color.B, alpha);
    }

    private static void DrawFilledRect(Color[] canvas, int canvasWidth, int canvasHeight, Rectangle rect, Color color) {
        int startX = Math.Max(0, rect.X);
        int startY = Math.Max(0, rect.Y);
        int endX = Math.Min(canvasWidth, rect.Right);
        int endY = Math.Min(canvasHeight, rect.Bottom);
        if (startX >= endX || startY >= endY || color.A == 0) {
            return;
        }

        for (int y = startY; y < endY; y++) {
            int row = y * canvasWidth;
            for (int x = startX; x < endX; x++) {
                int index = row + x;
                canvas[index] = color.A == 255 ? color : BlendOver(color, canvas[index]);
            }
        }
    }

    private static Color[] LoadTexturePixels(string path, out int width, out int height) {
        using FileStream stream = File.OpenRead(path);
        Texture2D texture = Texture2D.FromStream(Engine.Instance.GraphicsDevice, stream);
        try {
            width = texture.Width;
            height = texture.Height;
            Color[] pixels = new Color[width * height];
            texture.GetData(pixels);
            return pixels;
        } finally {
            texture.Dispose();
        }
    }

    private static Color[] LoadMergedImagePixels(string path, out int width, out int height) {
        if (AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat) == AkronScreenshotImageFormat.Png) {
            return LoadPngPixels(path, out width, out height);
        }

        return LoadTexturePixels(path, out width, out height);
    }

    private static void SaveMergedMapImageStreaming(string path, IReadOnlyList<AkronScreenshotMergedRoom> rooms, IReadOnlyList<AkronScreenshotMapPlacement> placements, int width, int height) {
        SaveMergedTexture(path, stream => {
            Dictionary<int, (Color[] Pixels, int Width, int Height)> cache = new Dictionary<int, (Color[] Pixels, int Width, int Height)>();
            SavePngRows(stream, width, height, (y, row) => {
                Array.Clear(row, 0, row.Length);

                List<int> stale = null;
                foreach (KeyValuePair<int, (Color[] Pixels, int Width, int Height)> entry in cache) {
                    AkronScreenshotMapPlacement placement = placements[entry.Key];
                    if (y < placement.Y || y >= placement.Y + placement.Height) {
                        stale ??= new List<int>();
                        stale.Add(entry.Key);
                    }
                }

                if (stale != null) {
                    foreach (int index in stale) {
                        cache.Remove(index);
                    }
                }

                for (int i = 0; i < rooms.Count; i++) {
                    AkronScreenshotMapPlacement placement = placements[i];
                    if (y < placement.Y || y >= placement.Y + placement.Height) {
                        continue;
                    }

                    if (!cache.TryGetValue(i, out var roomImage)) {
                        Color[] pixels = LoadMergedImagePixels(rooms[i].ImagePath, out int imageWidth, out int imageHeight);
                        roomImage = (pixels, imageWidth, imageHeight);
                        cache[i] = roomImage;
                    }

                    int targetStartX = Math.Max(0, placement.X);
                    int targetEndX = Math.Min(width, placement.X + placement.Width);
                    if (targetStartX >= targetEndX) {
                        continue;
                    }

                    int sourceY = Math.Min(roomImage.Height - 1, Math.Max(0, (int) ((y - placement.Y) * roomImage.Height / (float) placement.Height)));
                    int sourceRow = sourceY * roomImage.Width;
                    for (int x = targetStartX; x < targetEndX; x++) {
                        int sourceX = Math.Min(roomImage.Width - 1, Math.Max(0, (int) ((x - placement.X) * roomImage.Width / (float) placement.Width)));
                        Color source = roomImage.Pixels[sourceRow + sourceX];
                        if (source.A == 0) {
                            continue;
                        }

                        int target = x * 4;
                        if (source.A == 255 || row[target + 3] == 0) {
                            row[target] = source.R;
                            row[target + 1] = source.G;
                            row[target + 2] = source.B;
                            row[target + 3] = source.A;
                        } else {
                            BlendOverRowPixel(row, target, source);
                        }
                    }
                }
            });
        });
    }

    private static void BlendOverRowPixel(byte[] row, int target, Color source) {
        int sourceA = source.A;
        int destA = row[target + 3];
        int inverseA = 255 - sourceA;
        int outA = sourceA + destA * inverseA / 255;
        if (outA <= 0) {
            row[target] = 0;
            row[target + 1] = 0;
            row[target + 2] = 0;
            row[target + 3] = 0;
            return;
        }

        row[target] = (byte) ((source.R * sourceA + row[target] * destA * inverseA / 255) / outA);
        row[target + 1] = (byte) ((source.G * sourceA + row[target + 1] * destA * inverseA / 255) / outA);
        row[target + 2] = (byte) ((source.B * sourceA + row[target + 2] * destA * inverseA / 255) / outA);
        row[target + 3] = (byte) outA;
    }

    private static void SaveMergedImage(string path, Color[] pixels, int width, int height) {
        if (IsPngExport()) {
            SaveMergedTexture(path, stream => SavePngFromPixels(stream, pixels, width, height));
            return;
        }

        using Texture2D texture = new Texture2D(Engine.Instance.GraphicsDevice, width, height, false, SurfaceFormat.Color);
        texture.SetData(pixels);
        SaveMergedTexture(path, stream => AkronCapture.SaveTexture(stream, texture, width, height));
    }

    private static void SavePngFromPixels(Stream stream, Color[] pixels, int width, int height) {
        SavePngRows(stream, width, height, (y, row) => {
            int sourceRow = y * width;
            int target = 0;
            for (int x = 0; x < width; x++) {
                Color color = pixels[sourceRow + x];
                row[target++] = color.R;
                row[target++] = color.G;
                row[target++] = color.B;
                row[target++] = color.A;
            }
        });
    }

    private static void SavePngRows(Stream stream, int width, int height, Action<int, byte[]> fillRow) {
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

        byte[] ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WritePngChunk(stream, "IHDR", ihdr);

        using (PngIdatChunkStream idat = new PngIdatChunkStream(stream))
        using (ZLibStream zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true)) {
            byte[] rgba = new byte[width * 4];
            byte[] row = new byte[rgba.Length + 1];
            for (int y = 0; y < height; y++) {
                row[0] = 0;
                fillRow(y, rgba);
                Buffer.BlockCopy(rgba, 0, row, 1, rgba.Length);
                zlib.Write(row, 0, row.Length);
            }
        }

        WritePngChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static Color[] LoadPngPixels(string path, out int width, out int height) {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length < 33 ||
            bytes[0] != 137 ||
            bytes[1] != 80 ||
            bytes[2] != 78 ||
            bytes[3] != 71) {
            throw new InvalidDataException("unsupported PNG signature");
        }

        width = 0;
        height = 0;
        using MemoryStream idat = new MemoryStream();
        int offset = 8;
        while (offset + 8 <= bytes.Length) {
            int length = ReadBigEndian(bytes, offset);
            string type = System.Text.Encoding.ASCII.GetString(bytes, offset + 4, 4);
            offset += 8;
            if (offset + length + 4 > bytes.Length) {
                throw new InvalidDataException("truncated PNG chunk");
            }

            if (type == "IHDR") {
                width = ReadBigEndian(bytes, offset);
                height = ReadBigEndian(bytes, offset + 4);
                if (bytes[offset + 8] != 8 || bytes[offset + 9] != 6 || bytes[offset + 12] != 0) {
                    throw new InvalidDataException("unsupported PNG format");
                }
            } else if (type == "IDAT") {
                idat.Write(bytes, offset, length);
            } else if (type == "IEND") {
                break;
            }

            offset += length + 4;
        }

        if (width <= 0 || height <= 0) {
            throw new InvalidDataException("missing PNG dimensions");
        }

        Color[] pixels = new Color[width * height];
        idat.Position = 0;
        using ZLibStream zlib = new ZLibStream(idat, CompressionMode.Decompress);
        byte[] row = new byte[1 + width * 4];
        for (int y = 0; y < height; y++) {
            ReadExactly(zlib, row, 0, row.Length);
            if (row[0] != 0) {
                throw new InvalidDataException("unsupported PNG row filter " + row[0]);
            }

            int source = 1;
            int targetRow = y * width;
            for (int x = 0; x < width; x++) {
                pixels[targetRow + x] = new Color(row[source], row[source + 1], row[source + 2], row[source + 3]);
                source += 4;
            }
        }

        return pixels;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int offset, int count) {
        while (count > 0) {
            int read = stream.Read(buffer, offset, count);
            if (read <= 0) {
                throw new EndOfStreamException();
            }

            offset += read;
            count -= read;
        }
    }

    private static void WritePngChunk(Stream stream, string type, byte[] data) {
        Span<byte> header = stackalloc byte[8];
        WriteBigEndian(header, 0, data.Length);
        header[4] = (byte) type[0];
        header[5] = (byte) type[1];
        header[6] = (byte) type[2];
        header[7] = (byte) type[3];
        stream.Write(header);
        stream.Write(data, 0, data.Length);

        uint crc = Crc32Update(0xffffffffu, header.Slice(4, 4));
        crc = Crc32Update(crc, data) ^ 0xffffffffu;
        Span<byte> crcBytes = stackalloc byte[4];
        WriteBigEndian(crcBytes, 0, unchecked((int) crc));
        stream.Write(crcBytes);
    }

    private static uint Crc32Update(uint crc, ReadOnlySpan<byte> bytes) {
        for (int i = 0; i < bytes.Length; i++) {
            crc ^= bytes[i];
            for (int bit = 0; bit < 8; bit++) {
                crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320u;
            }
        }

        return crc;
    }

    private static void WriteBigEndian(Span<byte> bytes, int offset, int value) {
        bytes[offset] = (byte) ((value >> 24) & 0xff);
        bytes[offset + 1] = (byte) ((value >> 16) & 0xff);
        bytes[offset + 2] = (byte) ((value >> 8) & 0xff);
        bytes[offset + 3] = (byte) (value & 0xff);
    }

    private static int ReadBigEndian(byte[] bytes, int offset) {
        return (bytes[offset] << 24) |
               (bytes[offset + 1] << 16) |
               (bytes[offset + 2] << 8) |
               bytes[offset + 3];
    }

    private sealed class PngIdatChunkStream : Stream {
        private const int ChunkBufferSize = 1024 * 1024;
        private readonly Stream output;
        private readonly byte[] buffer = new byte[ChunkBufferSize];
        private int length;

        public PngIdatChunkStream(Stream output) {
            this.output = output;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() {
            FlushChunk();
            output.Flush();
        }

        public override int Read(byte[] target, int offset, int count) {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin) {
            throw new NotSupportedException();
        }

        public override void SetLength(long value) {
            throw new NotSupportedException();
        }

        public override void Write(byte[] source, int offset, int count) {
            while (count > 0) {
                int copied = Math.Min(count, buffer.Length - length);
                Buffer.BlockCopy(source, offset, buffer, length, copied);
                length += copied;
                offset += copied;
                count -= copied;

                if (length == buffer.Length) {
                    FlushChunk();
                }
            }
        }

        protected override void Dispose(bool disposing) {
            if (disposing) {
                FlushChunk();
            }

            base.Dispose(disposing);
        }

        private void FlushChunk() {
            if (length <= 0) {
                return;
            }

            byte[] chunk = new byte[length];
            Buffer.BlockCopy(buffer, 0, chunk, 0, length);
            WritePngChunk(output, "IDAT", chunk);
            length = 0;
        }
    }

    private static void SaveMergedTexture(string path, Action<Stream> save) {
        string tempPath = path + ".tmp";
        if (File.Exists(tempPath)) {
            File.Delete(tempPath);
        }

        try {
            using (FileStream stream = File.Create(tempPath)) {
                save(stream);
            }

            if (File.Exists(path)) {
                File.Delete(path);
            }
            File.Move(tempPath, path);
        } finally {
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }

    private static void BlendTile(Color[] canvas, int canvasWidth, int canvasHeight, Color[] tile, int tileWidth, int tileHeight, int targetX, int targetY) {
        int startX = Math.Max(0, -targetX);
        int startY = Math.Max(0, -targetY);
        int endX = Math.Min(tileWidth, canvasWidth - targetX);
        int endY = Math.Min(tileHeight, canvasHeight - targetY);
        for (int y = startY; y < endY; y++) {
            int sourceRow = y * tileWidth;
            int targetRow = (targetY + y) * canvasWidth + targetX;
            for (int x = startX; x < endX; x++) {
                Color source = tile[sourceRow + x];
                int targetIndex = targetRow + x;
                if (source.A == 255) {
                    canvas[targetIndex] = source;
                } else if (source.A > 0) {
                    canvas[targetIndex] = BlendOver(source, canvas[targetIndex]);
                }
            }
        }
    }

    private static void BlendScaledTileNearest(Color[] canvas, int canvasWidth, int canvasHeight, Color[] tile, int tileWidth, int tileHeight, int targetX, int targetY, float scale) {
        int scaledWidth = Math.Max(1, (int) (tileWidth * scale));
        int scaledHeight = Math.Max(1, (int) (tileHeight * scale));
        int startX = Math.Max(0, -targetX);
        int startY = Math.Max(0, -targetY);
        int endX = Math.Min(scaledWidth, canvasWidth - targetX);
        int endY = Math.Min(scaledHeight, canvasHeight - targetY);
        for (int y = startY; y < endY; y++) {
            int sourceY = Math.Min(tileHeight - 1, (int) (y / scale));
            int sourceRow = sourceY * tileWidth;
            int targetRow = (targetY + y) * canvasWidth + targetX;
            for (int x = startX; x < endX; x++) {
                int sourceX = Math.Min(tileWidth - 1, (int) (x / scale));
                Color source = tile[sourceRow + sourceX];
                int targetIndex = targetRow + x;
                if (source.A == 255) {
                    canvas[targetIndex] = source;
                } else if (source.A > 0) {
                    canvas[targetIndex] = BlendOver(source, canvas[targetIndex]);
                }
            }
        }
    }

    private static void BlendScaledTileNearestToRect(Color[] canvas, int canvasWidth, int canvasHeight, Color[] tile, int tileWidth, int tileHeight, int targetX, int targetY, int targetWidth, int targetHeight) {
        int scaledWidth = Math.Max(1, targetWidth);
        int scaledHeight = Math.Max(1, targetHeight);
        int startX = Math.Max(0, -targetX);
        int startY = Math.Max(0, -targetY);
        int endX = Math.Min(scaledWidth, canvasWidth - targetX);
        int endY = Math.Min(scaledHeight, canvasHeight - targetY);
        for (int y = startY; y < endY; y++) {
            int sourceY = Math.Min(tileHeight - 1, (int) (y * tileHeight / (float) scaledHeight));
            int sourceRow = sourceY * tileWidth;
            int targetRow = (targetY + y) * canvasWidth + targetX;
            for (int x = startX; x < endX; x++) {
                int sourceX = Math.Min(tileWidth - 1, (int) (x * tileWidth / (float) scaledWidth));
                Color source = tile[sourceRow + sourceX];
                int targetIndex = targetRow + x;
                if (source.A == 255) {
                    canvas[targetIndex] = source;
                } else if (source.A > 0) {
                    canvas[targetIndex] = BlendOver(source, canvas[targetIndex]);
                }
            }
        }
    }

    private static Color BlendOver(Color source, Color destination) {
        float sourceAlpha = source.A / 255f;
        float destinationAlpha = destination.A / 255f;
        float outputAlpha = sourceAlpha + destinationAlpha * (1f - sourceAlpha);
        if (outputAlpha <= 0f) {
            return Color.Transparent;
        }

        return new Color(
            (int) ((source.R * sourceAlpha + destination.R * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
            (int) ((source.G * sourceAlpha + destination.G * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
            (int) ((source.B * sourceAlpha + destination.B * destinationAlpha * (1f - sourceAlpha)) / outputAlpha),
            (int) (outputAlpha * 255f));
    }

    private static string ImageExtension() {
        return AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat) == AkronScreenshotImageFormat.Jpeg
            ? ".jpg"
            : ".png";
    }

    internal static void Load() {
        On.Celeste.PlayerCollider.Check += PlayerColliderOnCheck;
        On.Celeste.Puffer.Update += PufferOnUpdate;
        On.Celeste.BadelineOldsite.Update += BadelineOldsiteOnUpdate;
        On.Celeste.BadelineBoost.Update += BadelineBoostOnUpdate;
        On.Celeste.FlingBird.Update += FlingBirdOnUpdate;
        On.Celeste.RisingLava.Update += RisingLavaOnUpdate;
        On.Celeste.SandwichLava.Update += SandwichLavaOnUpdate;
        On.Celeste.AscendManager.Update += AscendManagerOnUpdate;
        On.Celeste.EventTrigger.OnEnter += EventTriggerOnEnter;
        On.Celeste.Level.Reload += LevelOnReload;
        Everest.Events.Level.OnExit += LevelOnExit;
    }

    internal static void Unload() {
        On.Celeste.PlayerCollider.Check -= PlayerColliderOnCheck;
        On.Celeste.Puffer.Update -= PufferOnUpdate;
        On.Celeste.BadelineOldsite.Update -= BadelineOldsiteOnUpdate;
        On.Celeste.BadelineBoost.Update -= BadelineBoostOnUpdate;
        On.Celeste.FlingBird.Update -= FlingBirdOnUpdate;
        On.Celeste.RisingLava.Update -= RisingLavaOnUpdate;
        On.Celeste.SandwichLava.Update -= SandwichLavaOnUpdate;
        On.Celeste.AscendManager.Update -= AscendManagerOnUpdate;
        On.Celeste.EventTrigger.OnEnter -= EventTriggerOnEnter;
        On.Celeste.Level.Reload -= LevelOnReload;
        Everest.Events.Level.OnExit -= LevelOnExit;
    }

    private static bool PlayerColliderOnCheck(On.Celeste.PlayerCollider.orig_Check orig, PlayerCollider self, Player player) {
        return !isScanning && orig(self, player);
    }

    private static void PufferOnUpdate(On.Celeste.Puffer.orig_Update orig, Puffer self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void BadelineOldsiteOnUpdate(On.Celeste.BadelineOldsite.orig_Update orig, BadelineOldsite self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void BadelineBoostOnUpdate(On.Celeste.BadelineBoost.orig_Update orig, BadelineBoost self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void FlingBirdOnUpdate(On.Celeste.FlingBird.orig_Update orig, FlingBird self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void RisingLavaOnUpdate(On.Celeste.RisingLava.orig_Update orig, RisingLava self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void SandwichLavaOnUpdate(On.Celeste.SandwichLava.orig_Update orig, SandwichLava self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void AscendManagerOnUpdate(On.Celeste.AscendManager.orig_Update orig, AscendManager self) {
        if (!isScanning) {
            orig(self);
        }
    }

    private static void EventTriggerOnEnter(On.Celeste.EventTrigger.orig_OnEnter orig, EventTrigger self, Player player) {
        if (!isScanning || allowScanRoomSetupTriggers) {
            orig(self, player);
        }
    }

    private static void LevelOnReload(On.Celeste.Level.orig_Reload orig, Level self) {
        if (!isScanning) {
            CancelForSceneChange();
        }
        orig(self);
    }

    private static void LevelOnExit(Level level, LevelExit levelExit, LevelExit.Mode mode, Session session, HiresSnow hiresSnow) {
        if (mode != LevelExit.Mode.Restart && !isScanning) {
            CancelForSceneChange();
        }
    }

    private static void CancelForSceneChange() {
        if (isScanning) {
            AkronLog.Warn(nameof(AkronScreenshotScanner), "Cancelling screenshot scan because the active level scene changed outside scanner room setup.");
        }
        scanCancelled = true;
        isScanning = false;
        allowScanRoomSetupTriggers = false;
        scannerHost = null;
    }
}

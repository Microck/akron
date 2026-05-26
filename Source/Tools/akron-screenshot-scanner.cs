using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Celeste;
using Celeste.Mod.Entities;
using Microsoft.Xna.Framework;
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

public static class AkronScreenshotScanner {
    private const int TileSize = 8;
    private static Entity scannerHost;
    private static bool isScanning;
    private static bool scanCancelled;
    private static string lastExportPath = string.Empty;

    public static bool IsScanning => isScanning;
    public static string LastExportPath => lastExportPath;

    public static void ScanRoom(Level level) {
        if (!TryStart(level, out Player player)) {
            return;
        }

        scannerHost.Add(new Coroutine(ScanRooms(player, new Queue<string>(new[] { level.Session.Level }))));
    }

    public static void ScanChapter(Level level) {
        if (!TryStart(level, out Player player)) {
            return;
        }

        Queue<string> rooms = new Queue<string>();
        foreach (LevelData room in level.Session.MapData.Levels) {
            rooms.Enqueue(room.Name);
        }

        scannerHost.Add(new Coroutine(ScanRooms(player, rooms)));
    }

    public static void Stop() {
        if (!isScanning) {
            return;
        }

        scanCancelled = true;
        isScanning = false;
        Engine.Scene?.Add(new AkronToast("Screenshot scan stopped."));
    }

    public static string Describe() {
        return isScanning ? "Scanning" : string.IsNullOrWhiteSpace(lastExportPath) ? "Ready" : Path.GetFileName(lastExportPath);
    }

    internal static void MaintainActiveScanHost(Level level) {
        if (!isScanning || level == null || scannerHost == null || scannerHost.Scene == level) {
            return;
        }

        // Room transitions can detach the persistent scanner host before its
        // coroutine has finished the map queue. Reattaching it on the next
        // active Level frame keeps Map Capture from reporting "Scanning"
        // forever after the helper stops receiving updates.
        if (scannerHost.Scene == null) {
            level.Add(scannerHost);
        }
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
        if (scannerHost?.Scene == null) {
            scannerHost = new Entity { Tag = Tags.Persistent };
            level.Add(scannerHost);
        }

        return true;
    }

    private static IEnumerator ScanRooms(Player initialPlayer, Queue<string> rooms) {
        string initialRoom = initialPlayer.level.Session.Level;
        Vector2 initialPosition = initialPlayer.Position;
        Vector2 initialSpeed = initialPlayer.Speed;
        bool initialVisible = initialPlayer.Visible;
        bool initialCollidable = initialPlayer.Collidable;
        int initialState = initialPlayer.StateMachine.State;
        while (rooms.Count > 0 && isScanning) {
            Level level = Engine.Scene as Level;
            Player player = level?.Tracker.GetEntity<Player>();
            if (level == null || player == null) {
                yield return null;
                continue;
            }

            string room = rooms.Dequeue();
            if (!string.Equals(level.Session.Level, room, StringComparison.Ordinal)) {
                yield return ChangeRoom(level, player, room);
                level = Engine.Scene as Level;
                player = level?.Tracker.GetEntity<Player>();
                if (level == null || player == null || !string.Equals(level.Session.Level, room, StringComparison.Ordinal)) {
                    continue;
                }
            }

            yield return ScanCurrentRoom(level, player);
        }

        Level current = Engine.Scene as Level;
        Player currentPlayer = current?.Tracker.GetEntity<Player>();
        if (current != null && currentPlayer != null && !string.Equals(current.Session.Level, initialRoom, StringComparison.Ordinal)) {
            yield return ChangeRoom(current, currentPlayer, initialRoom);
            current = Engine.Scene as Level;
            currentPlayer = current?.Tracker.GetEntity<Player>();
        }

        if (currentPlayer != null) {
            currentPlayer.Position = initialPosition;
            currentPlayer.Speed = initialSpeed;
            currentPlayer.Visible = initialVisible;
            currentPlayer.Collidable = initialCollidable;
            if (currentPlayer.Scene != null) {
                currentPlayer.StateMachine.State = initialState;
            }
        }

        bool cancelled = scanCancelled;
        isScanning = false;
        scanCancelled = false;
        scannerHost = null;
        if (!cancelled) {
            Engine.Scene?.Add(new AkronToast("Screenshot scan finished."));
        }
    }

    private static IEnumerator ChangeRoom(Level level, Player player, string roomName) {
        LevelData nextRoom = level.Session.MapData.Get(roomName);
        if (nextRoom == null || nextRoom.Spawns == null || nextRoom.Spawns.Count == 0) {
            yield break;
        }

        float previousTime = level.TimeActive;
        float previousRawTime = level.RawTimeActive;
        bool suppressMadeline = AkronModule.Settings.ScreenshotScannerNoclipHideMadeline;
        level.Session.Level = roomName;
        level.Session.FirstLevel = false;
        level.Session.StartedFromBeginning = false;
        Vector2 spawn = nextRoom.Spawns[0];
        level.OnEndOfFrame += () => {
            player.Position = spawn;
            player.Speed = Vector2.Zero;
            level.TransitionTo(nextRoom, Vector2.Zero);
        };

        for (int i = 0; i < 45; i++) {
            if (AkronModule.Settings.ScreenshotScannerFreezeTime) {
                level.TimeActive = previousTime;
                level.RawTimeActive = previousRawTime;
            }
            player.Position = spawn;
            player.Speed = Vector2.Zero;
            if (suppressMadeline) {
                player.Visible = false;
                player.Collidable = false;
                player.StateMachine.State = Player.StDummy;
            }
            yield return null;
        }
    }

    private static IEnumerator ScanCurrentRoom(Level level, Player player) {
        Rectangle bounds = level.Bounds;
        float cameraWidth = level.Camera.Right - level.Camera.Left;
        float cameraHeight = level.Camera.Bottom - level.Camera.Top;
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

        try {
            // Keep capture suppression local. Reusing global Hide Player/Noclip
            // would leave persistent user-facing state behind after export.
            if (suppressMadeline) {
                player.Visible = false;
                player.Collidable = false;
                player.Collider = new Hitbox(0f, 0f);
                player.StateMachine.State = Player.StDummy;
            }
            level.CameraLockMode = Level.CameraLockModes.None;
            if (AkronModule.Settings.ScreenshotScannerRemoveBackground) {
                level.Background = new BackdropRenderer();
            }
            if (AkronModule.Settings.ScreenshotScannerRemoveForeground) {
                level.Foreground = new BackdropRenderer();
            }

            WriteMetadata(level, bounds, cameraWidth, cameraHeight);
            foreach (AkronScreenshotScanTile tile in BuildScanTiles(bounds, cameraWidth, cameraHeight, stepX, stepY)) {
                if (!isScanning) {
                    break;
                }

                Vector2 camera = new Vector2(tile.CameraX, tile.CameraY);
                level.Camera.Position = camera;
                player.Position = camera + new Vector2(cameraWidth / 2f, cameraHeight / 2f);
                player.Speed = Vector2.Zero;
                if (suppressMadeline) {
                    player.Visible = false;
                    player.Collidable = false;
                    player.Collider = new Hitbox(0f, 0f);
                    player.StateMachine.State = Player.StDummy;
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
                        player.Visible = false;
                        player.Collidable = false;
                        player.Collider = new Hitbox(0f, 0f);
                        player.StateMachine.State = Player.StDummy;
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
                AkronCapture.CaptureToPath(level, lastExportPath, AkronModule.Settings.ScreenshotScannerExportMarkers);
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
            level.TimeActive = previousTime;
            level.RawTimeActive = previousRawTime;
        }
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

    private static void WriteMetadata(Level level, Rectangle bounds, float cameraWidth, float cameraHeight) {
        string path = BuildPath(level, "room.json");
        File.WriteAllText(path, BuildRoomMetadataJson(bounds, cameraWidth, cameraHeight, Engine.Viewport.Width, Engine.Viewport.Height));
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
        if (!isScanning) {
            orig(self, player);
        }
    }

    private static void LevelOnReload(On.Celeste.Level.orig_Reload orig, Level self) {
        CancelForSceneChange();
        orig(self);
    }

    private static void LevelOnExit(Level level, LevelExit levelExit, LevelExit.Mode mode, Session session, HiresSnow hiresSnow) {
        if (mode != LevelExit.Mode.Restart) {
            CancelForSceneChange();
        }
    }

    private static void CancelForSceneChange() {
        scanCancelled = true;
        isScanning = false;
        scannerHost = null;
    }
}

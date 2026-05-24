using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronScreenshotScanner {
    private const int TileSize = 8;
    private static Entity scannerHost;
    private static bool isScanning;
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

        isScanning = false;
        scannerHost = null;
        Engine.Scene?.Add(new AkronToast("Screenshot scan finished."));
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
        int previousPlayerState = player.StateMachine.State;
        BackdropRenderer previousBackground = level.Background;
        BackdropRenderer previousForeground = level.Foreground;
        bool suppressMadeline = AkronModule.Settings.ScreenshotScannerNoclipHideMadeline;
        bool freezeTime = AkronModule.Settings.ScreenshotScannerFreezeTime;

        try {
            // Keep capture suppression local. Reusing global Hide Player/Noclip
            // would leave persistent user-facing state behind after export.
            if (suppressMadeline) {
                player.Visible = false;
                player.Collidable = false;
                player.StateMachine.State = Player.StDummy;
            }
            if (AkronModule.Settings.ScreenshotScannerRemoveBackground) {
                level.Background = new BackdropRenderer();
            }
            if (AkronModule.Settings.ScreenshotScannerRemoveForeground) {
                level.Foreground = new BackdropRenderer();
            }

            WriteMetadata(level, bounds);
            int index = 0;
            for (float y = 0; y <= Math.Max(0, bounds.Height - cameraHeight) && isScanning; y += stepY) {
                float cameraY = bounds.Top + Math.Min(y, Math.Max(0, bounds.Height - cameraHeight));
                for (float x = 0; x <= Math.Max(0, bounds.Width - cameraWidth) && isScanning; x += stepX) {
                    float cameraX = bounds.Left + Math.Min(x, Math.Max(0, bounds.Width - cameraWidth));
                    Vector2 camera = new Vector2(cameraX, cameraY);
                    level.Camera.Position = camera;
                    player.Position = camera + new Vector2(cameraWidth / 2f, cameraHeight / 2f);
                    player.Speed = Vector2.Zero;
                    if (suppressMadeline) {
                        player.Visible = false;
                        player.Collidable = false;
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
                            player.StateMachine.State = Player.StDummy;
                        }
                    }

                    string fileName = index.ToString("0000", System.Globalization.CultureInfo.InvariantCulture) +
                                      ",dx=" + ((int) (cameraX - bounds.Left)).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                      ",dy=" + ((int) (cameraY - bounds.Top)).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                      ImageExtension();
                    lastExportPath = BuildPath(level, fileName);
                    AkronCapture.CaptureToPath(level, lastExportPath, AkronModule.Settings.ScreenshotScannerExportMarkers);
                    index++;
                }
            }
        } finally {
            player.Position = previousPlayerPosition;
            player.Speed = previousPlayerSpeed;
            player.Visible = previousPlayerVisible;
            player.Collidable = previousPlayerCollidable;
            if (player.Scene != null && player.StateMachine.State == Player.StDummy) {
                player.StateMachine.State = previousPlayerState;
            }
            level.Background = previousBackground;
            level.Foreground = previousForeground;
            level.TimeActive = previousTime;
            level.RawTimeActive = previousRawTime;
        }
    }

    private static string BuildPath(Level level, string fileName) {
        string root = AkronModuleSettings.NormalizeScreenshotScannerExportPath(AkronModule.Settings.ScreenshotScannerExportPath);
        string sid = level.Session.Area.GetSID();
        char side = (char) ('A' + (int) level.Session.Area.Mode);
        string path = Path.Combine(Everest.PathGame, root, sid, side.ToString(), level.Session.Level, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        return path;
    }

    private static void WriteMetadata(Level level, Rectangle bounds) {
        string path = BuildPath(level, "metadata.json");
        var metadata = new {
            roomPosition = new { x = bounds.X, y = bounds.Y },
            roomSize = new { width = bounds.Width, height = bounds.Height },
            cameraSize = new { width = Engine.ViewWidth, height = Engine.ViewHeight },
            viewPort = new { width = Engine.Viewport.Width, height = Engine.Viewport.Height }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
        lastExportPath = path;
    }

    private static string ImageExtension() {
        return AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat) == AkronScreenshotImageFormat.Jpeg
            ? ".jpg"
            : ".png";
    }
}

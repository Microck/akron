using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public enum GridEdge {
    Top,
    Bottom,
    Left,
    Right
}

public static partial class AkronEntityInspector {
    private const float CameraCullMargin = 32f;
    private const float HitboxThicknessUnitsPerGamePixel = 5f;
    private const float DefaultPlayerHurtboxWidth = 8f;
    private const float DefaultPlayerHurtboxHeight = 9f;
    private const float DefaultPlayerHurtboxX = -4f;
    private const float DefaultPlayerHurtboxY = -11f;
    private static readonly List<Rectangle> playerTrail = new List<Rectangle>();
    private static readonly Rectangle onePixelProbe = new Rectangle(0, 0, 1, 1);
    private static string playerTrailRoom = string.Empty;
    private static ulong lastPlayerTrailFrame;
    private static double lastRenderMilliseconds;
    private static int lastDrawCalls;
    private static int lastGridCellChecks;
    private static int lastGridRuns;
    private static int lastTrailSamples;
    private static int frameDrawCalls;
    private static int frameGridCellChecks;
    private static int frameGridRuns;
    private static bool renderingToGameplayBuffer;

    public static Entity GetFocusedEntity(Level level) {
        Player player = level.Tracker.GetEntity<Player>();
        if (player == null) {
            return null;
        }

        Entity closestEntity = null;
        float closestDistance = float.MaxValue;

        foreach (Entity entity in level.Entities) {
            if (entity == player || entity.Collider == null || entity is AkronOverlay || entity is AkronToast) {
                continue;
            }

            float distance = Vector2.DistanceSquared(entity.Center, player.Center);
            if (distance < closestDistance) {
                closestDistance = distance;
                closestEntity = entity;
            }
        }

        return closestEntity;
    }

    public static string Describe(Level level) {
        Entity entity = GetFocusedEntity(level);
        if (entity == null) {
            return "No nearby entity";
        }

        string collider = entity.Collider == null
            ? "none"
            : entity.Collider.Width.ToString("0") + "x" + entity.Collider.Height.ToString("0");

        return entity.GetType().Name + " @ " + entity.Position.X.ToString("0") + ", " + entity.Position.Y.ToString("0") + " [" + collider + "]";
    }

    public static void RenderHitboxes(Level level, Player player) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (!AkronModule.TryUse(AkronFeatureKind.HitboxViewer)) {
            return;
        }

        long startTimestamp = Stopwatch.GetTimestamp();
        frameDrawCalls = 0;
        frameGridCellChecks = 0;
        frameGridRuns = 0;
        player ??= FindPlayer(level);

        try {
            bool deathHitboxVisible = settings.HitboxShowLastDeath && HasVisibleLastDeathObjectHitbox(AkronModule.Session);
            if (ShouldRenderLiveHitboxes(settings.HitboxViewer, deathHitboxVisible, settings.HitboxShowAllOnDeath)) {
                if (settings.HitboxShowSolids) {
                    DrawVisibleSolidTiles(level);
                }

                if (player != null && !settings.HitboxHidePlayer) {
                    CapturePlayerTrail(level, player, settings);
                    DrawPlayerTrail(level, settings);
                    DrawPlayerHitbox(level, player);
                } else {
                    playerTrail.Clear();
                    lastPlayerTrailFrame = 0;
                    lastTrailSamples = 0;
                }

                Rectangle cameraBounds = CameraWorldBounds(level);
                foreach (Entity entity in level.Entities) {
                    if (entity.Collider == null || entity is AkronOverlay || entity is AkronToast) {
                        continue;
                    }
                    if (entity is Player || entity is SolidTiles) {
                        continue;
                    }
                    if (settings.HitboxActiveOnly && !entity.Active) {
                        continue;
                    }
                    if (!ShouldRenderHitboxEntity(entity, settings)) {
                        continue;
                    }
                    if (!IntersectsCamera(entity.Collider, cameraBounds)) {
                        continue;
                    }

                    DrawCollider(level, entity.Collider, HitboxColor(settings, entity), cameraBounds);
                }
            }

            if (settings.HitboxShowLastDeath && HasVisibleLastDeathHitbox()) {
                if (!settings.HitboxShowAllOnDeath && AkronModule.Session.LastDeathHitbox is Rectangle deathHitbox) {
                    DrawWorldRect(level, deathHitbox, ColorFromRgb(settings.HitboxDeathColor));
                }

                if (settings.HitboxShowDeathPlayerMarker) {
                    DrawLastDeathPlayerMarker(level, settings);
                }
            }
        } finally {
            lastRenderMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
            lastDrawCalls = frameDrawCalls;
            lastGridCellChecks = frameGridCellChecks;
            lastGridRuns = frameGridRuns;
        }
    }

    public static void RenderHitboxesToGameplayBuffer(Level level, Player player) {
        renderingToGameplayBuffer = true;
        try {
            RenderHitboxes(level, player);
        } finally {
            renderingToGameplayBuffer = false;
        }
    }

    public static string DescribeHitboxPerformance() {
        return "hitbox-render-ms: " + lastRenderMilliseconds.ToString("0.000", CultureInfo.InvariantCulture) +
               "; hitbox-draw-calls: " + lastDrawCalls.ToString(CultureInfo.InvariantCulture) +
               "; hitbox-grid-cell-checks: " + lastGridCellChecks.ToString(CultureInfo.InvariantCulture) +
               "; hitbox-grid-runs: " + lastGridRuns.ToString(CultureInfo.InvariantCulture) +
               "; hitbox-trail-samples: " + lastTrailSamples.ToString(CultureInfo.InvariantCulture);
    }

    public static void SyncHitboxes() {
        playerTrail.Clear();
        playerTrailRoom = string.Empty;
        lastPlayerTrailFrame = 0;
        lastRenderMilliseconds = 0;
        lastDrawCalls = 0;
        lastGridCellChecks = 0;
        lastGridRuns = 0;
        lastTrailSamples = 0;
        frameDrawCalls = 0;
        frameGridCellChecks = 0;
        frameGridRuns = 0;
    }

    public static void RecordLastDeath(Level level, Vector2 deathPosition) {
        RecordLastDeath(level, deathPosition, null, string.Empty);
    }

    public static void RecordLastDeath(Level level, Vector2 deathPosition, Rectangle? explicitHitbox, string explicitEntityType) {
        AkronModule.Session.LastDeathPosition = deathPosition;
        Player player = FindPlayer(level);
        AkronModule.Session.LastDeathPlayerBounds = player?.Collider?.Bounds;
        if (explicitHitbox.HasValue) {
            AkronModule.Session.LastDeathEntityType = explicitEntityType ?? string.Empty;
            AkronModule.Session.LastDeathHitbox = explicitHitbox.Value;
        } else {
            Entity nearbyHazard = level.Entities
                .Where(entity => entity.Collider != null && IsHazard(entity))
                .OrderBy(entity => Vector2.DistanceSquared(entity.Center, deathPosition))
                .FirstOrDefault();
            AkronModule.Session.LastDeathEntityType = nearbyHazard?.GetType().Name ?? string.Empty;
            AkronModule.Session.LastDeathHitbox = nearbyHazard?.Collider?.Bounds ??
                                                   new Rectangle((int) deathPosition.X - 4, (int) deathPosition.Y - 4, 8, 8);
        }
        AkronModule.Session.LastDeathHitboxVisible = true;
        AkronModule.Session.LastDeathHitboxSawDeathState = false;
        AkronModule.Session.LastDeathHitboxRecordedFrame = Engine.FrameCounter;
    }

    private static void DrawLastDeathPlayerMarker(Level level, AkronModuleSettings settings) {
        if (AkronModule.Session.LastDeathPlayerBounds is Rectangle playerBounds) {
            DrawWorldRect(level, playerBounds, ColorFromRgb(settings.HitboxDeathPlayerColor));
            return;
        }

        if (AkronModule.Session.LastDeathPosition is Vector2 deathPosition) {
            DrawWorldRect(
                level,
                new Rectangle((int) deathPosition.X - 3, (int) deathPosition.Y - 3, 6, 6),
                ColorFromRgb(settings.HitboxDeathPlayerColor));
        }
    }

    public static bool HasVisibleLastDeathHitbox() {
        AkronModuleSession session = AkronModule.Session;
        return session != null &&
               session.LastDeathHitboxVisible &&
               (session.LastDeathHitbox.HasValue ||
                session.LastDeathPlayerBounds.HasValue ||
                session.LastDeathPosition.HasValue);
    }

    internal static bool HasVisibleLastDeathObjectHitbox(AkronModuleSession session) {
        return session?.LastDeathHitboxVisible == true && session.LastDeathHitbox.HasValue;
    }

    internal static bool ShouldRenderLiveHitboxes(bool liveViewer, bool deathHitboxVisible, bool showAllOnDeath) {
        return deathHitboxVisible ? showAllOnDeath : liveViewer;
    }

    public static void ClearLastDeathHitbox() {
        AkronModuleSession session = AkronModule.Session;
        if (session == null) {
            return;
        }

        session.LastDeathPosition = null;
        session.LastDeathPlayerBounds = null;
        session.LastDeathHitbox = null;
        session.LastDeathHitboxVisible = false;
        session.LastDeathHitboxSawDeathState = false;
        session.LastDeathHitboxRecordedFrame = 0;
        session.LastDeathEntityType = string.Empty;
    }

    private static bool ShouldRenderHitboxEntity(Entity entity, AkronModuleSettings settings) {
        if (entity is Player) {
            return true;
        }

        if (entity is Trigger) {
            return settings.HitboxShowTriggers;
        }

        if (IsHazard(entity)) {
            return settings.HitboxShowHazards;
        }

        if (entity is Solid) {
            return settings.HitboxShowSolids;
        }

        // Unknown collidable helper entities can sit far away from their visual
        // owner, which creates floating boxes that look like broken player
        // hurtboxes. Keep the live overlay scoped to gameplay collision classes
        // Akron can classify confidently.
        return false;
    }

    private static Rectangle ColliderWorldBounds(Collider collider) {
        if (collider is Circle circle) {
            return new Rectangle(
                (int) System.Math.Floor(circle.AbsoluteX - circle.Radius),
                (int) System.Math.Floor(circle.AbsoluteY - circle.Radius),
                (int) System.Math.Ceiling(circle.Radius * 2f),
                (int) System.Math.Ceiling(circle.Radius * 2f));
        }

        return new Rectangle(
            (int) System.Math.Floor(collider.AbsoluteX),
            (int) System.Math.Floor(collider.AbsoluteY),
            (int) System.Math.Ceiling(collider.Width),
            (int) System.Math.Ceiling(collider.Height));
    }

    private static Player FindPlayer(Level level) {
        Player trackedPlayer = level.Tracker.GetEntities<Player>().OfType<Player>().FirstOrDefault();
        if (trackedPlayer != null) {
            return trackedPlayer;
        }

        foreach (Entity entity in level.Entities) {
            if (entity is Player player) {
                return player;
            }
        }

        return null;
    }

    private static void DrawPlayerHitbox(Level level, Player player) {
        AkronModuleSettings settings = AkronModule.Settings;
        Color solidCollisionColor = ColorFromRgb(settings.HitboxPlayerColor);
        if (player.Collider != null) {
            DrawCollider(level, player.Collider, solidCollisionColor, CameraWorldBounds(level));
        } else {
            // Madeline's normal gameplay collider is an 8x11 rectangle anchored to
            // the bottom-center player position. Use this only as a true fallback:
            // drawing it on top of an active collider makes fixed hitboxes look
            // like an extra regular hitbox was appended.
            Rectangle normalHurtbox = new Rectangle(
                (int) System.Math.Floor(player.Position.X - 4f),
                (int) System.Math.Floor(player.Position.Y - 11f),
                8,
                11);
            DrawWorldRect(level, normalHurtbox, solidCollisionColor);
        }

        if (settings.HitboxShowPlayerHurtbox) {
            DrawWorldRect(level, PlayerHazardHitboxBounds(player), ColorFromRgb(settings.HitboxPlayerHurtboxColor));
        }
    }

    private static Rectangle PlayerHazardHitboxBounds(Player player) {
        Hitbox hurtbox = player.GetFieldValue<Hitbox>("hurtbox");
        if (hurtbox == null) {
            return PlayerDefaultHazardHitboxBounds(player.Position.X, player.Position.Y);
        }

        return PlayerHazardHitboxBounds(
            player.Position.X,
            player.Position.Y,
            hurtbox.Position.X,
            hurtbox.Position.Y,
            hurtbox.Width,
            hurtbox.Height);
    }

    internal static Rectangle PlayerHazardHitboxBounds(float playerX, float playerY, float localX, float localY, float width, float height) {
        // CelesteTAS draws Player.hurtbox directly. Feed this helper the live
        // hurtbox offset/size so ducking and star-fly states keep their distinct
        // hazard shape. The fallback call site uses Celeste's normal hurtbox:
        // new Hitbox(8f, 9f, -4f, -11f).
        (int left, int top, int pixelWidth, int pixelHeight) = PixelExactBoundsParts(playerX + localX, playerY + localY, width, height);
        return new Rectangle(left, top, pixelWidth, pixelHeight);
    }

    internal static Rectangle PlayerDefaultHazardHitboxBounds(float playerX, float playerY) {
        return PlayerHazardHitboxBounds(
            playerX,
            playerY,
            DefaultPlayerHurtboxX,
            DefaultPlayerHurtboxY,
            DefaultPlayerHurtboxWidth,
            DefaultPlayerHurtboxHeight);
    }

    internal static (int Left, int Top, int Width, int Height) PixelExactBoundsParts(float x, float y, float width, float height) {
        int left = (int) System.Math.Floor(x);
        int top = (int) System.Math.Floor(y);

        return (
            left,
            top,
            (int) System.Math.Ceiling(width + x - left),
            (int) System.Math.Ceiling(height + y - top));
    }

    private static void CapturePlayerTrail(Level level, Player player, AkronModuleSettings settings) {
        int trailLength = settings.ShowHitboxTrail ? AkronModuleSettings.ClampHitboxTrailLength(settings.HitboxTrailLength) : 0;
        if (trailLength <= 0) {
            playerTrail.Clear();
            lastPlayerTrailFrame = 0;
            lastTrailSamples = 0;
            return;
        }

        string room = level.Session?.Level ?? string.Empty;
        if (!string.Equals(playerTrailRoom, room, System.StringComparison.Ordinal)) {
            playerTrail.Clear();
            lastPlayerTrailFrame = 0;
            playerTrailRoom = room;
        }

        if (lastPlayerTrailFrame == Engine.FrameCounter) {
            return;
        }

        Rectangle hurtbox = PlayerTrailHitbox(player);
        playerTrail.Add(hurtbox);
        lastPlayerTrailFrame = Engine.FrameCounter;

        while (playerTrail.Count > trailLength) {
            playerTrail.RemoveAt(0);
        }

        lastTrailSamples = playerTrail.Count;
    }

    private static Rectangle PlayerTrailHitbox(Player player) {
        if (player.Collider != null) {
            return ColliderWorldBounds(player.Collider);
        }

        return new Rectangle(
            (int) System.Math.Floor(player.Position.X - 4f),
            (int) System.Math.Floor(player.Position.Y - 11f),
            8,
            11);
    }

    private static void DrawPlayerTrail(Level level, AkronModuleSettings settings) {
        if (!settings.ShowHitboxTrail || playerTrail.Count <= 1) {
            return;
        }

        Color playerColor = ColorFromRgb(settings.HitboxPlayerColor);
        Rectangle cameraBounds = CameraWorldBounds(level);
        float maxOpacity = AkronModuleSettings.ClampOpacity(settings.HitboxTrailOpacity) / 100f;
        int lastIndex = playerTrail.Count - 1;
        for (int index = 0; index < lastIndex; index++) {
            if (!playerTrail[index].Intersects(cameraBounds)) {
                continue;
            }

            float fade = (index + 1f) / playerTrail.Count;
            DrawWorldRect(level, playerTrail[index], playerColor * maxOpacity * (0.15f + 0.85f * fade));
        }
    }

    private static Color HitboxColor(AkronModuleSettings settings, Entity entity) {
        if (entity is Player) {
            return ColorFromRgb(settings.HitboxPlayerColor);
        }

        if (entity is Trigger) {
            return ColorFromRgb(settings.HitboxTriggerColor);
        }

        if (IsHazard(entity)) {
            return ColorFromRgb(settings.HitboxHazardColor);
        }

        if (entity is Solid) {
            return ColorFromRgb(settings.HitboxSolidColor);
        }

        return ColorFromRgb(settings.HitboxOtherColor);
    }

    private static void DrawVisibleSolidTiles(Level level) {
        if (level.SolidTiles?.Grid == null) {
            return;
        }

        DrawGridCollider(level, level.SolidTiles.Grid, ColorFromRgb(AkronModule.Settings.HitboxSolidColor), CameraWorldBounds(level));
    }

    private static void DrawCollider(Level level, Collider collider, Color color, Rectangle cameraBounds) {
        if (AkronModule.Settings.FixHitboxPixels) {
            DrawExactColliderPixels(level, collider, color, cameraBounds);
            return;
        }

        switch (collider) {
            case ColliderList colliderList:
                foreach (Collider child in colliderList.colliders) {
                    if (child != null && IntersectsCamera(child, cameraBounds)) {
                        DrawCollider(level, child, color, cameraBounds);
                    }
                }
                break;
            case Grid grid:
                DrawGridCollider(level, grid, color, cameraBounds);
                break;
            case Circle circle:
                DrawPixelCircle(level, circle, color, cameraBounds);
                break;
            default:
                DrawWorldRect(level, ColliderWorldBounds(collider), color);
                break;
        }
    }

    private static void DrawExactColliderPixels(Level level, Collider collider, Color color, Rectangle cameraBounds) {
        switch (collider) {
            case ColliderList colliderList:
                foreach (Collider child in colliderList.colliders) {
                    if (child != null && IntersectsCamera(child, cameraBounds)) {
                        DrawExactColliderPixels(level, child, color, cameraBounds);
                    }
                }
                return;
            case Grid grid:
                DrawGridCollider(level, grid, color, cameraBounds);
                return;
            case Hitbox:
                DrawWorldRect(level, ColliderWorldBounds(collider), color);
                return;
            case Circle circle:
                DrawExactCircle(level, circle, color, cameraBounds);
                return;
        }

        Rectangle sampleBounds = ExactSampleBounds(collider);
        sampleBounds = Rectangle.Intersect(sampleBounds, cameraBounds);
        if (sampleBounds.Width <= 0 || sampleBounds.Height <= 0) {
            return;
        }

        float fillAlpha = AkronModuleSettings.ClampHitboxFillOpacity(AkronModule.Settings.HitboxFillOpacity) / 100f;
        float thickness = AkronModuleSettings.ClampHitboxLineThickness(AkronModule.Settings.HitboxLineThickness);
        if (fillAlpha > 0f) {
            foreach (Rectangle run in EnumerateExactColliderPixelRuns(collider, true, sampleBounds)) {
                DrawWorldPixelFillRun(level, run.X, run.Y, run.Width, color * fillAlpha);
            }
        }

        if (AkronModule.Settings.HitboxBlackOutline) {
            DrawConnectedPixelOutline(level, sampleBounds, (x, y) => CollidesPixel(collider, x, y), Color.Black * 0.75f, thickness + 2);
        }

        DrawConnectedPixelOutline(level, sampleBounds, (x, y) => CollidesPixel(collider, x, y), color * 0.95f, thickness);
    }

    private static IEnumerable<Rectangle> EnumerateExactColliderPixelRuns(Collider collider, bool includeFill, Rectangle sampleBounds) {
        foreach (Rectangle run in ExactPixelRuns(sampleBounds, (x, y) => CollidesPixel(collider, x, y), includeFill)) {
            yield return run;
        }
    }

    private static bool CollidesPixel(Collider collider, int x, int y) {
        Rectangle probe = onePixelProbe;
        probe.X = x;
        probe.Y = y;
        return collider.Collide(probe);
    }

    private static void DrawGridCollider(Level level, Grid grid, Color color, Rectangle cameraBounds) {
        if (grid.IsEmpty || !IntersectsCamera(grid, cameraBounds)) {
            return;
        }

        int minCellX = ClampCellIndex((int) System.Math.Floor((cameraBounds.Left - grid.AbsoluteLeft) / grid.CellWidth), grid.CellsX);
        int maxCellX = ClampCellIndex((int) System.Math.Ceiling((cameraBounds.Right - grid.AbsoluteLeft) / grid.CellWidth), grid.CellsX);
        int minCellY = ClampCellIndex((int) System.Math.Floor((cameraBounds.Top - grid.AbsoluteTop) / grid.CellHeight), grid.CellsY);
        int maxCellY = ClampCellIndex((int) System.Math.Ceiling((cameraBounds.Bottom - grid.AbsoluteTop) / grid.CellHeight), grid.CellsY);
        float fillAlpha = AkronModuleSettings.ClampHitboxFillOpacity(AkronModule.Settings.HitboxFillOpacity) / 100f;
        float thickness = AkronModuleSettings.ClampHitboxLineThickness(AkronModule.Settings.HitboxLineThickness);

        for (int cellY = minCellY; cellY < maxCellY; cellY++) {
            int runStart = -1;
            for (int cellX = minCellX; cellX < maxCellX; cellX++) {
                frameGridCellChecks++;
                if (grid[cellX, cellY]) {
                    if (runStart < 0) {
                        runStart = cellX;
                    }
                    continue;
                }

                if (runStart >= 0) {
                    DrawGridFillRun(level, grid, runStart, cellX, cellY, color * fillAlpha);
                    runStart = -1;
                }
            }

            if (runStart >= 0) {
                DrawGridFillRun(level, grid, runStart, maxCellX, cellY, color * fillAlpha);
            }
        }

        if (AkronModule.Settings.HitboxBlackOutline) {
            DrawGridOutline(level, grid, minCellX, minCellY, maxCellX, maxCellY, Color.Black * 0.75f, thickness + 2);
        }

        DrawGridOutline(level, grid, minCellX, minCellY, maxCellX, maxCellY, color * 0.95f, thickness);
    }

    private static void DrawGridFillRun(Level level, Grid grid, int startCellX, int endCellX, int cellY, Color color) {
        frameGridRuns++;
        DrawWorldFillRect(
            level,
            new Rectangle(
                (int) System.Math.Floor(grid.AbsoluteLeft + startCellX * grid.CellWidth),
                (int) System.Math.Floor(grid.AbsoluteTop + cellY * grid.CellHeight),
                (int) System.Math.Ceiling((endCellX - startCellX) * grid.CellWidth),
                (int) System.Math.Ceiling(grid.CellHeight)),
            color);
    }

    private static void DrawGridOutline(Level level, Grid grid, int minCellX, int minCellY, int maxCellX, int maxCellY, Color color, float thickness) {
        foreach ((int cellX, int cellY, GridEdge edge) in GridOutlineEdges(minCellX, minCellY, maxCellX, maxCellY, (x, y) => GridCellFilled(grid, x, y))) {
            float worldX = grid.AbsoluteLeft + cellX * grid.CellWidth;
            float worldY = grid.AbsoluteTop + cellY * grid.CellHeight;
            switch (edge) {
                case GridEdge.Top:
                    DrawWorldHorizontalBoundary(level, worldX, worldY, grid.CellWidth, color, thickness);
                    break;
                case GridEdge.Bottom:
                    DrawWorldHorizontalBoundary(level, worldX, worldY + grid.CellHeight - thickness / HitboxThicknessUnitsPerGamePixel, grid.CellWidth, color, thickness);
                    break;
                case GridEdge.Left:
                    DrawWorldVerticalBoundary(level, worldX, worldY, grid.CellHeight, color, thickness);
                    break;
                case GridEdge.Right:
                    DrawWorldVerticalBoundary(level, worldX + grid.CellWidth - thickness / HitboxThicknessUnitsPerGamePixel, worldY, grid.CellHeight, color, thickness);
                    break;
            }
        }
    }

    private static Rectangle CameraWorldBounds(Level level) {
        return new Rectangle(
            (int) System.Math.Floor(level.Camera.X - CameraCullMargin),
            (int) System.Math.Floor(level.Camera.Y - CameraCullMargin),
            (int) System.Math.Ceiling(320f + CameraCullMargin * 2f),
            (int) System.Math.Ceiling(180f + CameraCullMargin * 2f));
    }

    private static bool IntersectsCamera(Collider collider, Rectangle cameraBounds) {
        return ColliderWorldBounds(collider).Intersects(cameraBounds);
    }

    private static void DrawWorldRect(Level level, Rectangle worldBounds, Color color) {
        frameDrawCalls++;
        AkronHudRect rect = WorldToHitboxSurfaceRect(level, worldBounds);
        float x = rect.X;
        float y = rect.Y;
        float width = rect.Width;
        float height = rect.Height;
        float fillAlpha = AkronModuleSettings.ClampHitboxFillOpacity(AkronModule.Settings.HitboxFillOpacity) / 100f;
        float thickness = AkronModuleSettings.ClampHitboxLineThickness(AkronModule.Settings.HitboxLineThickness);

        Draw.Rect(x, y, width, height, color * fillAlpha);
        if (AkronModule.Settings.HitboxBlackOutline) {
            DrawThickHollowRect(x, y, width, height, Color.Black * 0.75f, thickness + 2);
        }
        DrawThickHollowRect(x, y, width, height, color * 0.95f, thickness);
    }

    private static void DrawWorldFillRect(Level level, Rectangle worldBounds, Color color) {
        if (color.A <= 0 || worldBounds.Width <= 0 || worldBounds.Height <= 0) {
            return;
        }

        frameDrawCalls++;
        AkronHudRect rect = WorldToHitboxSurfaceRect(level, worldBounds);
        Draw.Rect(rect.X, rect.Y, rect.Width, rect.Height, color);
    }

    private static void DrawWorldHorizontalBoundary(Level level, float worldX, float worldY, float worldWidth, Color color, float thickness) {
        frameDrawCalls++;
        float screenThickness = HitboxThicknessToScreenPixels(thickness);
        Vector2 start = WorldToHitboxSurface(level, new Vector2(worldX, worldY));
        Vector2 end = WorldToHitboxSurface(level, new Vector2(worldX + worldWidth, worldY));
        float left = System.Math.Min(start.X, end.X);
        float width = System.Math.Abs(end.X - start.X);

        // Draw exposed grid edges inside the filled cell. Centered boundary
        // strokes double up at shared cell borders and make floor grids look
        // thicker than the actual debug hitbox.
        Draw.Rect(left, start.Y, width, screenThickness, color);
    }

    private static void DrawWorldVerticalBoundary(Level level, float worldX, float worldY, float worldHeight, Color color, float thickness) {
        frameDrawCalls++;
        float screenThickness = HitboxThicknessToScreenPixels(thickness);
        Vector2 start = WorldToHitboxSurface(level, new Vector2(worldX, worldY));
        Vector2 end = WorldToHitboxSurface(level, new Vector2(worldX, worldY + worldHeight));
        float top = System.Math.Min(start.Y, end.Y);
        float height = System.Math.Abs(end.Y - start.Y);

        Draw.Rect(start.X, top, screenThickness, height, color);
    }

    private static void DrawPixelCircle(Level level, Circle circle, Color color, Rectangle cameraBounds) {
        DrawCirclePixelRuns(level, circle, color, cameraBounds, useColliderCollision: false);
    }

    private static void DrawExactCircle(Level level, Circle circle, Color color, Rectangle cameraBounds) {
        DrawCirclePixelRuns(level, circle, color, cameraBounds, useColliderCollision: true);
    }

    private static void DrawCirclePixelRuns(Level level, Circle circle, Color color, Rectangle cameraBounds, bool useColliderCollision) {
        Rectangle bounds = ColliderWorldBounds(circle);
        bounds.Inflate(1, 1);
        bounds = Rectangle.Intersect(bounds, cameraBounds);
        if (bounds.Width <= 0 || bounds.Height <= 0) {
            return;
        }

        float fillAlpha = AkronModuleSettings.ClampHitboxFillOpacity(AkronModule.Settings.HitboxFillOpacity) / 100f;
        float thickness = AkronModuleSettings.ClampHitboxLineThickness(AkronModule.Settings.HitboxLineThickness);
        if (fillAlpha > 0f) {
            foreach ((int x, int y, int width) in CircleRunSegments(circle, useColliderCollision, includeFill: true)) {
                DrawClippedWorldPixelFillRun(level, x, y, width, bounds, color * fillAlpha);
            }
        }

        if (AkronModule.Settings.HitboxBlackOutline) {
            DrawCirclePixelOutline(level, circle, bounds, Color.Black * 0.75f, thickness + 2, useColliderCollision);
        }

        DrawCirclePixelOutline(level, circle, bounds, color * 0.95f, thickness, useColliderCollision);
    }

    private static List<(int X, int Y, int Width)> CircleRunSegments(Circle circle, bool useColliderCollision, bool includeFill) {
        if (!useColliderCollision) {
            return PixelCircleRunSegments(circle.AbsoluteX, circle.AbsoluteY, circle.Radius, includeFill);
        }

        Rectangle bounds = ExactSampleBounds(circle);
        return ExactPixelRunSegments(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            (x, y) => CollidesPixel(circle, x, y),
            includeFill);
    }

    private static void DrawClippedWorldPixelFillRun(Level level, int worldX, int worldY, int width, Rectangle clipBounds, Color color) {
        if (worldY < clipBounds.Top || worldY >= clipBounds.Bottom) {
            return;
        }

        int left = System.Math.Max(worldX, clipBounds.Left);
        int right = System.Math.Min(worldX + width, clipBounds.Right);
        if (right <= left) {
            return;
        }

        DrawWorldPixelFillRun(level, left, worldY, right - left, color);
    }

    private static void DrawCirclePixelOutline(Level level, Circle circle, Rectangle clipBounds, Color color, float thickness, bool useColliderCollision) {
        if (useColliderCollision) {
            DrawConnectedPixelOutline(level, clipBounds, (x, y) => CollidesPixel(circle, x, y), color, thickness);
            return;
        }

        float radiusSquared = circle.Radius * circle.Radius;
        DrawConnectedPixelOutline(
            level,
            clipBounds,
            (x, y) => PixelCenterInsideCircle(x, y, circle.AbsoluteX, circle.AbsoluteY, radiusSquared),
            color,
            thickness);
    }

    private static void DrawConnectedPixelOutline(Level level, Rectangle sampleBounds, System.Func<int, int, bool> collidesPixel, Color color, float thickness) {
        if (System.Math.Abs(AkronModuleSettings.ClampHitboxLineThickness(thickness) - HitboxThicknessUnitsPerGamePixel) < 0.001f) {
            foreach (Rectangle run in ExactPixelRuns(sampleBounds, collidesPixel, includeFill: false)) {
                DrawWorldPixelFillRun(level, run.X, run.Y, run.Width, color);
            }
            return;
        }

        HashSet<(int X, int Y)> samples = ExactPixelOutlineSamples(
            sampleBounds.X,
            sampleBounds.Y,
            sampleBounds.Width,
            sampleBounds.Height,
            collidesPixel);

        foreach ((int x, int y) in samples) {
            if (samples.Contains((x + 1, y))) {
                DrawWorldPixelCenterConnector(level, x, y, x + 1, y, color, thickness);
            }
            if (samples.Contains((x, y + 1))) {
                DrawWorldPixelCenterConnector(level, x, y, x, y + 1, color, thickness);
            }
            if (samples.Contains((x + 1, y + 1)) && !collidesPixel(x + 1, y) && !collidesPixel(x, y + 1)) {
                DrawWorldPixelDiagonalConnector(level, x, y, x + 1, y + 1, color, thickness);
            }
            if (samples.Contains((x + 1, y - 1)) && !collidesPixel(x + 1, y) && !collidesPixel(x, y - 1)) {
                DrawWorldPixelDiagonalConnector(level, x, y, x + 1, y - 1, color, thickness);
            }
        }

        foreach ((int x, int y) in samples) {
            DrawWorldPixelCenter(level, x, y, color, thickness);
        }
    }

    private static void DrawWorldPixelFillRun(Level level, int worldX, int worldY, int width, Color color) {
        frameDrawCalls++;
        AkronHudRect fillRect = WorldToHitboxSurfaceRect(
            level,
            new Rectangle(worldX, worldY, width, 1));
        Draw.Rect(fillRect.X, fillRect.Y, fillRect.Width, fillRect.Height, color);
    }

    private static void DrawWorldPixelCenter(Level level, int worldX, int worldY, Color color, float thickness) {
        frameDrawCalls++;
        float screenThickness = HitboxThicknessToScreenPixels(thickness);
        Vector2 center = WorldToHitboxSurface(level, new Vector2(worldX + 0.5f, worldY + 0.5f));
        Draw.Rect(center.X - screenThickness * 0.5f, center.Y - screenThickness * 0.5f, screenThickness, screenThickness, color);
    }

    private static void DrawWorldPixelCenterConnector(Level level, int startWorldX, int startWorldY, int endWorldX, int endWorldY, Color color, float thickness) {
        frameDrawCalls++;
        float screenThickness = HitboxThicknessToScreenPixels(thickness);
        Vector2 start = WorldToHitboxSurface(level, new Vector2(startWorldX + 0.5f, startWorldY + 0.5f));
        Vector2 end = WorldToHitboxSurface(level, new Vector2(endWorldX + 0.5f, endWorldY + 0.5f));
        if (startWorldY == endWorldY) {
            float left = System.Math.Min(start.X, end.X);
            float width = System.Math.Abs(end.X - start.X);
            Draw.Rect(left, start.Y - screenThickness * 0.5f, width, screenThickness, color);
            return;
        }

        float top = System.Math.Min(start.Y, end.Y);
        float height = System.Math.Abs(end.Y - start.Y);
        Draw.Rect(start.X - screenThickness * 0.5f, top, screenThickness, height, color);
    }

    private static void DrawWorldPixelDiagonalConnector(Level level, int startWorldX, int startWorldY, int endWorldX, int endWorldY, Color color, float thickness) {
        frameDrawCalls++;
        float screenThickness = HitboxThicknessToScreenPixels(thickness);
        Vector2 start = WorldToHitboxSurface(level, new Vector2(startWorldX + 0.5f, startWorldY + 0.5f));
        Vector2 end = WorldToHitboxSurface(level, new Vector2(endWorldX + 0.5f, endWorldY + 0.5f));
        Draw.Line(start, end, color, screenThickness);
    }

    private static void DrawThickHollowRect(float x, float y, float width, float height, Color color, float thickness) {
        float lineThickness = HitboxThicknessToScreenPixels(thickness);
        float left = (float) System.Math.Floor(x);
        float top = (float) System.Math.Floor(y);
        float right = (float) System.Math.Floor(x + width);
        float bottom = (float) System.Math.Floor(y + height);
        float snappedWidth = System.Math.Max(1f, right - left);
        float snappedHeight = System.Math.Max(1f, bottom - top);
        float horizontalThickness = System.Math.Min(lineThickness, snappedHeight);
        float verticalThickness = System.Math.Min(lineThickness, snappedWidth);

        // Keep rectangle/grid hitboxes inside the projected collider bounds.
        // Expanding repeated hollow rectangles outward makes exact hitboxes look
        // shifted and bloated compared with Monocle's debug renderer.
        Draw.Rect(left, top, snappedWidth, horizontalThickness, color);
        Draw.Rect(left, bottom - horizontalThickness, snappedWidth, horizontalThickness, color);
        Draw.Rect(left, top, verticalThickness, snappedHeight, color);
        Draw.Rect(right - verticalThickness, top, verticalThickness, snappedHeight, color);
    }

    private static float HitboxThicknessToScreenPixels(float thickness) {
        float surfaceScale = renderingToGameplayBuffer ? 1f : AkronScreenProjection.CurrentViewportScale();
        float requestedPixels = surfaceScale *
                                AkronModuleSettings.ClampHitboxLineThickness(thickness) /
                                HitboxThicknessUnitsPerGamePixel;
        // Persisted old settings can request sub-pixel outlines. The renderer
        // will still count draw calls, but the hitboxes look absent in-game.
        return System.Math.Max(1f, requestedPixels);
    }

    private static AkronHudRect WorldToHitboxSurfaceRect(Level level, Rectangle worldBounds) {
        if (!renderingToGameplayBuffer) {
            return AkronScreenProjection.WorldToHudRect(level, worldBounds);
        }

        Vector2 topLeft = level.Camera.CameraToScreen(new Vector2(worldBounds.X, worldBounds.Y));
        return new AkronHudRect(topLeft.X, topLeft.Y, worldBounds.Width, worldBounds.Height);
    }

    private static Vector2 WorldToHitboxSurface(Level level, Vector2 worldPosition) {
        return renderingToGameplayBuffer
            ? level.Camera.CameraToScreen(worldPosition)
            : AkronScreenProjection.WorldToHud(level, worldPosition);
    }

    private static Color ColorFromRgb(int rgb) {
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    internal static bool IsHazard(Entity entity) {
        string name = entity.GetType().Name;
        return entity is Spikes ||
               name.Contains("Spike") ||
               name.Contains("Spinner") ||
               name.Contains("Hazard") ||
               name.Contains("Kill") ||
               name.Contains("Blade");
    }
}

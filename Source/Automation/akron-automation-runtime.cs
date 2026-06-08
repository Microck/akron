using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static Rectangle? pendingAutoKillDeathArea;

    private static void ApplyAutoKill(Level level, Player player) {
        if (!Settings.AutoKill ||
            player == null ||
            player.Dead ||
            level.Transitioning ||
            level.InCutscene ||
            level.SkippingCutscene ||
            !TryUse(AkronFeatureKind.AutoKill)) {
            return;
        }

        if (IsAutoKillBlockedByPlayerProtection()) {
            return;
        }

        if (Settings.AutoKillArea && TryGetPlayerAutoKillArea(player, out Rectangle autoKillArea)) {
            TriggerAutoKillDeath(player, "Auto kill area triggered.", autoKillArea);
            return;
        }

        if (!Settings.AutoKillTimer) {
            return;
        }

        long elapsedTicks = AkronPracticeStats.GetCurrentMapTime(level);
        long targetTicks = TimeSpan.FromSeconds(AkronModuleSettings.ClampAutoKillSeconds(Settings.AutoKillSeconds)).Ticks;
        if (elapsedTicks >= targetTicks) {
            TriggerAutoKillDeath(player, "Auto kill triggered at " + Settings.AutoKillSeconds + "s.", null);
        }
    }

    private static void ApplyAutoDeafen(Level level, Player player) {
        if (!Settings.AutoDeafen) {
            AkronActions.RestoreAutoDeafen();
            return;
        }

        if (AkronActions.AutoDeafenActive ||
            player == null ||
            player.Dead ||
            level.Transitioning ||
            level.InCutscene ||
            level.SkippingCutscene ||
            !Settings.AutoDeafenArea ||
            !TryGetPlayerAutoDeafenArea(player, out Rectangle autoDeafenArea) ||
            !TryUse(AkronFeatureKind.AutoDeafen)) {
            return;
        }

        if (AkronActions.ActivateAutoDeafen(out string error)) {
            Engine.Scene?.Add(new AkronToast("Auto Deafen hotkey sent: " + autoDeafenArea.Width + "x" + autoDeafenArea.Height + "."));
        } else {
            Engine.Scene?.Add(new AkronToast("Auto Deafen: " + error));
        }
    }

    private static bool IsAutoKillBlockedByPlayerProtection() {
        return Settings.Noclip && TryUse(AkronFeatureKind.Noclip) ||
               Settings.NoclipAccuracy && AkronPolicy.CanUse(AkronFeatureKind.HazardAccuracy).Allowed ||
               Settings.Invincibility && TryUse(AkronFeatureKind.Invincibility);
    }

    private static void TriggerAutoKillDeath(Player player, string message, Rectangle? autoKillArea) {
        RestoreNoclipDepth(player);
        RestorePlayerVisibilityOverride(player);
        pendingAutoKillDeathArea = autoKillArea;
        try {
            player.Die(Vector2.Zero, evenIfInvincible: false);
        } finally {
            pendingAutoKillDeathArea = null;
        }

        Engine.Scene?.Add(new AkronToast(message));
    }

    private static bool TryGetPlayerAutoKillArea(Player player, out Rectangle autoKillArea) {
        Rectangle playerBounds = PlayerAutoKillBounds(player);
        foreach (Rectangle area in GetAutoKillAreas()) {
            if (area.Width > 0 &&
                area.Height > 0 &&
                area.Intersects(playerBounds)) {
                autoKillArea = area;
                return true;
            }
        }

        autoKillArea = default;
        return false;
    }

    private static Rectangle PlayerAutoKillBounds(Player player) {
        if (player.Collider != null) {
            return new Rectangle(
                (int) Math.Floor(player.Collider.AbsoluteX),
                (int) Math.Floor(player.Collider.AbsoluteY),
                (int) Math.Ceiling(player.Collider.Width),
                (int) Math.Ceiling(player.Collider.Height));
        }

        return new Rectangle(
            (int) Math.Floor(player.Position.X - 4f),
            (int) Math.Floor(player.Position.Y - 11f),
            8,
            11);
    }

    public static Rectangle GetAutoKillArea() {
        return GetAutoKillAreas().FirstOrDefault();
    }

    public static List<Rectangle> GetAutoKillAreas() {
        List<Rectangle> areas = (Settings.AutoKillAreas ?? new List<AkronRectangleData>())
            .Where(area => area != null && area.Width > 0 && area.Height > 0)
            .Select(area => new Rectangle(
                area.X,
                area.Y,
                AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
                AkronModuleSettings.ClampAutoKillAreaSize(area.Height)))
            .ToList();

        return areas;
    }

    public static void SetAutoKillArea(Rectangle area) {
        Settings.AutoKillAreas = new List<AkronRectangleData>();
        AddAutoKillArea(area);
    }

    public static void AddAutoKillArea(Rectangle area) {
        if (Settings.AutoKillAreas == null) {
            Settings.AutoKillAreas = new List<AkronRectangleData>();
        }

        Rectangle clamped = new Rectangle(
            area.X,
            area.Y,
            AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
            AkronModuleSettings.ClampAutoKillAreaSize(area.Height));
        if (clamped.Width <= 0 || clamped.Height <= 0) {
            return;
        }

        Settings.AutoKillAreas.Add(new AkronRectangleData(clamped));
        Settings.AutoKillAreaX = area.X;
        Settings.AutoKillAreaY = area.Y;
        Settings.AutoKillAreaWidth = clamped.Width;
        Settings.AutoKillAreaHeight = clamped.Height;
        Settings.AutoKillArea = Settings.AutoKillAreas.Count > 0;
        Settings.AutoKillTimer = false;
        Settings.AutoKillShowArea = true;
    }

    public static void ClearAutoKillArea() {
        Settings.AutoKillArea = false;
        Settings.AutoKillTimer = true;
        Settings.AutoKillAreas = new List<AkronRectangleData>();
        Settings.AutoKillAreaX = 0;
        Settings.AutoKillAreaY = 0;
        Settings.AutoKillAreaWidth = 0;
        Settings.AutoKillAreaHeight = 0;
    }

    public static Rectangle GetAutoDeafenArea() {
        return GetAutoDeafenAreas().FirstOrDefault();
    }

    public static List<Rectangle> GetAutoDeafenAreas() {
        return (Settings.AutoDeafenAreas ?? new List<AkronRectangleData>())
            .Where(area => area != null && area.Width > 0 && area.Height > 0)
            .Select(area => new Rectangle(
                area.X,
                area.Y,
                AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
                AkronModuleSettings.ClampAutoKillAreaSize(area.Height)))
            .ToList();
    }

    public static void SetAutoDeafenArea(Rectangle area) {
        Settings.AutoDeafenAreas = new List<AkronRectangleData>();
        AddAutoDeafenArea(area);
    }

    public static void AddAutoDeafenArea(Rectangle area) {
        if (Settings.AutoDeafenAreas == null) {
            Settings.AutoDeafenAreas = new List<AkronRectangleData>();
        }

        Rectangle clamped = new Rectangle(
            area.X,
            area.Y,
            AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
            AkronModuleSettings.ClampAutoKillAreaSize(area.Height));
        if (clamped.Width <= 0 || clamped.Height <= 0) {
            return;
        }

        Settings.AutoDeafenAreas.Add(new AkronRectangleData(clamped));
        Settings.AutoDeafenAreaX = area.X;
        Settings.AutoDeafenAreaY = area.Y;
        Settings.AutoDeafenAreaWidth = clamped.Width;
        Settings.AutoDeafenAreaHeight = clamped.Height;
        Settings.AutoDeafenArea = Settings.AutoDeafenAreas.Count > 0;
        Settings.AutoDeafenShowArea = true;
    }

    public static void ClearAutoDeafenArea() {
        Settings.AutoDeafenArea = false;
        Settings.AutoDeafenAreas = new List<AkronRectangleData>();
        Settings.AutoDeafenAreaX = 0;
        Settings.AutoDeafenAreaY = 0;
        Settings.AutoDeafenAreaWidth = 0;
        Settings.AutoDeafenAreaHeight = 0;
        AkronActions.RestoreAutoDeafen();
    }

    private static bool TryGetPlayerAutoDeafenArea(Player player, out Rectangle autoDeafenArea) {
        Rectangle playerBounds = PlayerAutoKillBounds(player);
        foreach (Rectangle area in GetAutoDeafenAreas()) {
            if (area.Width > 0 &&
                area.Height > 0 &&
                area.Intersects(playerBounds)) {
                autoDeafenArea = area;
                return true;
            }
        }

        autoDeafenArea = default;
        return false;
    }
}

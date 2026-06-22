using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static Rectangle? pendingAutoKillDeathArea;
    private static int selectedAutoKillAreaIndex;

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

        if (Settings.AutoKillArea &&
            TryGetPlayerAutoKillArea(player, out Rectangle autoKillArea)) {
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
        }
        finally {
            pendingAutoKillDeathArea = null;
        }

        Engine.Scene?.Add(new AkronToast(message));
    }

    private static bool TryGetPlayerAutoKillArea(Player player, out Rectangle autoKillArea) {
        Rectangle playerBounds = PlayerAutoKillBounds(player);
        foreach (AkronAutoKillAreaData areaData in Settings.AutoKillAreas ?? new List<AkronAutoKillAreaData>()) {
            Rectangle area = AutoKillAreaRectangle(areaData);
            if (area.Width > 0 &&
                area.Height > 0 &&
                area.Intersects(playerBounds) &&
                AutoKillAreaConditionsMatch(player, areaData)) {
                autoKillArea = area;
                return true;
            }
        }

        autoKillArea = default;
        return false;
    }

    private static bool AutoKillAreaConditionsMatch(Player player, AkronAutoKillAreaData area) {
        float totalSpeed = player.Speed.Length();
        return AutoKillAreaConditionsMatch(
            area,
            totalSpeed,
            player.Speed.X,
            player.Speed.Y,
            player.Dashes,
            player.OnGround(),
            player.StateMachine.State);
    }

    internal static bool AutoKillAreaConditionsMatch(
        AkronAutoKillAreaData area,
        float totalSpeed,
        float horizontalSpeed,
        float verticalSpeed,
        int dashes,
        bool onGround,
        int playerState) {
        bool matches = AutoKillAreaConditionsMatchCore(area, totalSpeed, horizontalSpeed, verticalSpeed, dashes, onGround, playerState);
        return area.InvertConditions ? !matches : matches;
    }

    private static bool AutoKillAreaConditionsMatchCore(
        AkronAutoKillAreaData area,
        float totalSpeed,
        float horizontalSpeed,
        float verticalSpeed,
        int dashes,
        bool onGround,
        int playerState) {
        if (area.SpeedCondition) {
            int speed = (int) Math.Round(totalSpeed);
            int minSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MinSpeed);
            int maxSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MaxSpeed);
            if (maxSpeed < minSpeed) {
                maxSpeed = minSpeed;
            }

            if (speed < minSpeed || speed > maxSpeed) {
                return false;
            }
        }

        if (area.HorizontalSpeedCondition) {
            int speed = (int) Math.Round(Math.Abs(horizontalSpeed));
            int minSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MinHorizontalSpeed);
            int maxSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MaxHorizontalSpeed);
            if (maxSpeed < minSpeed) {
                maxSpeed = minSpeed;
            }

            if (speed < minSpeed || speed > maxSpeed) {
                return false;
            }
        }

        if (area.VerticalSpeedCondition) {
            int speed = (int) Math.Round(Math.Abs(verticalSpeed));
            int minSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MinVerticalSpeed);
            int maxSpeed = AkronModuleSettings.ClampAutoKillSpeed(area.MaxVerticalSpeed);
            if (maxSpeed < minSpeed) {
                maxSpeed = minSpeed;
            }

            if (speed < minSpeed || speed > maxSpeed) {
                return false;
            }
        }

        if (area.DashCountCondition &&
            dashes != AkronModuleSettings.ClampAutoKillDashCount(area.DashCount)) {
            return false;
        }

        AkronAutoKillGroundCondition groundCondition = AkronModuleSettings.NormalizeAutoKillGroundCondition(area.GroundCondition);
        if (groundCondition == AkronAutoKillGroundCondition.Grounded && !onGround) {
            return false;
        }

        if (groundCondition == AkronAutoKillGroundCondition.Airborne && onGround) {
            return false;
        }

        if (!AutoKillAxisConditionMatches(area.HorizontalDirection, horizontalSpeed)) {
            return false;
        }

        if (!AutoKillAxisConditionMatches(area.VerticalDirection, verticalSpeed)) {
            return false;
        }

        return !area.PlayerStateCondition ||
               playerState == AkronModuleSettings.ClampAutoKillPlayerState(area.PlayerState);
    }

    private static bool AutoKillAxisConditionMatches(AkronAutoKillAxisCondition condition, float speed) {
        switch (AkronModuleSettings.NormalizeAutoKillAxisCondition(condition)) {
            case AkronAutoKillAxisCondition.Negative:
                return speed < -0.01f;
            case AkronAutoKillAxisCondition.Positive:
                return speed > 0.01f;
            case AkronAutoKillAxisCondition.Zero:
                return Math.Abs(speed) <= 0.01f;
            default:
                return true;
        }
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

    public static Rectangle GetSelectedAutoKillArea() {
        return TryGetSelectedAutoKillArea(out AkronAutoKillAreaData area)
            ? AutoKillAreaRectangle(area)
            : default;
    }

    public static AkronAutoKillAreaData GetAutoKillDefaultAreaConditions() {
        Settings.AutoKillDefaultAreaConditions ??= new AkronAutoKillAreaData();
        return Settings.AutoKillDefaultAreaConditions;
    }

    public static bool UseSelectedAutoKillAreaAsDefault() {
        if (!TryGetSelectedAutoKillArea(out AkronAutoKillAreaData area)) {
            return false;
        }

        Settings.AutoKillDefaultAreaConditions = new AkronAutoKillAreaData(area) {
            X = 0,
            Y = 0,
            Width = 0,
            Height = 0
        };
        return true;
    }

    public static List<Rectangle> GetAutoKillAreas() {
        List<Rectangle> areas = (Settings.AutoKillAreas ?? new List<AkronAutoKillAreaData>())
            .Where(area => area != null && area.Width > 0 && area.Height > 0)
            .Select(AutoKillAreaRectangle)
            .ToList();

        return areas;
    }

    public static int GetAutoKillAreaCount() {
        return GetAutoKillAreas().Count;
    }

    public static int GetSelectedAutoKillAreaIndex() {
        int count = Settings.AutoKillAreas?.Count ?? 0;
        if (count <= 0) {
            selectedAutoKillAreaIndex = 0;
            return 0;
        }

        selectedAutoKillAreaIndex = Math.Max(0, Math.Min(selectedAutoKillAreaIndex, count - 1));
        return selectedAutoKillAreaIndex;
    }

    public static bool TrySelectAutoKillArea(int index) {
        int count = Settings.AutoKillAreas?.Count ?? 0;
        if (index < 0 || index >= count) {
            return false;
        }

        selectedAutoKillAreaIndex = index;
        SetLatestAutoKillArea(Settings.AutoKillAreas[index]);
        return true;
    }

    public static bool TryGetSelectedAutoKillArea(out AkronAutoKillAreaData area) {
        int count = Settings.AutoKillAreas?.Count ?? 0;
        if (count <= 0) {
            selectedAutoKillAreaIndex = 0;
            area = null;
            return false;
        }

        int index = GetSelectedAutoKillAreaIndex();
        area = Settings.AutoKillAreas[index];
        return area != null && area.Width > 0 && area.Height > 0;
    }

    public static bool RemoveSelectedAutoKillArea() {
        if (!TryGetSelectedAutoKillArea(out _)) {
            return false;
        }

        Settings.AutoKillAreas.RemoveAt(selectedAutoKillAreaIndex);
        if (Settings.AutoKillAreas.Count == 0) {
            ClearAutoKillArea();
            return true;
        }

        selectedAutoKillAreaIndex = Math.Min(selectedAutoKillAreaIndex, Settings.AutoKillAreas.Count - 1);
        Settings.AutoKillArea = true;
        SetLatestAutoKillArea(Settings.AutoKillAreas[selectedAutoKillAreaIndex]);
        return true;
    }

    public static void SetAutoKillArea(Rectangle area) {
        Settings.AutoKillAreas = new List<AkronAutoKillAreaData>();
        selectedAutoKillAreaIndex = 0;
        AddAutoKillArea(area);
    }

    public static void AddAutoKillArea(Rectangle area) {
        if (Settings.AutoKillAreas == null) {
            Settings.AutoKillAreas = new List<AkronAutoKillAreaData>();
        }

        Rectangle clamped = new Rectangle(
            area.X,
            area.Y,
            AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
            AkronModuleSettings.ClampAutoKillAreaSize(area.Height));
        if (clamped.Width <= 0 || clamped.Height <= 0) {
            return;
        }

        Settings.AutoKillAreas.Add(GetAutoKillDefaultAreaConditions().CopyWithRectangle(clamped));
        selectedAutoKillAreaIndex = Settings.AutoKillAreas.Count - 1;
        SetLatestAutoKillArea(Settings.AutoKillAreas[selectedAutoKillAreaIndex]);
        Settings.AutoKillArea = Settings.AutoKillAreas.Count > 0;
        Settings.AutoKillTimer = false;
        Settings.AutoKillShowArea = true;
    }

    public static void ClearAutoKillArea() {
        Settings.AutoKillArea = false;
        Settings.AutoKillTimer = true;
        Settings.AutoKillAreas = new List<AkronAutoKillAreaData>();
        Settings.AutoKillAreaX = 0;
        Settings.AutoKillAreaY = 0;
        Settings.AutoKillAreaWidth = 0;
        Settings.AutoKillAreaHeight = 0;
    }

    private static Rectangle AutoKillAreaRectangle(AkronAutoKillAreaData area) {
        if (area == null) {
            return default;
        }

        return new Rectangle(
            area.X,
            area.Y,
            AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
            AkronModuleSettings.ClampAutoKillAreaSize(area.Height));
    }

    private static void SetLatestAutoKillArea(AkronAutoKillAreaData area) {
        Rectangle rectangle = AutoKillAreaRectangle(area);
        Settings.AutoKillAreaX = rectangle.X;
        Settings.AutoKillAreaY = rectangle.Y;
        Settings.AutoKillAreaWidth = rectangle.Width;
        Settings.AutoKillAreaHeight = rectangle.Height;
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

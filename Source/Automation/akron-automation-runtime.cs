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

    // Auto Kill and Auto Deafen areas are world-pixel rectangles, and the same pixels exist in
    // every chapter, so each area carries the map it was drawn on. Everything that fires, draws,
    // selects, or counts areas reads the current map's view below, never the stored list.
    internal static string CurrentAreaMapSid() {
        return (Engine.Scene as Level)?.Session?.Area.GetSID() ?? string.Empty;
    }

    private static List<AkronAutoKillAreaData> CurrentMapAutoKillAreas() {
        string mapSid = CurrentAreaMapSid();
        return string.IsNullOrEmpty(mapSid)
            ? new List<AkronAutoKillAreaData>()
            : (Settings.AutoKillAreas ?? new List<AkronAutoKillAreaData>())
                .Where(area => area != null && string.Equals(area.MapSid, mapSid, StringComparison.Ordinal))
                .ToList();
    }

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
            TryGetPlayerAutoKillArea(level, player, out Rectangle autoKillArea)) {
            TriggerAutoKillDeath(player, "Auto kill area triggered.", autoKillArea);
            return;
        }

        if (!Settings.AutoKillTimer || Session.AutoKillTimerFired) {
            return;
        }

        // The threshold is time played on this attempt, not chapter time: chapter time never
        // resets on death, so reading it killed on every respawn once the chapter passed it.
        if (Session.AttemptElapsedSeconds < AkronModuleSettings.ClampAutoKillSeconds(Settings.AutoKillSeconds)) {
            return;
        }

        // The attempt is spent before the death, not after it: Player.Die runs Akron's death
        // hook inside the call below, and that hook starts the next attempt and clears this
        // flag. Setting it afterwards would mark the fresh attempt as already fired.
        Session.AutoKillTimerFired = true;
        TriggerAutoKillDeath(player, "Auto kill triggered at " + Settings.AutoKillSeconds + "s.", null);
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
            !TryGetPlayerAutoDeafenArea(level, player, out Rectangle autoDeafenArea) ||
            !TryUse(AkronFeatureKind.AutoDeafen)) {
            return;
        }

        if (AkronActions.ActivateAutoDeafen(out string error)) {
            Engine.Scene?.Add(new AkronToast("Auto Deafen hotkey sent: " + autoDeafenArea.Width + "x" + autoDeafenArea.Height + "."));
        } else {
            Engine.Scene?.Add(new AkronToast("Auto Deafen: " + error));
        }
    }

    // Celeste's own Assist invincibility counts: Player.Die(evenIfInvincible: false) is a no-op
    // under it, so without this check Auto Kill would keep asking for a death that never happens.
    private static bool IsAutoKillBlockedByPlayerProtection() {
        return Settings.Noclip && TryUse(AkronFeatureKind.Noclip) ||
               Settings.NoclipAccuracy && AkronPolicy.CanUse(AkronFeatureKind.HazardAccuracy).Allowed ||
               Settings.Invincibility && TryUse(AkronFeatureKind.Invincibility) ||
               global::Celeste.SaveData.Instance?.Assists.Invincible == true;
    }

    // Returns whether Madeline actually died. Player.Die returns null when something still
    // refuses the death, and a toast for a death that did not happen is a lie.
    private static bool TriggerAutoKillDeath(Player player, string message, Rectangle? autoKillArea) {
        RestoreNoclipDepth(player);
        RestorePlayerVisibilityOverride(player);
        pendingAutoKillDeathArea = autoKillArea;
        PlayerDeadBody deadBody;
        try {
            deadBody = player.Die(Vector2.Zero, evenIfInvincible: false);
        }
        finally {
            pendingAutoKillDeathArea = null;
        }

        if (deadBody == null && !player.Dead) {
            return false;
        }

        Engine.Scene?.Add(new AkronToast(message));
        return true;
    }

    // Iterates the stored list rather than the map view: this runs every frame, and the view
    // would allocate a list to do the same string comparison.
    private static bool TryGetPlayerAutoKillArea(Level level, Player player, out Rectangle autoKillArea) {
        string mapSid = level?.Session?.Area.GetSID() ?? string.Empty;
        Rectangle playerBounds = PlayerAutoKillBounds(player);
        foreach (AkronAutoKillAreaData areaData in Settings.AutoKillAreas ?? new List<AkronAutoKillAreaData>()) {
            if (areaData == null ||
                string.IsNullOrEmpty(mapSid) ||
                !string.Equals(areaData.MapSid, mapSid, StringComparison.Ordinal)) {
                continue;
            }

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
        return CurrentMapAutoKillAreas()
            .Where(area => area.Width > 0 && area.Height > 0)
            .Select(AutoKillAreaRectangle)
            .ToList();
    }

    public static int GetAutoKillAreaCount() {
        return GetAutoKillAreas().Count;
    }

    // The selection index addresses the current map's areas, which is the list the player sees.
    public static int GetSelectedAutoKillAreaIndex() {
        int count = CurrentMapAutoKillAreas().Count;
        if (count <= 0) {
            selectedAutoKillAreaIndex = 0;
            return 0;
        }

        selectedAutoKillAreaIndex = Math.Max(0, Math.Min(selectedAutoKillAreaIndex, count - 1));
        return selectedAutoKillAreaIndex;
    }

    public static bool TrySelectAutoKillArea(int index) {
        List<AkronAutoKillAreaData> areas = CurrentMapAutoKillAreas();
        if (index < 0 || index >= areas.Count) {
            return false;
        }

        selectedAutoKillAreaIndex = index;
        SetLatestAutoKillArea(areas[index]);
        return true;
    }

    public static bool TryGetSelectedAutoKillArea(out AkronAutoKillAreaData area) {
        List<AkronAutoKillAreaData> areas = CurrentMapAutoKillAreas();
        if (areas.Count <= 0) {
            selectedAutoKillAreaIndex = 0;
            area = null;
            return false;
        }

        area = areas[GetSelectedAutoKillAreaIndex()];
        return area != null && area.Width > 0 && area.Height > 0;
    }

    public static bool RemoveSelectedAutoKillArea() {
        if (!TryGetSelectedAutoKillArea(out AkronAutoKillAreaData selected)) {
            return false;
        }

        Settings.AutoKillAreas.Remove(selected);
        List<AkronAutoKillAreaData> remaining = CurrentMapAutoKillAreas();
        if (remaining.Count == 0) {
            ClearAutoKillArea();
            return true;
        }

        selectedAutoKillAreaIndex = Math.Min(selectedAutoKillAreaIndex, remaining.Count - 1);
        Settings.AutoKillArea = true;
        SetLatestAutoKillArea(remaining[selectedAutoKillAreaIndex]);
        return true;
    }

    // Replaces this map's areas. Areas drawn on other maps are not this map's business.
    public static void SetAutoKillArea(Rectangle area) {
        ClearAutoKillArea();
        selectedAutoKillAreaIndex = 0;
        AddAutoKillArea(area);
    }

    public static void AddAutoKillArea(Rectangle area) {
        string mapSid = CurrentAreaMapSid();
        if (string.IsNullOrEmpty(mapSid)) {
            Engine.Scene?.Add(new AkronToast("Enter a map before drawing an Auto Kill area."));
            return;
        }

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

        AkronAutoKillAreaData added = GetAutoKillDefaultAreaConditions().CopyWithRectangle(clamped);
        added.MapSid = mapSid;
        Settings.AutoKillAreas.Add(added);
        List<AkronAutoKillAreaData> areas = CurrentMapAutoKillAreas();
        selectedAutoKillAreaIndex = Math.Max(0, areas.Count - 1);
        SetLatestAutoKillArea(added);
        Settings.AutoKillArea = areas.Count > 0;
        Settings.AutoKillTimer = false;
        Settings.AutoKillShowArea = true;
    }

    // Clears the configured areas and nothing else: the Area and Timer mode toggles and Auto
    // Kill itself stay where the player left them.
    public static void ClearAutoKillArea() {
        List<AkronAutoKillAreaData> areas = CurrentMapAutoKillAreas();
        foreach (AkronAutoKillAreaData area in areas) {
            Settings.AutoKillAreas.Remove(area);
        }

        selectedAutoKillAreaIndex = 0;
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
        return CurrentMapAutoDeafenAreas()
            .Where(area => area.Width > 0 && area.Height > 0)
            .Select(AutoDeafenAreaRectangle)
            .ToList();
    }

    private static List<AkronRectangleData> CurrentMapAutoDeafenAreas() {
        string mapSid = CurrentAreaMapSid();
        return string.IsNullOrEmpty(mapSid)
            ? new List<AkronRectangleData>()
            : (Settings.AutoDeafenAreas ?? new List<AkronRectangleData>())
                .Where(area => area != null && string.Equals(area.MapSid, mapSid, StringComparison.Ordinal))
                .ToList();
    }

    private static Rectangle AutoDeafenAreaRectangle(AkronRectangleData area) {
        if (area == null) {
            return default;
        }

        return new Rectangle(
            area.X,
            area.Y,
            AkronModuleSettings.ClampAutoKillAreaSize(area.Width),
            AkronModuleSettings.ClampAutoKillAreaSize(area.Height));
    }

    // Replaces this map's areas, like SetAutoKillArea.
    public static void SetAutoDeafenArea(Rectangle area) {
        ClearAutoDeafenArea();
        AddAutoDeafenArea(area);
    }

    public static void AddAutoDeafenArea(Rectangle area) {
        string mapSid = CurrentAreaMapSid();
        if (string.IsNullOrEmpty(mapSid)) {
            Engine.Scene?.Add(new AkronToast("Enter a map before drawing an Auto Deafen area."));
            return;
        }

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

        Settings.AutoDeafenAreas.Add(new AkronRectangleData(clamped, mapSid));
        Settings.AutoDeafenAreaX = area.X;
        Settings.AutoDeafenAreaY = area.Y;
        Settings.AutoDeafenAreaWidth = clamped.Width;
        Settings.AutoDeafenAreaHeight = clamped.Height;
        Settings.AutoDeafenArea = CurrentMapAutoDeafenAreas().Count > 0;
        Settings.AutoDeafenShowArea = true;
    }

    public static void ClearAutoDeafenArea() {
        Settings.AutoDeafenArea = false;
        foreach (AkronRectangleData area in CurrentMapAutoDeafenAreas()) {
            Settings.AutoDeafenAreas.Remove(area);
        }

        Settings.AutoDeafenAreaX = 0;
        Settings.AutoDeafenAreaY = 0;
        Settings.AutoDeafenAreaWidth = 0;
        Settings.AutoDeafenAreaHeight = 0;
        AkronActions.RestoreAutoDeafen();
    }

    private static bool TryGetPlayerAutoDeafenArea(Level level, Player player, out Rectangle autoDeafenArea) {
        string mapSid = level?.Session?.Area.GetSID() ?? string.Empty;
        Rectangle playerBounds = PlayerAutoKillBounds(player);
        foreach (AkronRectangleData areaData in Settings.AutoDeafenAreas ?? new List<AkronRectangleData>()) {
            if (areaData == null ||
                string.IsNullOrEmpty(mapSid) ||
                !string.Equals(areaData.MapSid, mapSid, StringComparison.Ordinal)) {
                continue;
            }

            Rectangle area = AutoDeafenAreaRectangle(areaData);
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

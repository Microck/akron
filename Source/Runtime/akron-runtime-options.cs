using System;
using System.Collections.Generic;
using System.Reflection;
using Celeste;
using FMOD.Studio;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronRuntimeOptions {
    private const float FreeCameraMouseDeadzone = 0.08f;
    private static bool audioPitchApplied;
    private static Level screenshakeLevel;
    private static bool previousScreenshakeDisabled;
    private static Player freeCameraLockedPlayer;
    private static bool freeCameraPlayerStateCaptured;
    private static int previousFreeCameraPlayerState;
    private static readonly Dictionary<Entity, bool> HiddenPauseMenuVisibility = new Dictionary<Entity, bool>();
    private static readonly Dictionary<object, bool> HiddenPauseHudVisibility = new Dictionary<object, bool>();
    private static readonly Dictionary<string, object> SafeModeStatSnapshot = new Dictionary<string, object>();
    private static string safeModeSnapshotArea = string.Empty;
    private static object safeModeSnapshotSaveData;
    private static object safeModeSnapshotModeStats;
    private static readonly AkronVisualTuningOverrides VisualTuningOverrides = new();

    public static void Reset() {
        RestorePauseMenuVisibility();
        RestoreFreeCameraPlayerControl();
        RestoreVisualTuning();
        RestoreScreenshake();
        RestoreAudioPitch();
        AkronEarAid.ReconcileVolumeOverrides(restore: true);
        AkronModule.RestoreTransitionSpeed();
        AkronModule.RestoreMadelineVisualOverrides();
        ClearSafeModeStatSnapshot();
    }

    public static void Apply(Level level, Player player) {
        ApplyAudioSpeedAndPitch();
        AkronEarAid.ReconcileVolumeOverrides();
        ApplyHudVisibility(level);
        ApplyPauseMenuVisibility(level);
        ApplyScreenshake(level);
        ApplyVisualTuning(level);
        ApplyFreeCamera(level, player);
        ApplySafeModeStatFreeze(level);
    }

    public static void ApplyScreenshakeAfterLevelUpdate(Level level) {
        ApplyScreenshake(level, afterLevelUpdate: true);
    }

    public static string DescribeVisualTuning() {
        List<string> active = new List<string>();
        if (AkronModule.Settings.LightLevel) {
            active.Add("Light " + AkronModuleSettings.ClampLightLevelPercent(AkronModule.Settings.LightLevelPercent).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
        }

        if (AkronModule.Settings.BloomLevel) {
            active.Add("Bloom " + AkronModuleSettings.ClampBloomLevelPercent(AkronModule.Settings.BloomLevelPercent).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
        }

        if (AkronModule.Settings.ScreenTint) {
            active.Add("Tint " + AkronModuleSettings.ClampOpacity(AkronModule.Settings.ScreenTintOpacity).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%");
        }

        return active.Count == 0 ? "Off" : string.Join(" / ", active);
    }

    public static string DescribeFpsBypass() {
        if (!AkronMotionSmoothingInterop.Loaded) {
            return "Missing";
        }

        return AkronModule.Settings.FpsBypass
            ? AkronModuleSettings.ResolveFrameBypassRates(
                AkronModule.Settings.FpsBypass,
                AkronModule.Settings.FpsBypassTarget,
                AkronModule.Settings.TpsBypass,
                AkronModule.Settings.TpsBypassTarget,
                AkronModule.Settings.FrameBypassMethod).Describe()
            : "Off";
    }

    public static string DescribeTpsBypass() {
        if (!AkronMotionSmoothingInterop.Loaded) {
            return "Missing";
        }

        return AkronModule.Settings.TpsBypass
            ? AkronModuleSettings.ClampTpsTarget(AkronModule.Settings.TpsBypassTarget) + " TPS"
            : "Off";
    }

    public static AkronFrameBypassRates ResolveCurrentFrameBypassRates() {
        return AkronModuleSettings.ResolveFrameBypassRates(
            AkronModule.Settings.FpsBypass && AkronPolicy.CanUse(AkronFeatureKind.FpsBypass).Allowed,
            AkronModule.Settings.FpsBypassTarget,
            AkronModule.Settings.TpsBypass && AkronPolicy.CanUse(AkronFeatureKind.TpsBypass).Allowed,
            AkronModule.Settings.TpsBypassTarget,
            AkronModule.Settings.FrameBypassMethod);
    }

    public static string DescribeAudioSpeed() {
        if (!AkronModule.Settings.AudioSpeed) {
            return "Off";
        }

        return AkronModule.Settings.AudioSpeedPolicy == AkronAudioSpeedPolicy.Independent
            ? AkronModule.Settings.AudioSpeedMultiplier.ToString("0.0x")
            : AkronModule.Settings.AudioSpeedPolicy.ToString();
    }

    public static string DescribePitchShift() {
        if (!AkronModule.Settings.PitchShift) {
            return "Off";
        }

        return AkronModule.Settings.PitchShiftPolicy == AkronPitchPolicy.Independent
            ? AkronModule.Settings.PitchShiftMultiplier.ToString("0.0x")
            : AkronModule.Settings.PitchShiftPolicy.ToString();
    }

    public static string DescribeHudVisibility() {
        if (AkronModule.Settings.HideVanillaHud && AkronModule.Settings.HideAkronHud) {
            return "All hidden";
        }

        if (AkronModule.Settings.HideVanillaHud) {
            return "Vanilla hidden";
        }

        return AkronModule.Settings.HideAkronHud ? "Akron hidden" : "Visible";
    }

    public static string DescribeSafeModeStats() {
        // The guards only run while Safe Mode is on, so counting them while it is off would
        // describe something that is not happening.
        if (!AkronModule.Settings.SafeMode) {
            return "Off";
        }

        int enabled = (AkronModule.Settings.SafeModeFreezeAttempts ? 1 : 0) +
                      (AkronModule.Settings.SafeModeFreezeJumps ? 1 : 0) +
                      (AkronModule.Settings.SafeModeFreezeBestRun ? 1 : 0);
        return enabled == 0 ? "Indicator only" : enabled + " guards";
    }

    public static string DescribeFreeCamera() {
        if (!AkronModule.IsFreeCameraEffectiveEnabled()) {
            return "Off";
        }

        List<string> parts = new List<string> {
            AkronModule.Settings.FreeCamera ? "Room" : "Held",
            AkronModule.Settings.FreeCameraSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (AkronModule.IsFreeCameraMouseControlEffectiveEnabled()) {
            parts.Add("Mouse");
        }
        return string.Join(" / ", parts);
    }

    public static bool ShouldFreezeGameplayForFreeCamera(Level level) {
        return IsFreeCameraActive(level) &&
               AkronPolicy.CanUse(AkronFeatureKind.Freeze).Allowed &&
               (AkronModule.Settings.FreeCamera && AkronModule.Settings.FreeCameraFreezeGameplay ||
                AkronModule.IsCursorToolsFreezeGameplayEffectiveEnabled());
    }

    public static bool IsFreeCameraActive(Level level) {
        return level != null &&
               AkronModule.IsFreeCameraEffectiveEnabled() &&
               AkronPolicy.CanUse(AkronFeatureKind.FreeCamera).Allowed;
    }

    public static bool ShouldSuppressPauseHud(Scene scene) {
        return scene is Level level &&
               level.Paused &&
               AkronModule.Settings.HidePauseMenu &&
               !AkronPromptMenu.IsOpen &&
               !AkronDiagnosticsMenu.IsOpen &&
               AkronPolicy.CanUse(AkronFeatureKind.PauseMenuVisibility).Allowed;
    }

    public static bool ShouldSuppressPauseBackgroundFade(Scene scene) {
        return scene is Level &&
               AkronModule.IsPauseCountdownActive &&
               AkronModule.Settings.PauseCountdownHidePauseTint &&
               AkronPolicy.CanUse(AkronFeatureKind.PauseCountdown).Allowed;
    }

    public static void HoldSceneClockForSkippedLevelUpdate(Level level) {
        if (level == null) {
            return;
        }

        // Scene.BeforeUpdate runs before Level.Update and advances these clocks
        // even when Akron intentionally skips the actual level update. Restore
        // that frame's increment so paused/frozen gameplay also freezes timers
        // and time-driven scene effects.
        if (!level.Paused) {
            level.TimeActive = Math.Max(0f, level.TimeActive - Engine.DeltaTime);
        }
        level.RawTimeActive = Math.Max(0f, level.RawTimeActive - Engine.RawDeltaTime);
    }

    private static void ApplyAudioSpeedAndPitch() {
        bool audioSpeedAllowed = AkronModule.Settings.AudioSpeed && AkronPolicy.CanUse(AkronFeatureKind.AudioSpeed).Allowed;
        bool pitchShiftAllowed = AkronModule.Settings.PitchShift && AkronPolicy.CanUse(AkronFeatureKind.PitchShift).Allowed;
        if (!audioSpeedAllowed && !pitchShiftAllowed) {
            RestoreAudioPitch();
            return;
        }

        float speed = 1f;
        if (audioSpeedAllowed && AkronModule.TryUseRuntime(AkronFeatureKind.AudioSpeed)) {
            speed = AkronModule.Settings.AudioSpeedPolicy switch {
                AkronAudioSpeedPolicy.SyncTimescale => AkronModule.Session?.TimescaleEnabled == true && AkronPolicy.CanUse(AkronFeatureKind.Timescale).Allowed ? AkronModule.Session.TimescaleMultiplier : 1f,
                AkronAudioSpeedPolicy.Independent => AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.AudioSpeedMultiplier),
                _ => 1f
            };
        }

        float pitch = 1f;
        if (pitchShiftAllowed && AkronModule.TryUseRuntime(AkronFeatureKind.PitchShift)) {
            pitch = AkronModule.Settings.PitchShiftPolicy switch {
                AkronPitchPolicy.FollowSpeed => speed,
                AkronPitchPolicy.Independent => AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.PitchShiftMultiplier),
                _ => 1f
            };
        }

        SetAudioPitch(Calc.Clamp(speed * pitch, 0.1f, 4f));
        audioPitchApplied = true;
    }

    private static void RestoreAudioPitch() {
        if (audioPitchApplied) {
            SetAudioPitch(1f);
            audioPitchApplied = false;
        }
    }

    private static void SetAudioPitch(float pitch) {
        TrySetPitch(Audio.CurrentMusicEventInstance, pitch);
        TrySetPitch(Audio.CurrentAmbienceEventInstance, pitch);
        TrySetPitch(Audio.currentAltMusicEvent, pitch);
    }

    private static void TrySetPitch(EventInstance instance, float pitch) {
        if (instance == null) {
            return;
        }

        instance.setPitch(pitch);
    }

    private static void ApplyHudVisibility(Level level)
    {
        if (level != null && AkronModule.Settings.HideVanillaHud)
        {
            AkronModule.TryUseRuntime(AkronFeatureKind.HudVisibility);
        }
        // Only final compositing is suppressed. Keep native buffers refreshing
        // and do not capture false visibility into another owner's save state.
    }

    internal static void ApplyPauseMenuVisibility(Level level) {
        if (level == null ||
            !AkronModule.Settings.HidePauseMenu ||
            !level.Paused ||
            AkronDiagnosticsMenu.IsOpen) {
            RestorePauseMenuVisibility();
            return;
        }

        if (!AkronPolicy.CanUse(AkronFeatureKind.PauseMenuVisibility).Allowed) {
            RestorePauseMenuVisibility();
            return;
        }

        foreach (Entity entity in level.Entities) {
            // The prompt is Akron's own modal surface; ShouldSuppressPauseHud exempts it for
            // the same reason. Hidden, it would still block and take input.
            if (entity is not TextMenu menu || AkronPromptMenu.IsPromptMenu(menu)) {
                continue;
            }

            if (!HiddenPauseMenuVisibility.ContainsKey(menu)) {
                HiddenPauseMenuVisibility[menu] = menu.Visible;
            }
            menu.Visible = false;
        }

        // Celeste's pause dimming and counters live on the level HUD renderers,
        // so hiding TextMenu entities alone leaves the pause layer visible.
        SetHudRendererVisibility(level, false, HiddenPauseHudVisibility);
    }

    internal static void RestorePauseMenuVisibility() {
        foreach (KeyValuePair<Entity, bool> entry in HiddenPauseMenuVisibility) {
            if (entry.Key.Scene != null) {
                entry.Key.Visible = entry.Value;
            }
        }
        HiddenPauseMenuVisibility.Clear();

        foreach ((object renderer, bool visible) in HiddenPauseHudVisibility) {
            SetVisible(renderer, visible);
        }
        HiddenPauseHudVisibility.Clear();
    }

    private static void ApplyScreenshake(Level level, bool afterLevelUpdate = false) {
        if (level == null || !AkronModule.Settings.Screenshake || !AkronPolicy.CanUse(AkronFeatureKind.Screenshake).Allowed) {
            RestoreScreenshake();
            return;
        }

        if (screenshakeLevel != level) {
            RestoreScreenshake();
            screenshakeLevel = level;
            previousScreenshakeDisabled = GetMember(level, "DisableScreenShake") is true;
        }

        int intensity = AkronModuleSettings.ClampScreenshakeIntensity(AkronModule.Settings.ScreenshakeIntensity);
        bool disabled = AkronModule.Settings.Screenshake &&
                        intensity <= 0 &&
                        AkronModule.TryUseRuntime(AkronFeatureKind.Screenshake);
        SetMember(level, "DisableScreenShake", disabled);
        if (disabled) {
            SetMember(level, "ShakeVector", Vector2.Zero);
            return;
        }

        if (!afterLevelUpdate ||
            !AkronModule.Settings.Screenshake ||
            intensity >= 100 ||
            !AkronModule.TryUseRuntime(AkronFeatureKind.Screenshake) ||
            !TryGetMemberValue(level, "ShakeVector", out object shakeVector) ||
            shakeVector is not Vector2 vector) {
            return;
        }

        SetMember(level, "ShakeVector", vector * (intensity / 100f));
    }

    private static void RestoreScreenshake() {
        if (screenshakeLevel != null) {
            SetMember(screenshakeLevel, "DisableScreenShake", previousScreenshakeDisabled);
            screenshakeLevel = null;
        }
    }

    private static void ApplyVisualTuning(Level level)
    {
        if (level == null)
        {
            RestoreVisualTuning();
            return;
        }

        bool allowed = ShouldApplyVisualTuning();
        float light = AkronModuleSettings.ClampLightLevelPercent(AkronModule.Settings.LightLevelPercent) / 100f;
        float bloom = AkronModuleSettings.ClampBloomLevelPercent(AkronModule.Settings.BloomLevelPercent) / 100f;
        VisualTuningOverrides.Apply(level, level.Session?.Level ?? string.Empty,
            GetMember(level, "Lighting"), GetMember(level, "Bloom"),
            allowed && AkronModule.Settings.LightLevel, Math.Clamp(1f - light, 0f, 1f),
            allowed && AkronModule.Settings.BloomLevel, Math.Clamp(bloom, 0f, 1f), Math.Max(1f, bloom));
    }

    private static bool ShouldApplyVisualTuning()
    {
        if (!AkronModule.Settings.LightLevel && !AkronModule.Settings.BloomLevel && !AkronModule.Settings.ScreenTint)
        {
            return false;
        }

        return AkronModule.TryUseRuntime(AkronFeatureKind.VisualTuning);
    }

    internal static void PrepareVisualTuningSnapshot(Level source, Level snapshot)
    {
        if (source == null || snapshot == null || ReferenceEquals(source, snapshot))
        {
            return;
        }
        VisualTuningOverrides.CopyBaselinesToSnapshot(source,
            GetMember(source, "Lighting"), GetMember(source, "Bloom"),
            GetMember(snapshot, "Lighting"), GetMember(snapshot, "Bloom"));
    }

    internal static void BeforeVisualTuningLevelLoad(Level level)
    {
        VisualTuningOverrides.BeforeLevelLoad(level, Engine.Scene);
    }

    private static void RestoreVisualTuning()
    {
        VisualTuningOverrides.Reset();
    }

    internal static bool IsCurrentVanillaHudRenderer(object renderer, object hudRenderer, object subHudRenderer)
    {
        return renderer != null &&
               (ReferenceEquals(renderer, hudRenderer) || ReferenceEquals(renderer, subHudRenderer));
    }

    private static void SetHudRendererVisibility(Level level, bool visible, Dictionary<object, bool> visibilitySnapshot) {
        foreach (string memberName in new[] { "HudRenderer", "SubHudRenderer" }) {
            object renderer = GetMember(level, memberName);
            if (renderer == null) {
                continue;
            }

            if (!visibilitySnapshot.ContainsKey(renderer)) {
                visibilitySnapshot[renderer] = GetVisible(renderer) ?? true;
            }

            SetVisible(renderer, visible);
        }
    }

    internal static void ApplyFreeCameraForStartPosPlacement(Level level) {
        if (level != null && AkronModule.IsFreeCameraEffectiveEnabled() &&
            AkronModule.TryUseRuntime(AkronFeatureKind.FreeCamera)) {
            // Placement already freezes gameplay. Do not capture or change player state.
            MoveFreeCamera(level);
        }
    }

    private static void ApplyFreeCamera(Level level, Player player) {
        if (level == null || !AkronModule.IsFreeCameraEffectiveEnabled()) {
            RestoreFreeCameraPlayerControl();
            return;
        }

        if (!AkronModule.TryUseRuntime(AkronFeatureKind.FreeCamera)) {
            RestoreFreeCameraPlayerControl();
            return;
        }

        LockFreeCameraPlayerControl(player);
        MoveFreeCamera(level);
    }

    private static void MoveFreeCamera(Level level) {
        if (AkronModule.ShouldSuppressFreeCameraMovementForClickTeleport()) {
            level.Camera.Position = ClampCameraToRoom(level, level.Camera.Position);
            return;
        }

        Vector2 aim = Input.Aim.Value;
        if (MInput.Keyboard.Check(Microsoft.Xna.Framework.Input.Keys.Left)) {
            aim.X -= 1f;
        }
        if (MInput.Keyboard.Check(Microsoft.Xna.Framework.Input.Keys.Right)) {
            aim.X += 1f;
        }
        if (MInput.Keyboard.Check(Microsoft.Xna.Framework.Input.Keys.Up)) {
            aim.Y -= 1f;
        }
        if (MInput.Keyboard.Check(Microsoft.Xna.Framework.Input.Keys.Down)) {
            aim.Y += 1f;
        }

        aim = NormalizeFreeCameraInputAim(aim);
        if (AkronModule.IsFreeCameraMouseControlEffectiveEnabled()) {
            MouseState mouse = Mouse.GetState();
            Vector2 mouseAim = CalculateFreeCameraMouseAim(AkronScreenProjection.MouseScreenToGame(new Vector2(mouse.X, mouse.Y)));
            aim = ClampFreeCameraAim(new Vector2 {
                X = aim.X + mouseAim.X,
                Y = aim.Y + mouseAim.Y
            });
        }

        if (VectorLength(aim) > 0f) {
            float speed = AkronModuleSettings.ClampFreeCameraSpeed(AkronModule.Settings.FreeCameraSpeed);
            level.Camera.Position = ClampCameraToRoom(level, new Vector2 {
                X = level.Camera.Position.X + aim.X * speed * Engine.RawDeltaTime,
                Y = level.Camera.Position.Y + aim.Y * speed * Engine.RawDeltaTime
            });
        } else {
            level.Camera.Position = ClampCameraToRoom(level, level.Camera.Position);
        }

    }

    internal static Vector2 CalculateFreeCameraMouseAim(Vector2 mouseGamePosition) {
        Vector2 aim = new Vector2 {
            X = (mouseGamePosition.X - 160f) / 160f,
            Y = (mouseGamePosition.Y - 90f) / 90f
        };
        float length = VectorLength(aim);
        if (length <= FreeCameraMouseDeadzone) {
            return default;
        }

        float adjustedLength = Math.Min(1f, (length - FreeCameraMouseDeadzone) / (1f - FreeCameraMouseDeadzone));
        Vector2 normalized = NormalizeFreeCameraInputAim(aim);
        return new Vector2 {
            X = normalized.X * adjustedLength,
            Y = normalized.Y * adjustedLength
        };
    }

    internal static Vector2 NormalizeFreeCameraInputAim(Vector2 aim) {
        float length = VectorLength(aim);
        return length <= 0f
            ? default
            : new Vector2 {
                X = aim.X / length,
                Y = aim.Y / length
            };
    }

    internal static Vector2 ClampFreeCameraAim(Vector2 aim) {
        if (VectorLength(aim) <= 1f) {
            return aim;
        }

        return NormalizeFreeCameraInputAim(aim);
    }

    private static float VectorLength(Vector2 vector) {
        return (float) Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
    }

    private static Vector2 ClampCameraToRoom(Level level, Vector2 position) {
        Rectangle bounds = level.Bounds;
        float maxX = Math.Max(bounds.Left, bounds.Right - 320f);
        float maxY = Math.Max(bounds.Top, bounds.Bottom - 180f);
        return new Vector2(
            Calc.Clamp(position.X, bounds.Left, maxX),
            Calc.Clamp(position.Y, bounds.Top, maxY));
    }

    private static void LockFreeCameraPlayerControl(Player player) {
        if (player == null || player.Dead) {
            return;
        }

        if (freeCameraLockedPlayer != player) {
            RestoreFreeCameraPlayerControl();
            freeCameraLockedPlayer = player;
            previousFreeCameraPlayerState = player.StateMachine.State;
            freeCameraPlayerStateCaptured = true;
        }

        player.StateMachine.State = Player.StDummy;
    }

    private static void RestoreFreeCameraPlayerControl() {
        if (freeCameraLockedPlayer != null &&
            freeCameraPlayerStateCaptured &&
            freeCameraLockedPlayer.Scene != null &&
            freeCameraLockedPlayer.StateMachine.State == Player.StDummy &&
            previousFreeCameraPlayerState != Player.StDummy) {
            freeCameraLockedPlayer.StateMachine.State = previousFreeCameraPlayerState;
        }

        freeCameraLockedPlayer = null;
        freeCameraPlayerStateCaptured = false;
        previousFreeCameraPlayerState = Player.StNormal;
    }

    private static void ApplySafeModeStatFreeze(Level level) {
        if (level == null ||
            !AkronModule.Settings.SafeMode ||
            !AnySafeModeStatFreezeEnabled() ||
            !AkronModule.TryUseRuntime(AkronFeatureKind.SafeModeStats)) {
            ClearSafeModeStatSnapshot();
            return;
        }

        ApplySafeModeStatFreezeState(
            SaveData.Instance,
            GetAreaModeStats(level),
            level.Session.Area.GetSID() + ":" + level.Session.Area.Mode,
            AkronModule.Settings.SafeModeFreezeAttempts,
            AkronModule.Settings.SafeModeFreezeJumps,
            AkronModule.Settings.SafeModeFreezeBestRun);
    }

    internal static void ApplySafeModeStatFreezeState(object saveData, object modeStats,
        string areaKey, bool freezeDeaths, bool freezeJumps, bool freezeBestRun) {
        if (saveData == null || (!freezeDeaths && !freezeJumps && !freezeBestRun)) {
            ClearSafeModeStatSnapshot();
            return;
        }

        // A save or stats object can be replaced without changing the area SID.
        if (!ReferenceEquals(safeModeSnapshotSaveData, saveData) ||
            !ReferenceEquals(safeModeSnapshotModeStats, modeStats) ||
            safeModeSnapshotArea != areaKey) {
            ClearSafeModeStatSnapshot();
            safeModeSnapshotSaveData = saveData;
            safeModeSnapshotModeStats = modeStats;
            safeModeSnapshotArea = areaKey;
        }

        FreezeStat(saveData, "TotalDeaths", "file:TotalDeaths", freezeDeaths);
        FreezeStat(modeStats, "Deaths", "area:Deaths", freezeDeaths);
        FreezeStat(saveData, "TotalJumps", "file:TotalJumps", freezeJumps);
        FreezeStat(modeStats, "BestTime", "area:BestTime", freezeBestRun);
        FreezeStat(modeStats, "BestFullClearTime", "area:BestFullClearTime", freezeBestRun);
    }

    private static void ClearSafeModeStatSnapshot() {
        SafeModeStatSnapshot.Clear();
        safeModeSnapshotArea = string.Empty;
        safeModeSnapshotSaveData = null;
        safeModeSnapshotModeStats = null;
    }

    private static void FreezeStat(object target, string name, string key, bool enabled) {
        if (!enabled || target == null) {
            SafeModeStatSnapshot.Remove(key);
        } else if (SafeModeStatSnapshot.TryGetValue(key, out object value)) {
            SetMember(target, name, value);
        } else if (TryGetMemberValue(target, name, out value)) {
            // Capture when this guard is enabled, not when a different guard was enabled.
            SafeModeStatSnapshot[key] = value;
        }
    }

    private static bool AnySafeModeStatFreezeEnabled() {
        return AkronModule.Settings.SafeModeFreezeAttempts ||
               AkronModule.Settings.SafeModeFreezeJumps ||
               AkronModule.Settings.SafeModeFreezeBestRun;
    }

    private static object GetAreaModeStats(Level level) {
        AreaKey area = level.Session.Area;
        if (SaveData.Instance == null ||
            area.ID < 0 ||
            area.ID >= SaveData.Instance.Areas.Count) {
            return null;
        }

        AreaStats stats = SaveData.Instance.Areas[area.ID];
        int mode = (int) area.Mode;
        return mode >= 0 && mode < stats.Modes.Length ? stats.Modes[mode] : null;
    }

    private static object GetMember(object target, string name) {
        if (target == null) {
            return null;
        }

        Type type = target.GetType();
        return type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target) ??
               type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    }

    private static bool TryGetMemberValue(object target, string name, out object value) {
        value = GetMember(target, name);
        return value != null;
    }

    private static void SetMember(object target, string name, object value) {
        if (target == null) {
            return;
        }

        Type type = target.GetType();
        PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.CanWrite == true) {
            property.SetValue(target, value);
            return;
        }

        FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        field?.SetValue(target, value);
    }

    private static bool? GetVisible(object target) {
        if (target == null) {
            return null;
        }

        object value = GetMember(target, "Visible");
        return value is bool visible ? visible : null;
    }

    private static void SetVisible(object target, bool visible) {
        SetMember(target, "Visible", visible);
    }
}

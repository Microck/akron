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
    private static bool vanillaHudHidden;
    private static Player freeCameraLockedPlayer;
    private static bool freeCameraPlayerStateCaptured;
    private static int previousFreeCameraPlayerState;
    private static readonly Dictionary<Entity, bool> HiddenPauseMenuVisibility = new Dictionary<Entity, bool>();
    private static readonly Dictionary<object, bool> HiddenPauseHudVisibility = new Dictionary<object, bool>();
    private static readonly Dictionary<object, bool> HiddenHudVisibility = new Dictionary<object, bool>();
    private static readonly Dictionary<string, object> SafeModeStatSnapshot = new Dictionary<string, object>();
    private static string safeModeSnapshotArea = string.Empty;
    private static Level visualTuningLevel;
    private static string visualTuningRoom = string.Empty;
    private static object visualTuningLighting;
    private static object visualTuningBloom;
    private static float? visualTuningLightingAlpha;
    private static float? visualTuningBloomBase;
    private static float? visualTuningBloomStrength;

    public static void Reset() {
        RestorePauseMenuVisibility();
        RestoreHudVisibility();
        RestoreFreeCameraPlayerControl();
        RestoreVisualTuning();
        SetAudioPitch(1f);
        SafeModeStatSnapshot.Clear();
        safeModeSnapshotArea = string.Empty;
    }

    public static void Apply(Level level, Player player) {
        ApplyAudioSpeedAndPitch();
        ApplyHudVisibility(level);
        ApplyPauseMenuVisibility(level);
        ApplyScreenshake(level);
        ApplyVisualTuning(level);
        ApplyFreeCamera(level, player);
        ApplySafeModeStatFreeze(level);
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

        return AkronModule.Settings.FpsBypass ? ResolveCurrentFrameBypassRates().Describe() : "Off";
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
            AkronModule.Settings.FpsBypass,
            AkronModule.Settings.FpsBypassTarget,
            AkronModule.Settings.TpsBypass,
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
        return level != null &&
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
               AkronPolicy.CanUse(AkronFeatureKind.PauseMenuVisibility).Allowed;
    }

    public static bool ShouldSuppressPauseBackgroundFade(Scene scene) {
        return scene is Level &&
               AkronModule.IsPauseCountdownActive &&
               AkronModule.Settings.PauseCountdownHidePauseTint;
    }

    public static void HoldSceneClockForFreeCameraFreeze(Level level) {
        if (level == null) {
            return;
        }

        // Scene.BeforeUpdate runs before Level.Update and advances these clocks
        // even when this hook skips the actual level update. Restore that frame's
        // increment so "Freeze gameplay" also freezes timers and time-driven scene effects.
        if (!level.Paused) {
            level.TimeActive = Math.Max(0f, level.TimeActive - Engine.DeltaTime);
        }
        level.RawTimeActive = Math.Max(0f, level.RawTimeActive - Engine.RawDeltaTime);
    }

    private static void ApplyAudioSpeedAndPitch() {
        float speed = 1f;
        if (AkronModule.Settings.AudioSpeed && AkronModule.TryUse(AkronFeatureKind.AudioSpeed)) {
            speed = AkronModule.Settings.AudioSpeedPolicy switch {
                AkronAudioSpeedPolicy.SyncTimescale => AkronModule.Session?.TimescaleEnabled == true ? AkronModule.Session.TimescaleMultiplier : 1f,
                AkronAudioSpeedPolicy.Independent => AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.AudioSpeedMultiplier),
                _ => 1f
            };
        }

        float pitch = 1f;
        if (AkronModule.Settings.PitchShift && AkronModule.TryUse(AkronFeatureKind.PitchShift)) {
            pitch = AkronModule.Settings.PitchShiftPolicy switch {
                AkronPitchPolicy.FollowSpeed => speed,
                AkronPitchPolicy.Independent => AkronModuleSettings.ClampAudioMultiplier(AkronModule.Settings.PitchShiftMultiplier),
                _ => 1f
            };
        }

        SetAudioPitch(Calc.Clamp(speed * pitch, 0.1f, 4f));
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

    private static void ApplyHudVisibility(Level level) {
        if (level == null) {
            return;
        }

        if (!AkronModule.Settings.HideVanillaHud) {
            RestoreHudVisibility();
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.HudVisibility)) {
            return;
        }

        vanillaHudHidden = true;
        SetHudRendererVisibility(level, false, HiddenHudVisibility);
    }

    private static void ApplyPauseMenuVisibility(Level level) {
        if (level == null ||
            !AkronModule.Settings.HidePauseMenu ||
            !level.Paused) {
            RestorePauseMenuVisibility();
            return;
        }

        if (!AkronPolicy.CanUse(AkronFeatureKind.PauseMenuVisibility).Allowed) {
            RestorePauseMenuVisibility();
            return;
        }

        foreach (Entity entity in level.Entities) {
            if (entity is not TextMenu menu) {
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

    private static void RestorePauseMenuVisibility() {
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

    private static void ApplyScreenshake(Level level) {
        if (level == null) {
            return;
        }

        bool disabled = AkronModule.Settings.Screenshake &&
                        AkronModuleSettings.ClampScreenshakeIntensity(AkronModule.Settings.ScreenshakeIntensity) <= 0 &&
                        AkronModule.TryUse(AkronFeatureKind.Screenshake);
        SetMember(level, "DisableScreenShake", disabled);
        if (disabled) {
            SetMember(level, "ShakeVector", Vector2.Zero);
        }
    }

    private static void ApplyVisualTuning(Level level) {
        if (level == null || !ShouldApplyVisualTuning()) {
            RestoreVisualTuning();
            return;
        }

        CaptureVisualTuningBaseline(level);

        if (AkronModule.Settings.LightLevel) {
            object lighting = GetMember(level, "Lighting");
            if (lighting != null) {
                float light = AkronModuleSettings.ClampLightLevelPercent(AkronModule.Settings.LightLevelPercent) / 100f;
                SetMember(lighting, "Alpha", Calc.Clamp(1f - light, 0f, 1f));
            }
        }

        if (AkronModule.Settings.BloomLevel) {
            object bloom = GetMember(level, "Bloom");
            if (bloom != null) {
                float amount = AkronModuleSettings.ClampBloomLevelPercent(AkronModule.Settings.BloomLevelPercent) / 100f;
                SetMember(bloom, "Base", Calc.Clamp(Math.Min(amount, 1f), 0f, 1f));
                SetMember(bloom, "Strength", Math.Max(1f, amount));
            }
        }
    }

    private static bool ShouldApplyVisualTuning() {
        if (!AkronModule.Settings.LightLevel && !AkronModule.Settings.BloomLevel && !AkronModule.Settings.ScreenTint) {
            return false;
        }

        return AkronModule.TryUse(AkronFeatureKind.VisualTuning);
    }

    private static void CaptureVisualTuningBaseline(Level level) {
        string room = level.Session?.Level ?? string.Empty;
        if (ReferenceEquals(visualTuningLevel, level) && string.Equals(visualTuningRoom, room, StringComparison.Ordinal)) {
            return;
        }

        RestoreVisualTuning();
        visualTuningLevel = level;
        visualTuningRoom = room;
        visualTuningLighting = GetMember(level, "Lighting");
        visualTuningBloom = GetMember(level, "Bloom");

        if (TryGetMemberValue(visualTuningLighting, "Alpha", out object alpha) && alpha is float lightingAlpha) {
            visualTuningLightingAlpha = lightingAlpha;
        }

        if (TryGetMemberValue(visualTuningBloom, "Base", out object bloomBase) && bloomBase is float baseValue) {
            visualTuningBloomBase = baseValue;
        }

        if (TryGetMemberValue(visualTuningBloom, "Strength", out object bloomStrength) && bloomStrength is float strengthValue) {
            visualTuningBloomStrength = strengthValue;
        }
    }

    private static void RestoreVisualTuning() {
        if (visualTuningLighting != null && visualTuningLightingAlpha.HasValue) {
            SetMember(visualTuningLighting, "Alpha", visualTuningLightingAlpha.Value);
        }

        if (visualTuningBloom != null && visualTuningBloomBase.HasValue) {
            SetMember(visualTuningBloom, "Base", visualTuningBloomBase.Value);
        }

        if (visualTuningBloom != null && visualTuningBloomStrength.HasValue) {
            SetMember(visualTuningBloom, "Strength", visualTuningBloomStrength.Value);
        }

        visualTuningLevel = null;
        visualTuningRoom = string.Empty;
        visualTuningLighting = null;
        visualTuningBloom = null;
        visualTuningLightingAlpha = null;
        visualTuningBloomBase = null;
        visualTuningBloomStrength = null;
    }

    private static void RestoreHudVisibility() {
        if (!vanillaHudHidden) {
            return;
        }

        foreach ((object renderer, bool visible) in HiddenHudVisibility) {
            SetVisible(renderer, visible);
        }

        HiddenHudVisibility.Clear();
        vanillaHudHidden = false;
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

    private static void ApplyFreeCamera(Level level, Player player) {
        if (level == null || !AkronModule.IsFreeCameraEffectiveEnabled()) {
            RestoreFreeCameraPlayerControl();
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.FreeCamera)) {
            RestoreFreeCameraPlayerControl();
            return;
        }

        LockFreeCameraPlayerControl(player);
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
            !AnySafeModeStatFreezeEnabled()) {
            SafeModeStatSnapshot.Clear();
            safeModeSnapshotArea = string.Empty;
            return;
        }

        if (!AkronModule.TryUse(AkronFeatureKind.SafeModeStats)) {
            return;
        }

        string areaKey = level.Session.Area.GetSID() + ":" + level.Session.Area.Mode;
        object modeStats = GetAreaModeStats(level);
        if (modeStats == null) {
            return;
        }

        if (safeModeSnapshotArea != areaKey) {
            SafeModeStatSnapshot.Clear();
            safeModeSnapshotArea = areaKey;
            CaptureStat(modeStats, "Deaths");
            CaptureStat(modeStats, "Jumps");
            CaptureStat(modeStats, "BestTime");
            CaptureStat(modeStats, "BestFullClearTime");
            return;
        }

        if (AkronModule.Settings.SafeModeFreezeAttempts) {
            RestoreStat(modeStats, "Deaths");
        }
        if (AkronModule.Settings.SafeModeFreezeJumps) {
            RestoreStat(modeStats, "Jumps");
        }
        if (AkronModule.Settings.SafeModeFreezeBestRun) {
            RestoreStat(modeStats, "BestTime");
            RestoreStat(modeStats, "BestFullClearTime");
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

    private static void CaptureStat(object target, string name) {
        if (TryGetMemberValue(target, name, out object value)) {
            SafeModeStatSnapshot[name] = value;
        }
    }

    private static void RestoreStat(object target, string name) {
        if (SafeModeStatSnapshot.TryGetValue(name, out object value)) {
            SetMember(target, name, value);
        }
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

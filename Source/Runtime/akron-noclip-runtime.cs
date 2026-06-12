using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static void ApplyNoclip(Player player) {
        Vector2 aim = Input.Aim.Value;
        UpdateNoclipDepth(player);
        ApplyPlayerVisibilityOverride(player);
        if (aim == Vector2.Zero) {
            player.Speed = Vector2.Zero;
            TrackHazardAccuracy(player);
            UpdateNoclipCamera(player);
            return;
        }

        // Noclip owns player movement while active. NaiveMove keeps Monocle's
        // exact-position bookkeeping coherent while intentionally bypassing
        // collision resolution, unlike directly mutating Position before the
        // vanilla physics update runs.
        float speed = Input.Grab.Check
            ? Math.Max(1, Settings.NoclipFloatSpeed)
            : Math.Max(1, Settings.NoclipSpeed);
        player.Speed = Vector2.Zero;
        player.NaiveMove(aim.SafeNormalize() * speed * Engine.DeltaTime);
        TrackHazardAccuracy(player);
        UpdateNoclipCamera(player);
    }

    private static void TrackHazardAccuracy(Player player) {
        if (!Settings.NoclipAccuracy ||
            !AkronPolicy.CanUse(AkronFeatureKind.HazardAccuracy).Allowed ||
            player == null ||
            player.Scene == null) {
            noclipAccuracyInvalidLastFrame = false;
            return;
        }

        bool invalid = IsNoclipAccuracyInvalid(player);
        bool moved = hazardAccuracyHasLastPosition &&
                     Vector2.DistanceSquared(player.Position, hazardAccuracyLastPosition) > 0.0001f;
        hazardAccuracyLastPosition = player.Position;
        hazardAccuracyHasLastPosition = true;

        if (!AkronModuleSettings.ShouldCountHazardAccuracySample(invalid, moved)) {
            noclipAccuracyInvalidLastFrame = false;
            return;
        }

        noclipAccuracySamples++;
        if (invalid) {
            noclipAccuracyInvalidSamples++;
            if (!noclipAccuracyInvalidLastFrame) {
                RecordHazardAccuracyInvalidEntry();
            }

            if (Settings.NoclipAccuracyTintMode == AkronNoclipAccuracyTintMode.WhileTouching) {
                TriggerNoclipAccuracyTint();
            }
        }

        noclipAccuracyInvalidLastFrame = invalid;
    }

    private static bool IsNoclipAccuracyInvalid(Player player) {
        // Standing on normal collision, especially JumpThru platforms, can
        // overlap the player hitbox by design. Hazard Accuracy should only
        // sample actual death-hazard contact; the Player.Die hook covers the
        // broader hazard set while this keeps "while touching" tint responsive
        // for vanilla spikes.
        return player.CollideCheck<Spikes>() ||
               IsPlayerTouchingBottomKillbox(player);
    }

    internal static bool IsPlayerTouchingBottomKillbox(Player player) {
        return player?.Scene is Level level &&
               IsPlayerTouchingBottomKillbox(player.Top, level.Bounds.Bottom);
    }

    internal static bool IsPlayerTouchingBottomKillbox(float playerTop, int levelBottom) {
        return playerTop > levelBottom;
    }

    internal static bool IsPlayerPastBottomKillboxRescueBoundary(Player player) {
        return player?.Scene is Level level &&
               IsPlayerPastBottomKillboxRescueBoundary(player.Top, level.Bounds.Bottom);
    }

    internal static bool IsPlayerPastBottomKillboxRescueBoundary(float playerTop, int levelBottom) {
        return playerTop > levelBottom + 64f;
    }

    internal static bool ShouldRecordBottomKillboxHazardAccuracyBeforeRescue(bool hazardAccuracyAllowed, bool touchingBottomKillbox) {
        return hazardAccuracyAllowed && touchingBottomKillbox;
    }

    private static bool IsHazardAccuracyAllowed() {
        return Settings.NoclipAccuracy && AkronPolicy.CanUse(AkronFeatureKind.HazardAccuracy).Allowed;
    }

    private static void RecordHazardAccuracyInvalidContact(Player player) {
        if (!Settings.NoclipAccuracy) {
            return;
        }

        noclipAccuracySamples++;
        noclipAccuracyInvalidSamples++;
        if (!noclipAccuracyInvalidLastFrame) {
            RecordHazardAccuracyInvalidEntry();
        }
        if (Settings.NoclipAccuracyTintMode == AkronNoclipAccuracyTintMode.WhileTouching) {
            TriggerNoclipAccuracyTint();
        }
        noclipAccuracyInvalidLastFrame = true;
        if (player != null) {
            hazardAccuracyLastPosition = player.Position;
            hazardAccuracyHasLastPosition = true;
        }
    }

    private static void RecordHazardAccuracyInvalidEntry() {
        noclipAccuracyInvalidEntries++;
        if (Settings.NoclipAccuracyTintMode == AkronNoclipAccuracyTintMode.OnInvalidEntry) {
            TriggerNoclipAccuracyTint();
        }
        ShowNoclipAccuracyLimitToastIfNeeded();
    }

    private static void ShowNoclipAccuracyLimitToastIfNeeded() {
        int limit = Settings.NoclipAccuracyInvalidLimit;
        if (limit <= 0 || noclipAccuracyLimitToastShown || noclipAccuracyInvalidEntries < limit) {
            return;
        }

        noclipAccuracyLimitToastShown = true;
        Engine.Scene?.Add(new AkronToast("Hazard accuracy invalid limit reached."));
    }

    private static void TriggerNoclipAccuracyTint() {
        if (!Settings.NoclipAccuracyTint) {
            return;
        }

        noclipAccuracyTintTimer = AkronModuleSettings.ClampNoclipAccuracyTintDurationMs(Settings.NoclipAccuracyTintDurationMs) / 1000f;
    }

    private static void UpdateNoclipAccuracyTintTimer() {
        if (noclipAccuracyTintTimer <= 0f) {
            return;
        }

        noclipAccuracyTintTimer = Math.Max(0f, noclipAccuracyTintTimer - Math.Max(0f, Engine.RawDeltaTime));
    }

    private static void RenderNoclipAccuracyTint() {
        if (!Settings.NoclipAccuracy || !Settings.NoclipAccuracyTint || noclipAccuracyTintTimer <= 0f) {
            return;
        }

        float duration = AkronModuleSettings.ClampNoclipAccuracyTintDurationMs(Settings.NoclipAccuracyTintDurationMs) / 1000f;
        float fade = duration <= 0f ? 1f : Calc.Clamp(noclipAccuracyTintTimer / duration, 0f, 1f);
        float opacity = AkronModuleSettings.ClampOpacity(Settings.NoclipAccuracyTintOpacity) / 100f;
        Color color = ColorFromRgb(Settings.NoclipAccuracyTintColor) * (opacity * fade);
        Draw.Rect(0f, 0f, Engine.Width, Engine.Height, color);
    }

    public static AkronNoclipAccuracySnapshot GetNoclipAccuracySnapshot() {
        return new AkronNoclipAccuracySnapshot(
            noclipAccuracySamples,
            noclipAccuracyInvalidSamples,
            noclipAccuracyInvalidEntries,
            Instance?._Settings is AkronModuleSettings settings ? settings.NoclipAccuracyInvalidLimit : 0,
            noclipAccuracyInvalidLastFrame);
    }

    public static void ResetNoclipAccuracy() {
        noclipAccuracySamples = 0;
        noclipAccuracyInvalidSamples = 0;
        noclipAccuracyInvalidEntries = 0;
        noclipAccuracyInvalidLastFrame = false;
        noclipAccuracyLimitToastShown = false;
        noclipAccuracyTintTimer = 0f;
        hazardAccuracyHasLastPosition = false;
        hazardAccuracyLastPosition = Vector2.Zero;
    }

    private static void UpdateNoclipCamera(Player player) {
        if (player.Scene is not Level level) {
            return;
        }

        Vector2 from = level.Camera.Position;
        Vector2 target = player.CameraTarget;
        level.Camera.Position = from + (target - from) * (1f - (float) Math.Pow(0.01f, Engine.DeltaTime));
    }

    private static void UpdateNoclipDepth(Player player) {
        if (!Settings.NoclipDrawOnTop) {
            RestoreNoclipDepth();
            return;
        }

        if (noclipDepthPlayer != player) {
            RestoreNoclipDepth();
            noclipDepthPlayer = player;
            previousNoclipDepth = player.Depth;
        }

        player.Depth = Depths.Top;
    }

    private static void RestoreNoclipDepth() {
        if (noclipDepthPlayer == null) {
            return;
        }

        if (noclipDepthPlayer.Scene != null) {
            noclipDepthPlayer.Depth = previousNoclipDepth;
        }

        noclipDepthPlayer = null;
    }

    private static void RestoreNoclipDepth(Player player) {
        if (noclipDepthPlayer == player) {
            RestoreNoclipDepth();
        }
    }

    private static void ApplyPlayerVisibilityOverride(Player player) {
        bool shouldHide = Settings.HidePlayer && AkronPolicy.CanUse(AkronFeatureKind.HidePlayer).Allowed ||
                          Settings.Noclip && Settings.NoclipHidePlayer;
        if (!shouldHide) {
            RestorePlayerVisibilityOverride();
            return;
        }

        if (noclipVisibilityPlayer != player) {
            RestorePlayerVisibilityOverride();
            noclipVisibilityPlayer = player;
            previousNoclipVisible = player.Visible;
        }

        player.Visible = false;
    }

    private static void RestorePlayerVisibilityOverride() {
        if (noclipVisibilityPlayer == null) {
            return;
        }

        if (noclipVisibilityPlayer.Scene != null) {
            noclipVisibilityPlayer.Visible = previousNoclipVisible;
        }

        noclipVisibilityPlayer = null;
    }

    private static void RestorePlayerVisibilityOverride(Player player) {
        if (noclipVisibilityPlayer == player) {
            RestorePlayerVisibilityOverride();
        }
    }
}

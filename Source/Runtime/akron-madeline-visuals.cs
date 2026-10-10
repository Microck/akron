using System;
using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static Player madelineVisualPlayer;
    private static Color? previousMadelineHairColor;
    private static int? previousMadelineHairCount;
    private static readonly Dictionary<Sprite, Color> PreviousCrownColors = new();

    internal static void RestoreMadelineVisualOverrides() {
        if (madelineVisualPlayer?.Hair != null && previousMadelineHairColor.HasValue) {
            madelineVisualPlayer.Hair.Color = previousMadelineHairColor.Value;
        }
        if (madelineVisualPlayer?.Sprite != null && previousMadelineHairCount.HasValue) {
            madelineVisualPlayer.Sprite.HairCount = previousMadelineHairCount.Value;
        }
        foreach (KeyValuePair<Sprite, Color> entry in PreviousCrownColors) {
            entry.Key.SetColor(entry.Value);
        }
        PreviousCrownColors.Clear();
        madelineVisualPlayer = null;
        previousMadelineHairColor = null;
        previousMadelineHairCount = null;
    }

    private static bool TryRenderMadelineSyncedDeathEffect(Player player) {
        if (!ShouldSyncMadelineEffect(Settings.MadelineDeathEffectSync) ||
            player?.StateMachine?.State != 14 ||
            global::Celeste.SaveData.Instance?.Assists.InvisibleMotion == true ||
            !TryUseRuntime(AkronFeatureKind.MadelineEffectSync) ||
            !TryResolveMadelineSyncColor(player, out Color color)) {
            return false;
        }

        DeathEffect.Draw(player.Center + player.deadOffset, color, player.introEase);
        return true;
    }

    private static bool TryRunMadelineSyncedDashParticles(On.Celeste.Player.orig_DashBegin orig, Player player) {
        if (!ShouldSyncMadelineEffect(Settings.MadelineDashParticleSync) ||
            !TryUseRuntime(AkronFeatureKind.MadelineEffectSync) ||
            !TryResolveMadelineSyncColor(player, out Color color)) {
            return false;
        }

        ParticleType previousDashA = Player.P_DashA;
        ParticleType previousDashB = Player.P_DashB;
        ParticleType previousDashBadB = Player.P_DashBadB;
        try {
            ParticleType synced = new ParticleType(previousDashA) {
                Color = color,
                Color2 = color
            };
            Player.P_DashA = synced;
            Player.P_DashB = synced;
            Player.P_DashBadB = synced;
            orig(player);
        } finally {
            Player.P_DashA = previousDashA;
            Player.P_DashB = previousDashB;
            Player.P_DashBadB = previousDashBadB;
        }

        return true;
    }

    private static Color ResolveForcedTrailColor(Player player) {
        if (!Settings.CustomTrail) {
            return player.Hair?.Color ?? Player.NormalHairColor;
        }

        if (ShouldSyncMadelineEffect(Settings.MadelineDashTrailSync) &&
            TryUseRuntime(AkronFeatureKind.MadelineEffectSync) &&
            TryResolveMadelineSyncColor(player, out Color syncedColor)) {
            return syncedColor;
        }

        return ResolveCustomTrailColor(player);
    }

    private static void ApplyMadelineVisualOverrides(Player player) {
        madelineVisualPlayer = player;
        ApplyMadelineColors(player);
        ApplyMadelineHairLength(player);
        ApplyMadelineCrownSync(player);
    }

    private static void ApplyMadelineColors(Player player) {
        if (player == null || !Settings.MadelineColors || !TryUseRuntime(AkronFeatureKind.MadelineColors)) {
            return;
        }

        if (player.StateMachine?.State == 19 &&
            Settings.MadelineEffectSync &&
            Settings.MadelineFeatherColorSync == AkronMadelineEffectSyncMode.Off) {
            return;
        }

        // Do not use Player.OverrideHairColor for this feature. That field
        // flattens every player state to one color, while this option is meant
        // to mirror Hyperline/LiquidMod-style dash-state coloring.
        if (player.OverrideHairColor.HasValue) {
            player.OverrideHairColor = null;
        }

        if (TryResolveMadelineConfiguredColor(player, out Color color)) {
            previousMadelineHairColor = player.Hair.Color;
            player.Hair.Color = color;
        }
    }

    private static void ApplyMadelineHairLength(Player player) {
        if (player?.Sprite == null ||
            !Settings.MadelineHairLength ||
            !TryUseRuntime(AkronFeatureKind.MadelineHairLength)) {
            return;
        }

        // Keep vanilla special-state hair counts such as ducking and feather.
        // This option is only meant to adjust normal dash-state hair length.
        if (player.StateMachine?.State == 5 || player.StateMachine?.State == 19) {
            return;
        }

        previousMadelineHairCount = player.Sprite.HairCount;
        player.Sprite.HairCount = ResolveMadelineHairLength(player.Dashes, player.MaxDashes);
    }

    public static int ResolveMadelineHairLength(int dashes, int maxDashes) {
        if (IsMadelineNoDashColorState(dashes, maxDashes)) {
            return AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineNoDashHairLength);
        }

        if (dashes >= 5) {
            return AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineFiveDashHairLength);
        }

        return dashes switch {
            4 => AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineFourDashHairLength),
            3 => AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineThreeDashHairLength),
            2 => AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineTwoDashHairLength),
            _ => AkronModuleSettings.ClampMadelineHairLength(Settings.MadelineOneDashHairLength)
        };
    }

    private static void ApplyMadelineCrownSync(Player player) {
        if (!ShouldSyncMadelineEffect(Settings.MadelineCrownColorSync) ||
            !TryUseRuntime(AkronFeatureKind.MadelineEffectSync) ||
            !TryResolveMadelineSyncColor(player, out Color color)) {
            return;
        }

        foreach (Sprite sprite in player.Components.GetAll<Sprite>()) {
            if (sprite?.Animations?.ContainsKey("crown") == true) {
                PreviousCrownColors[sprite] = sprite.Color;
                sprite.SetColor(color);
            }
        }
    }

    private static bool ShouldSyncMadelineEffect(AkronMadelineEffectSyncMode mode) {
        return Settings.MadelineEffectSync &&
               AkronModuleSettings.NormalizeMadelineEffectSyncMode(mode) == AkronMadelineEffectSyncMode.MatchHair;
    }

    private static bool TryResolveMadelineSyncColor(Player player, out Color color) {
        color = Player.NormalHairColor;
        if (player == null) {
            return false;
        }

        if (Settings.MadelineColors && TryUseRuntime(AkronFeatureKind.MadelineColors) && TryResolveMadelineConfiguredColor(player, out color)) {
            return true;
        }

        if (player.Hair != null) {
            color = player.Hair.Color;
            return true;
        }

        return true;
    }

    private static bool TryResolveMadelineConfiguredColor(Player player, out Color color) {
        color = Player.NormalHairColor;
        int rgb;
        if (IsMadelineNoDashColorState(player.Dashes, player.MaxDashes) && Settings.MadelineColorNoDash) {
            rgb = Settings.MadelineNoDashColor;
        } else if (player.Dashes >= 5 && Settings.MadelineColorFiveDash) {
            rgb = Settings.MadelineFiveDashColor;
        } else if (player.Dashes >= 4 && Settings.MadelineColorFourDash) {
            rgb = Settings.MadelineFourDashColor;
        } else if (player.Dashes >= 3 && Settings.MadelineColorThreeDash) {
            rgb = Settings.MadelineThreeDashColor;
        } else if (player.Dashes >= 2 && Settings.MadelineColorTwoDash) {
            rgb = Settings.MadelineTwoDashColor;
        } else if (player.Dashes == 1 && Settings.MadelineColorOneDash) {
            rgb = Settings.MadelineOneDashColor;
        } else {
            return false;
        }

        color = ColorFromRgb(rgb);
        if (Settings.MadelineColorGradient) {
            float speed = AkronModuleSettings.ClampMadelineGradientSpeed(Settings.MadelineColorGradientSpeed);
            float phase = 0.5f + 0.5f * (float) Math.Sin((player.Scene?.TimeActive ?? 0f) * speed * MathHelper.TwoPi);
            color = Color.Lerp(ColorFromRgb(Settings.MadelineGradientColorA), ColorFromRgb(Settings.MadelineGradientColorB), phase);
        }

        return true;
    }
}

using System;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private const float VanillaDeathParticleDurationSeconds = 0.834f;
    private static bool customDeathParticleRenderWarningLogged;
    private static int customDeathParticleRenderHookCalls;
    private static int customDeathParticleDrawHookCalls;
    private static int customDeathParticleRenderAttempts;
    private static float customDeathParticleLastEase;
    private static float customDeathParticleLastVisualEase;
    private static AkronDeathParticleShape customDeathParticleLastShape;
    private static Color customDeathParticleLastFill;

    private static void DeathEffectOnRender(On.Celeste.DeathEffect.orig_Render orig, DeathEffect self) {
        customDeathParticleRenderHookCalls++;
        if (ShouldUseCustomDeathParticles() &&
            self?.Entity != null &&
            TryRenderCustomDeathParticles(self.Entity.Position + self.Position, self.Color, self.Percent)) {
            return;
        }

        orig(self);
    }

    private static void DeathEffectOnDraw(On.Celeste.DeathEffect.orig_Draw orig, Vector2 position, Color color, float ease) {
        customDeathParticleDrawHookCalls++;
        if (!ShouldUseCustomDeathParticles() || !TryRenderCustomDeathParticles(position, color, ease)) {
            orig(position, color, ease);
        }
    }

    private static bool ShouldUseCustomDeathParticles() {
        return Settings.CustomDeathParticles &&
               !Settings.NoDeathEffect &&
               AkronPolicy.CanUse(AkronFeatureKind.DeathVisuals).Allowed;
    }

    private static bool TryRenderCustomDeathParticles(Vector2 position, Color color, float ease) {
        try {
            customDeathParticleRenderAttempts++;
            RenderCustomDeathParticles(position, color, ease);
            return true;
        } catch (Exception exception) {
            if (!customDeathParticleRenderWarningLogged) {
                customDeathParticleRenderWarningLogged = true;
                Logger.Log(LogLevel.Warn, nameof(AkronModule), "Custom death particle rendering failed; falling back to vanilla: " + exception);
            }

            return false;
        }
    }

    private static void RenderCustomDeathParticles(Vector2 position, Color vanillaColor, float ease) {
        float visualEase = ResolveDeathParticleVisualEase(ease);
        Color primary = AkronModuleSettings.NormalizeDeathParticleColorMode(Settings.DeathParticleColorMode) == AkronDeathParticleColorMode.Custom
            ? ColorFromRgb(Settings.DeathParticleColor)
            : vanillaColor;
        Color flash = ColorFromRgb(Settings.DeathParticleFlashColor);
        Color outline = ColorFromRgb(Settings.DeathParticleOutlineColor);
        Color fill = Math.Floor(visualEase * 10f) % 2.0 == 0.0 ? primary : flash;
        AkronDeathParticleShape shape = AkronModuleSettings.NormalizeDeathParticleShape(Settings.DeathParticleShape);
        customDeathParticleLastEase = ease;
        customDeathParticleLastVisualEase = visualEase;
        customDeathParticleLastShape = shape;
        customDeathParticleLastFill = fill;
        float scale = visualEase < 0.5f
            ? 0.5f + visualEase
            : Ease.CubeOut(1f - (visualEase - 0.5f) * 2f);
        float radius = Ease.CubeOut(visualEase) * 24f;

        for (int index = 0; index < 8; index++) {
            Vector2 offset = Calc.AngleToVector((index / 8f + visualEase * 0.25f) * MathHelper.TwoPi, radius);
            Vector2 center = position + offset;
            if (shape == AkronDeathParticleShape.Vanilla) {
                DrawVanillaDeathParticle(center, fill, outline, scale);
            } else {
                DrawMaskedDeathParticle(center, ResolveDeathParticleMask(shape), fill, outline, Math.Max(0.75f, scale));
            }
        }
    }

    internal static float ResolveDeathParticleVisualEase(float vanillaEase) {
        return ResolveDeathParticleVisualEase(vanillaEase, Settings.DeathParticleDurationSeconds);
    }

    internal static float ResolveDeathParticleVisualEase(float vanillaEase, float durationSeconds) {
        float duration = AkronModuleSettings.ClampDeathParticleDurationSeconds(durationSeconds);
        return Math.Max(0f, Math.Min(1f, vanillaEase * VanillaDeathParticleDurationSeconds / duration));
    }

    private static void DrawVanillaDeathParticle(Vector2 center, Color fill, Color outline, float scale) {
        MTexture texture = GFX.Game["characters/player/hair00"];
        Vector2 size = new Vector2(scale, scale);
        texture.DrawCentered(center + new Vector2(-1f, 0f), outline, size);
        texture.DrawCentered(center + new Vector2(1f, 0f), outline, size);
        texture.DrawCentered(center + new Vector2(0f, -1f), outline, size);
        texture.DrawCentered(center + new Vector2(0f, 1f), outline, size);
        texture.DrawCentered(center, fill, size);
    }

    private static void DrawMaskedDeathParticle(Vector2 center, string mask, Color fill, Color outline, float pixelSize) {
        DrawMaskedDeathParticleLayer(center + new Vector2(-1f, 0f), mask, outline, pixelSize);
        DrawMaskedDeathParticleLayer(center + new Vector2(1f, 0f), mask, outline, pixelSize);
        DrawMaskedDeathParticleLayer(center + new Vector2(0f, -1f), mask, outline, pixelSize);
        DrawMaskedDeathParticleLayer(center + new Vector2(0f, 1f), mask, outline, pixelSize);
        DrawMaskedDeathParticleLayer(center, mask, fill, pixelSize);
    }

    private static void DrawMaskedDeathParticleLayer(Vector2 center, string mask, Color color, float pixelSize) {
        float left = center.X - AkronModuleSettings.DeathParticleCanvasSize * pixelSize * 0.5f;
        float top = center.Y - AkronModuleSettings.DeathParticleCanvasSize * pixelSize * 0.5f;
        for (int index = 0; index < AkronModuleSettings.DeathParticleCanvasCells; index++) {
            if (mask[index] != '1') {
                continue;
            }

            int x = index % AkronModuleSettings.DeathParticleCanvasSize;
            int y = index / AkronModuleSettings.DeathParticleCanvasSize;
            Draw.Pixel.Draw(
                new Vector2(left + x * pixelSize, top + y * pixelSize),
                Vector2.Zero,
                color,
                new Vector2(pixelSize, pixelSize));
        }
    }

    internal static string ResolveDeathParticleMask(AkronDeathParticleShape shape) {
        return AkronModuleSettings.NormalizeDeathParticleShape(shape) switch {
            AkronDeathParticleShape.Circle => "0011110001111110111111111111111111111111111111110111111000111100",
            AkronDeathParticleShape.Square => "0000000001111110011111100111111001111110011111100111111000000000",
            AkronDeathParticleShape.Diamond => "0001100000111100011111101111111111111111011111100011110000011000",
            AkronDeathParticleShape.Plus => "0001100000011000011111100111111000011000000110000001100000000000",
            AkronDeathParticleShape.Star => "0001100010011000011111100011110001111110000110010010010000000000",
            AkronDeathParticleShape.Custom => AkronModuleSettings.NormalizeDeathParticleCustomShape(Settings.DeathParticleCustomShape),
            _ => AkronModuleSettings.DefaultDeathParticleCustomShape
        };
    }

    internal static string DescribeDeathParticleRenderTelemetry() {
        return "render-hooks=" + customDeathParticleRenderHookCalls +
               "; draw-hooks=" + customDeathParticleDrawHookCalls +
               "; attempts=" + customDeathParticleRenderAttempts +
               "; last-ease=" + customDeathParticleLastEase.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
               "; last-visual-ease=" + customDeathParticleLastVisualEase.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) +
               "; last-shape=" + customDeathParticleLastShape +
               "; last-fill=#" + customDeathParticleLastFill.R.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) +
               customDeathParticleLastFill.G.ToString("X2", System.Globalization.CultureInfo.InvariantCulture) +
               customDeathParticleLastFill.B.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
    }
}

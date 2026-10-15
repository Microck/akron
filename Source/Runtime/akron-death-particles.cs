using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Celeste;
using Microsoft.Xna.Framework;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private const float VanillaDeathParticleDurationSeconds = 0.834f;
    private static ConditionalWeakTable<DeathEffect, CustomDeathParticleBurst> customDeathBursts = new();
    private static readonly List<CustomDeathParticleBurst> activeCustomDeathBursts = new();
    private static int vanillaDeathParticleDrawDepth;
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
        if (self != null && customDeathBursts.TryGetValue(self, out var burst)) {
            // Keep the native component and callbacks alive. Disabling the custom
            // feature or a renderer failure restores its vanilla rendering.
            if (!ShouldUseCustomDeathParticles() || burst.RenderFailed) {
                vanillaDeathParticleDrawDepth++;
                try {
                    orig(self);
                } finally {
                    vanillaDeathParticleDrawDepth--;
                }
            }
            return;
        }
        if (ShouldUseCustomDeathParticles() &&
            self?.Entity != null &&
            TryRenderCustomDeathParticles(self.Entity.Position + self.Position, self.Color, self.Percent)) {
            return;
        }

        orig(self);
    }

    private static void DeathEffectOnDraw(On.Celeste.DeathEffect.orig_Draw orig, Vector2 position, Color color, float ease) {
        customDeathParticleDrawHookCalls++;
        if (vanillaDeathParticleDrawDepth > 0 || !ShouldUseCustomDeathParticles() || !TryRenderCustomDeathParticles(position, color, ease)) {
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
        RenderDeathParticleAppearance(position, CaptureDeathParticleAppearance(vanillaColor), visualEase, ease);
    }

    private readonly record struct DeathParticleAppearance(Color Primary, Color Flash, Color Outline,
        AkronDeathParticleShape Shape, string Mask);

    private static DeathParticleAppearance CaptureDeathParticleAppearance(Color vanillaColor) {
        Color primary = AkronModuleSettings.NormalizeDeathParticleColorMode(Settings.DeathParticleColorMode) == AkronDeathParticleColorMode.Custom
            ? ColorFromRgb(Settings.DeathParticleColor) : vanillaColor;
        AkronDeathParticleShape shape = AkronModuleSettings.NormalizeDeathParticleShape(Settings.DeathParticleShape);
        return new DeathParticleAppearance(primary, ColorFromRgb(Settings.DeathParticleFlashColor),
            ColorFromRgb(Settings.DeathParticleOutlineColor), shape, ResolveDeathParticleMask(shape));
    }

    private static void RenderDeathParticleAppearance(Vector2 position, DeathParticleAppearance appearance, float visualEase, float ease) {
        if (visualEase >= 1f) {
            return;
        }
        Color primary = appearance.Primary;
        Color flash = appearance.Flash;
        Color outline = appearance.Outline;
        Color fill = Math.Floor(visualEase * 10f) % 2.0 == 0.0 ? primary : flash;
        AkronDeathParticleShape shape = appearance.Shape;
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
                DrawMaskedDeathParticle(center, appearance.Mask, fill, outline, Math.Max(0.75f, scale));
            }
        }
    }

    private static void EnsureCustomDeathParticleBurst(PlayerDeadBody body) {
        if (!ShouldUseCustomDeathParticles() || body?.Scene is not Level level ||
            PlayerDeadBodyDeathEffectField?.GetValue(body) is not DeathEffect effect ||
            customDeathBursts.TryGetValue(effect, out _)) {
            return;
        }

        var burst = new CustomDeathParticleBurst(level, body.Position + effect.Position,
            CaptureDeathParticleAppearance(effect.Color), Settings.DeathParticleDurationSeconds,
            Math.Max(0f, effect.Percent * effect.Duration), body.Depth);
        customDeathBursts.Add(effect, burst);
        activeCustomDeathBursts.Add(burst);
        level.Add(burst);
    }

    private static void ClearCustomDeathParticleBursts() {
        foreach (var burst in activeCustomDeathBursts.ToArray()) {
            burst.Cancel();
        }
        // External savestate systems can restore cloned entities not present in
        // the live registry. Clear those through the current scene as well.
        if (Engine.Scene is Level level) {
            foreach (var burst in level.Entities.OfType<CustomDeathParticleBurst>().ToArray()) {
                burst.Cancel();
            }
        }
        activeCustomDeathBursts.Clear();
        customDeathBursts = new ConditionalWeakTable<DeathEffect, CustomDeathParticleBurst>();
    }

    internal static float ResolveDeathParticleLifetimeProgress(float age, float duration) {
        return Math.Max(0f, Math.Min(1f, age / AkronModuleSettings.ClampDeathParticleDurationSeconds(duration)));
    }

    private sealed class CustomDeathParticleBurst : Entity {
        private readonly Level owner;
        private readonly Session session;
        private readonly string room;
        private readonly DeathParticleAppearance appearance;
        private readonly float duration;
        private float age;
        private bool cancelled;
        internal bool RenderFailed { get; private set; }

        internal CustomDeathParticleBurst(Level level, Vector2 position, DeathParticleAppearance appearance,
            float duration, float initialAge, int depth) : base(position) {
            owner = level;
            session = level.Session;
            room = session.Level;
            this.appearance = appearance;
            this.duration = AkronModuleSettings.ClampDeathParticleDurationSeconds(duration);
            age = initialAge;
            Depth = depth;
            // Global survives the same Level's death reload; it is not a HUD overlay
            // and remains behind the native wipe. Transition updates allow cleanup.
            Tag = Tags.Global | Tags.TransitionUpdate | Tags.PauseUpdate;
            AkronSaveLoadService.IgnoreSaveState(this);
        }

        private bool IsValid => !cancelled && Scene == owner && owner.Session == session &&
            session.Level == room && !owner.Transitioning && ShouldUseCustomDeathParticles();

        internal void Cancel() {
            cancelled = true;
            Visible = false;
            RemoveSelf();
            activeCustomDeathBursts.Remove(this);
        }

        public override void Update() {
            base.Update();
            if (!IsValid) {
                Cancel();
                return;
            }
            // RetryPlayerCorpse updates PauseUpdate entities, while genuine
            // pause/freeze must retain the same gameplay-time age.
            if (owner.FrozenOrPaused) {
                return;
            }
            age += Math.Max(0f, Engine.DeltaTime);
            if (ResolveDeathParticleLifetimeProgress(age, duration) >= 1f) {
                Cancel();
            }
        }

        public override void Render() {
            if (!IsValid) {
                return;
            }
            try {
                RenderDeathParticleAppearance(Position, appearance,
                    ResolveDeathParticleLifetimeProgress(age, duration), age / VanillaDeathParticleDurationSeconds);
            } catch (Exception exception) {
                if (!customDeathParticleRenderWarningLogged) {
                    customDeathParticleRenderWarningLogged = true;
                    Logger.Log(LogLevel.Warn, nameof(AkronModule), "Custom death burst rendering failed: " + exception);
                }
                RenderFailed = true;
                Cancel();
            }
        }

        public override void Added(Scene scene) {
            base.Added(scene);
            // Save-state exclusion can temporarily remove and re-add this entity.
            if (!cancelled && !activeCustomDeathBursts.Contains(this)) {
                activeCustomDeathBursts.Add(this);
            }
        }

        public override void Removed(Scene scene) {
            activeCustomDeathBursts.Remove(this);
            base.Removed(scene);
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

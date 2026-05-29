using System.Collections.Generic;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static void BackdropRendererOnRender(On.Celeste.BackdropRenderer.orig_Render orig, BackdropRenderer self, Scene scene) {
        if (!ShouldSuppressBackdropVisuals()) {
            orig(self, scene);
            return;
        }

        List<bool> oldVisible = new List<bool>(self.Backdrops.Count);
        foreach (Backdrop backdrop in self.Backdrops) {
            oldVisible.Add(backdrop.Visible);
            if (ShouldHideBackdrop(backdrop)) {
                backdrop.Visible = false;
            }
        }

        try {
            orig(self, scene);
        } finally {
            for (int index = 0; index < oldVisible.Count && index < self.Backdrops.Count; index++) {
                self.Backdrops[index].Visible = oldVisible[index];
            }
        }
    }

    private static bool ShouldSuppressBackdropVisuals() {
        return AkronPolicy.CanUse(AkronFeatureKind.ReducedVisualNoise).Allowed &&
               (Settings.HideSnow || Settings.HideWindSnow);
    }

    private static bool ShouldHideBackdrop(Backdrop backdrop) {
        return Settings.HideSnow && backdrop is Snow ||
               Settings.HideWindSnow && backdrop is WindSnowFG;
    }

    private static void WaterFallOnRender(On.Celeste.WaterFall.orig_Render orig, WaterFall self) {
        if (!ShouldHideWaterfalls()) {
            orig(self);
        }
    }

    private static void WaterFallOnRenderDisplacement(On.Celeste.WaterFall.orig_RenderDisplacement orig, WaterFall self) {
        if (!ShouldHideWaterfalls()) {
            orig(self);
        }
    }

    private static void WaterFallOnUpdate(On.Celeste.WaterFall.orig_Update orig, WaterFall self) {
        Water oldWater = self.water;
        if (ShouldHideWaterfalls()) {
            self.water = null;
        }

        orig(self);

        if (ShouldHideWaterfalls()) {
            self.water = oldWater;
        }
    }

    private static void BigWaterfallOnRender(On.Celeste.BigWaterfall.orig_Render orig, BigWaterfall self) {
        if (!ShouldHideWaterfalls()) {
            orig(self);
        }
    }

    private static void BigWaterfallOnRenderDisplacement(On.Celeste.BigWaterfall.orig_RenderDisplacement orig, BigWaterfall self) {
        if (!ShouldHideWaterfalls()) {
            orig(self);
        }
    }

    private static bool ShouldHideWaterfalls() {
        return Settings.HideWaterfalls && AkronPolicy.CanUse(AkronFeatureKind.ReducedVisualNoise).Allowed;
    }

    private static void ReflectionTentaclesOnRender(On.Celeste.ReflectionTentacles.orig_Render orig, ReflectionTentacles self) {
        if (!Settings.HideTentacles || !AkronPolicy.CanUse(AkronFeatureKind.ReducedVisualNoise).Allowed) {
            orig(self);
        }
    }

    private static void HeatWaveOnRenderDisplacement(On.Celeste.HeatWave.orig_RenderDisplacement orig, HeatWave self, Level level) {
        if (!Settings.HideHeatDistortion || !AkronPolicy.CanUse(AkronFeatureKind.ReducedVisualNoise).Allowed) {
            orig(self, level);
        }
    }

    private static void ApplyReducedVisualNoise(Level level) {
        if (Settings.ReducedVisualNoise || Settings.NoGlitch) {
            Glitch.Value = 0f;
        }

        if (Settings.ReducedVisualNoise || Settings.NoAnxiety) {
            Distort.Anxiety = 0f;
        }

        if (Settings.ReducedVisualNoise || Settings.NoDistortion) {
            Distort.GameRate = 1f;
        }

        if (Settings.ReducedVisualNoise || Settings.NoParticles) {
            level.Particles.Clear();
            level.ParticlesBG.Clear();
            level.ParticlesFG.Clear();
        }

        if (Settings.ReducedVisualNoise || Settings.NoTrails) {
            TrailManager.Clear();
        }
    }

    private static bool ShouldApplyAnyVisualNoiseSuppression() {
        return Settings.ReducedVisualNoise ||
               Settings.NoParticles ||
               Settings.NoTrails ||
               Settings.NoGlitch ||
               Settings.NoAnxiety ||
               Settings.NoDistortion;
    }
}

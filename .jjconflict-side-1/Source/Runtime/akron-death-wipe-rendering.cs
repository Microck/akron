using System;
using System.Linq;
using Celeste;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private static Action deferredScreenWipeAction;
    private static bool deathWipeRenderSuppressionActive;
    private static bool deathWipeRenderSuppressionAwaitingWipeIn;
    private static bool deathWipeRenderSuppressionHasDrawnPrimitives;
    private static int deathWipeRenderSuppressionFallbackFrames;
    private static ulong deathWipeRenderSuppressionLastUpdateFrame;

    private static void AreaDataOnDoScreenWipe(On.Celeste.AreaData.orig_DoScreenWipe orig, AreaData self, Scene scene, bool wipeIn, Action onComplete) {
        bool suppressVanillaWipe = ShouldSuppressScreenWipe(scene, wipeIn);
        bool shouldHideAkronSurfaces = !suppressVanillaWipe && ShouldRouteAkronBehindDeathWipe(scene, wipeIn);
        if (!suppressVanillaWipe) {
            if (shouldHideAkronSurfaces) {
                BeginDeathWipeRenderSuppression(wipeIn);
                orig(self, scene, wipeIn, () => {
                    try {
                        onComplete?.Invoke();
                    } finally {
                        CompleteDeathWipeRenderSuppressionStage(wipeIn);
                    }
                });
                return;
            }

            orig(self, scene, wipeIn, onComplete);
            return;
        }

        ClearDeathWipeRenderSuppression();
        if (Settings.NoDeathWipeRunCallbacks) {
            deferredScreenWipeAction = onComplete;
        }
    }

    private static void RunDeferredScreenWipeAction() {
        Action action = deferredScreenWipeAction;
        if (action == null) {
            return;
        }

        deferredScreenWipeAction = null;
        action();
    }

    private static bool ShouldSuppressScreenWipe(Scene scene, bool wipeIn) {
        if (!Settings.NoDeathWipe || !AkronPolicy.CanUse(AkronFeatureKind.DeathVisuals).Allowed) {
            return false;
        }

        if (Settings.NoDeathWipeMode == AkronNoDeathWipeMode.AllWipes) {
            return true;
        }

        if (wipeIn) {
            return false;
        }

        if (scene is not Level level) {
            return false;
        }

        Player player = level.Entities.OfType<Player>().FirstOrDefault();
        return player?.Dead == true ||
               level.Entities.OfType<PlayerDeadBody>().Any();
    }

    private static bool ShouldRouteAkronBehindDeathWipe(Scene scene, bool wipeIn) {
        if (wipeIn) {
            return deathWipeRenderSuppressionAwaitingWipeIn;
        }

        if (scene is not Level level) {
            return false;
        }

        Player player = level.Entities.OfType<Player>().FirstOrDefault();
        return player?.Dead == true ||
               level.Entities.OfType<PlayerDeadBody>().Any();
    }

    private static void BeginDeathWipeRenderSuppression(bool wipeIn) {
        bool continuingDeathWipe = wipeIn && deathWipeRenderSuppressionAwaitingWipeIn;
        deathWipeRenderSuppressionActive = true;
        deathWipeRenderSuppressionFallbackFrames = 360;
        if (!continuingDeathWipe) {
            deathWipeRenderSuppressionHasDrawnPrimitives = false;
        }

        if (!wipeIn) {
            deathWipeRenderSuppressionAwaitingWipeIn = true;
        }
    }

    private static void CompleteDeathWipeRenderSuppressionStage(bool wipeIn) {
        if (wipeIn || !deathWipeRenderSuppressionAwaitingWipeIn) {
            ClearDeathWipeRenderSuppression();
            return;
        }

        // The death wipe is a two-stage visual: wipe out to black, then wipe in
        // after the respawn scene is ready. Keep Akron surfaces hidden through the
        // black handoff so they never draw above either half of the transition.
        deathWipeRenderSuppressionHasDrawnPrimitives = true;
        deathWipeRenderSuppressionFallbackFrames = 360;
    }

    private static void UpdateDeathWipeRenderSuppression() {
        if (!deathWipeRenderSuppressionActive) {
            return;
        }

        if (deathWipeRenderSuppressionLastUpdateFrame == Engine.FrameCounter) {
            return;
        }

        deathWipeRenderSuppressionLastUpdateFrame = Engine.FrameCounter;
        deathWipeRenderSuppressionFallbackFrames--;
        if (deathWipeRenderSuppressionFallbackFrames <= 0) {
            ClearDeathWipeRenderSuppression();
        }
    }

    private static void ClearDeathWipeRenderSuppression() {
        deathWipeRenderSuppressionActive = false;
        deathWipeRenderSuppressionAwaitingWipeIn = false;
        deathWipeRenderSuppressionHasDrawnPrimitives = false;
        deathWipeRenderSuppressionFallbackFrames = 0;
        deathWipeRenderSuppressionLastUpdateFrame = 0;
    }

    public static bool ShouldHideAkronRenderSurfacesBehindDeathWipe() {
        return deathWipeRenderSuppressionActive && deathWipeRenderSuppressionHasDrawnPrimitives;
    }

    private static void ScreenWipeOnDrawPrimitives(On.Celeste.ScreenWipe.orig_DrawPrimitives orig, VertexPositionColor[] vertices) {
        if (deathWipeRenderSuppressionActive && Engine.Scene is Level level) {
            // Do not hide Akron labels on the death frame before the wipe is
            // actually present. Once primitives begin drawing, route the HUD
            // immediately before the transition geometry so only the covered
            // pixels disappear. The normal post-Level HUD pass stays suppressed
            // so labels cannot leak on top of full-screen transition frames.
            deathWipeRenderSuppressionHasDrawnPrimitives = true;
            RenderAkronLevelHud(level, ignoreDeathWipeSuppression: true);
        }

        orig(vertices);
    }
}

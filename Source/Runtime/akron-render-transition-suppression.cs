using System;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModule {
    private const int StateTransitionRenderSuppressionFrames = 12;
    private static int stateTransitionRenderSuppressionFrames;
    private static ulong stateTransitionRenderSuppressionLastUpdateFrame;

    internal static void SuppressAkronRenderSurfacesAfterStateTransition() {
        stateTransitionRenderSuppressionFrames = Math.Max(
            stateTransitionRenderSuppressionFrames,
            StateTransitionRenderSuppressionFrames);
    }

    private static void UpdateStateTransitionRenderSuppression() {
        if (stateTransitionRenderSuppressionFrames <= 0) {
            return;
        }

        if (stateTransitionRenderSuppressionLastUpdateFrame == Engine.FrameCounter) {
            return;
        }

        stateTransitionRenderSuppressionLastUpdateFrame = Engine.FrameCounter;
        stateTransitionRenderSuppressionFrames--;
        if (stateTransitionRenderSuppressionFrames <= 0) {
            ClearStateTransitionRenderSuppression();
        }
    }

    private static void ClearStateTransitionRenderSuppression() {
        stateTransitionRenderSuppressionFrames = 0;
        stateTransitionRenderSuppressionLastUpdateFrame = 0;
    }

    internal static bool ShouldHideAkronRenderSurfaces() {
        return ShouldHideAkronRenderSurfacesBehindDeathWipe() ||
               stateTransitionRenderSuppressionFrames > 0;
    }

    internal static bool ShouldHideAkronRenderSurfacesAfterStateTransition() {
        return stateTransitionRenderSuppressionFrames > 0;
    }
}

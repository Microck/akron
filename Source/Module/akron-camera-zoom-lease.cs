using System;
using Microsoft.Xna.Framework.Graphics;

namespace Celeste.Mod.Akron;

// A foreign camera force owns a temporary viewport until its original scene is released.
internal sealed class AkronCameraZoomLease {
    private object owner;
    private Viewport originalViewport;
    private bool cameraResetCompleted;
    internal bool HasOwner => owner != null;
    internal bool IsOwner(object scene) => owner != null && ReferenceEquals(owner, scene);

    internal void Capture(object scene, Viewport viewport) {
        if (owner != null) {
            if (!ReferenceEquals(owner, scene)) throw new InvalidOperationException("Release the previous camera owner first.");
            // A new force after a failed automatic-mode restore needs another camera reset.
            cameraResetCompleted = false;
            return;
        }
        owner = scene ?? throw new ArgumentNullException(nameof(scene));
        originalViewport = viewport;
        cameraResetCompleted = false;
    }

    internal bool TryRelease(object currentScene, Action<Viewport> restoreViewport,
        Action resetZoom, Func<bool> restoreAutomatic) {
        if (owner == null) return true;
        if (ReferenceEquals(owner, currentScene) && !cameraResetCompleted) {
            // Active ECD's native ResetZoom hook may replace this with current map-evaluated state.
            restoreViewport(originalViewport);
            resetZoom();
            cameraResetCompleted = true;
        }
        if (!restoreAutomatic()) return false;
        owner = null;
        cameraResetCompleted = false;
        return true;
    }
}

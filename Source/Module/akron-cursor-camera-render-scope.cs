using System;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

// Render-only translation: no offset survives into the next camera update.
internal sealed class AkronCursorCameraRenderScope : IDisposable {
    private Action restore;
    internal AkronCursorCameraRenderScope(Action apply, Action restore) {
        this.restore = restore;
        try { apply(); } catch { Dispose(); throw; }
    }
    public void Dispose() {
        Action action = restore;
        restore = null;
        action?.Invoke();
    }
    internal static Vector2 Offset(Vector2 focus, float zoom) {
        Vector2 result = default;
        if (!float.IsFinite(zoom) || zoom <= 1f) return result;
        result.X = focus.X - 160f;
        result.Y = focus.Y - 90f;
        return result;
    }
    internal static Vector2 Project(Vector2 world, Vector2 camera, float zoom) {
        Vector2 result = default;
        result.X = (world.X - camera.X) * zoom;
        result.Y = (world.Y - camera.Y) * zoom;
        return result;
    }
    internal static Vector2 Unproject(Vector2 screen, Vector2 camera, float zoom) {
        Vector2 result = default;
        result.X = camera.X + screen.X / zoom;
        result.Y = camera.Y + screen.Y / zoom;
        return result;
    }
}

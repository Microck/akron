using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

// Blurs the finished 320x180 gameplay buffer while the overlay is open, so the menu sits on a
// softened room instead of live pixels. It runs at the point in Level.Render where Celeste has
// just unbound GameplayBuffers.Level and is about to draw it to the screen, so the color grade,
// zoom, HUD, and wipe on top are untouched and nothing about the rendered game changes.
//
// The blur is a linear-sampled downsample and upsample through two small targets, repeated as
// the amount rises. No shader is needed and the work is a handful of 160x90 draws, which is why
// it can run every frame the overlay is visible.
internal static class AkronOverlayBlur {
    private static RenderTarget2D halfTarget;
    private static RenderTarget2D quarterTarget;

    public static void ApplyToLevelBuffer(Level level) {
        int blur = AkronModuleSettings.ClampOverlayBlur(AkronModule.Settings.OverlayBlur);
        RenderTarget2D source = GameplayBuffers.Level?.Target;
        if (blur <= 0 || source == null || level == null || !AkronModule.IsOverlayVisible || AkronCapture.IsCapturingGameFrame) {
            return;
        }

        GraphicsDevice device = source.GraphicsDevice;
        EnsureTargets(device, source.Width, source.Height);

        // 1 to 3 rounds; each round is a full down-and-up pass, so the amount picks strength.
        int rounds = 1 + blur / 34;
        for (int round = 0; round < rounds; round++) {
            Downsample(device, source, halfTarget);
            Downsample(device, halfTarget, quarterTarget);
            Downsample(device, quarterTarget, halfTarget);
            Downsample(device, halfTarget, source);
        }

        // Level.Render expects the backbuffer bound here; this is the state it left before the hook ran.
        device.SetRenderTarget(null);
    }

    public static void Unload() {
        halfTarget?.Dispose();
        quarterTarget?.Dispose();
        halfTarget = null;
        quarterTarget = null;
    }

    private static void EnsureTargets(GraphicsDevice device, int width, int height) {
        int halfWidth = System.Math.Max(1, width / 2);
        int halfHeight = System.Math.Max(1, height / 2);
        if (halfTarget == null || halfTarget.IsDisposed || halfTarget.Width != halfWidth || halfTarget.Height != halfHeight) {
            halfTarget?.Dispose();
            halfTarget = new RenderTarget2D(device, halfWidth, halfHeight, false, SurfaceFormat.Color, DepthFormat.None);
        }

        int quarterWidth = System.Math.Max(1, width / 4);
        int quarterHeight = System.Math.Max(1, height / 4);
        if (quarterTarget == null || quarterTarget.IsDisposed || quarterTarget.Width != quarterWidth || quarterTarget.Height != quarterHeight) {
            quarterTarget?.Dispose();
            quarterTarget = new RenderTarget2D(device, quarterWidth, quarterHeight, false, SurfaceFormat.Color, DepthFormat.None);
        }
    }

    // Draws source stretched over the whole destination with linear sampling. Going down
    // averages neighbouring pixels; coming back up spreads that average, which is the blur.
    private static void Downsample(GraphicsDevice device, Texture2D source, RenderTarget2D destination) {
        device.SetRenderTarget(destination);
        Draw.SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        Draw.SpriteBatch.Draw(source, new Rectangle(0, 0, destination.Width, destination.Height), Color.White);
        Draw.SpriteBatch.End();
    }
}

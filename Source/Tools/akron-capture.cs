using System;
using System.IO;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

public static class AkronCapture {
    private static string pendingPath = string.Empty;
    private static bool pendingScannerOverlays;

    internal static bool IsCapturingGameFrame { get; private set; }
    internal static bool IsCapturingScannerOverlays { get; private set; }

    public static string Capture(Level level) {
        if (string.IsNullOrWhiteSpace(pendingPath)) {
            return string.Empty;
        }

        string outputPath = pendingPath;
        bool scannerOverlays = pendingScannerOverlays;
        pendingPath = string.Empty;
        pendingScannerOverlays = false;

        GraphicsDevice graphicsDevice = Engine.Instance.GraphicsDevice;
        Viewport captureViewport = Engine.Viewport;
        Viewport originalViewport = graphicsDevice.Viewport;
        Color[] pixels;
        Texture2D texture = null;
        Texture2D scaledTexture = null;

        try {
            pixels = AkronModule.Settings.ScreenshotScannerRemoveBackground
                ? CaptureTransparentBackground(level, graphicsDevice, captureViewport, scannerOverlays)
                : CaptureFrame(level, graphicsDevice, captureViewport, scannerOverlays, level.BackgroundColor);

            texture = new Texture2D(graphicsDevice, captureViewport.Width, captureViewport.Height, false, SurfaceFormat.Color);
            texture.SetData(pixels);

            int scale = AkronModuleSettings.ClampScreenshotScale(AkronModule.Settings.ScreenshotScale);
            using FileStream stream = File.Create(outputPath);
            if (scale <= 1) {
                SaveTexture(stream, texture, captureViewport.Width, captureViewport.Height);
            } else {
                int scaledWidth = captureViewport.Width * scale;
                int scaledHeight = captureViewport.Height * scale;
                Color[] scaledPixels = ScalePoint(pixels, captureViewport.Width, captureViewport.Height, scale);
                scaledTexture = new Texture2D(graphicsDevice, scaledWidth, scaledHeight, false, SurfaceFormat.Color);
                scaledTexture.SetData(scaledPixels);
                SaveTexture(stream, scaledTexture, scaledWidth, scaledHeight);
            }
        } finally {
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Viewport = originalViewport;
            IsCapturingGameFrame = false;
            IsCapturingScannerOverlays = false;
            texture?.Dispose();
            scaledTexture?.Dispose();
            try {
                Draw.SpriteBatch.End();
            } catch {
            }
        }

        AkronModule.Session.LastScreenshotPath = outputPath;
        return outputPath;
    }

    private static Color[] CaptureTransparentBackground(Level level, GraphicsDevice graphicsDevice, Viewport captureViewport, bool scannerOverlays) {
        Color previousBackgroundColor = level.BackgroundColor;
        float previousBloomStrength = level.Bloom?.Strength ?? 0f;

        try {
            // ScreenshotTool infers alpha by rendering the same frame over white
            // and black backgrounds. Dropping bloom from the white pass avoids
            // counting the same glow twice when the two passes are combined.
            if (level.Bloom != null) {
                level.Bloom.Strength = 0f;
            }
            Color[] whitePixels = CaptureFrame(level, graphicsDevice, captureViewport, scannerOverlays, Color.White);
            if (level.Bloom != null) {
                level.Bloom.Strength = previousBloomStrength;
            }
            Color[] blackPixels = CaptureFrame(level, graphicsDevice, captureViewport, scannerOverlays, Color.Black);
            return InferTransparentPixels(whitePixels, blackPixels);
        } finally {
            level.BackgroundColor = previousBackgroundColor;
            if (level.Bloom != null) {
                level.Bloom.Strength = previousBloomStrength;
            }
        }
    }

    private static Color[] CaptureFrame(Level level, GraphicsDevice graphicsDevice, Viewport captureViewport, bool scannerOverlays, Color backgroundColor) {
        Color previousBackgroundColor = level.BackgroundColor;
        SpeedrunType previousSpeedrunClock = global::Celeste.Settings.Instance.SpeedrunClock;
        object speedrunToolRoomTimerState = null;
        Color[] pixels = new Color[captureViewport.Width * captureViewport.Height];
        try {
            level.BackgroundColor = backgroundColor;
            IsCapturingGameFrame = true;
            IsCapturingScannerOverlays = scannerOverlays;
            AkronModule.EnsureCaptureSuppressionHooks();
            global::Celeste.Settings.Instance.SpeedrunClock = SpeedrunType.Off;
            speedrunToolRoomTimerState = AkronSpeedrunToolBroker.SuppressRoomTimerHudForCapture();
            try {
                level.BeforeRender();
                graphicsDevice.Viewport = captureViewport;
                graphicsDevice.SetRenderTarget(null);
                graphicsDevice.Clear(backgroundColor);
                level.Render();
                level.AfterRender();
            } finally {
                AkronSpeedrunToolBroker.RestoreRoomTimerHudAfterCapture(speedrunToolRoomTimerState);
                global::Celeste.Settings.Instance.SpeedrunClock = previousSpeedrunClock;
                IsCapturingGameFrame = false;
                IsCapturingScannerOverlays = false;
            }
            graphicsDevice.GetBackBufferData(captureViewport.Bounds, pixels, 0, pixels.Length);
            return pixels;
        } finally {
            level.BackgroundColor = previousBackgroundColor;
        }
    }

    internal static Color[] InferTransparentPixels(Color[] whitePixels, Color[] blackPixels) {
        Color[] pixels = new Color[blackPixels.Length];
        for (int i = 0; i < pixels.Length; i++) {
            UnpackColor(whitePixels[i], out int whiteR, out _, out _, out _);
            UnpackColor(blackPixels[i], out int blackR, out int blackG, out int blackB, out _);
            (int r, int g, int b, int a) = InferTransparentChannels(whiteR, blackR, blackG, blackB);
            pixels[i] = new Color(r, g, b, a);
        }

        return pixels;
    }

    internal static (int R, int G, int B, int A) InferTransparentChannels(int whiteR, int blackR, int blackG, int blackB) {
        int alpha = ClampByte(255 - (whiteR - blackR));
        if (alpha < 1) {
            return (0, 0, 0, 0);
        }

        float multiplier = 255f / alpha;
        return (
            ClampByte((int) (blackR * multiplier)),
            ClampByte((int) (blackG * multiplier)),
            ClampByte((int) (blackB * multiplier)),
            alpha);
    }

    private static int ClampByte(int value) {
        if (value < 0) {
            return 0;
        }

        return value > 255 ? 255 : value;
    }

    private static void UnpackColor(Color color, out int r, out int g, out int b, out int a) {
        r = color.R;
        g = color.G;
        b = color.B;
        a = color.A;
    }

    public static string CaptureToPath(Level level, string outputPath) {
        return CaptureToPath(level, outputPath, includeScannerOverlays: false);
    }

    public static string CaptureToPath(Level level, string outputPath, bool includeScannerOverlays) {
        string previous = pendingPath;
        bool previousScannerOverlays = pendingScannerOverlays;
        pendingPath = outputPath;
        pendingScannerOverlays = includeScannerOverlays;
        try {
            return Capture(level);
        } finally {
            if (pendingPath == outputPath) {
                pendingPath = string.Empty;
                pendingScannerOverlays = false;
            }
            if (!string.IsNullOrWhiteSpace(previous)) {
                pendingPath = previous;
                pendingScannerOverlays = previousScannerOverlays;
            }
        }
    }

    private static void SaveTexture(Stream stream, Texture2D texture, int width, int height) {
        if (AkronModuleSettings.NormalizeScreenshotScannerImageFormat(AkronModule.Settings.ScreenshotScannerImageFormat) == AkronScreenshotImageFormat.Jpeg) {
            texture.SaveAsJpeg(stream, width, height);
            return;
        }

        texture.SaveAsPng(stream, width, height);
    }

    private static Color[] ScalePoint(Color[] source, int width, int height, int scale) {
        Color[] scaled = new Color[width * height * scale * scale];
        int scaledWidth = width * scale;
        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                Color color = source[y * width + x];
                int targetY = y * scale;
                int targetX = x * scale;
                for (int yy = 0; yy < scale; yy++) {
                    int row = (targetY + yy) * scaledWidth + targetX;
                    for (int xx = 0; xx < scale; xx++) {
                        scaled[row + xx] = color;
                    }
                }
            }
        }

        return scaled;
    }
}

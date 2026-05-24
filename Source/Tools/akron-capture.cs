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
        Color[] pixels = new Color[captureViewport.Width * captureViewport.Height];
        Texture2D texture = null;
        Texture2D scaledTexture = null;

        try {
            level.BeforeRender();
            graphicsDevice.Viewport = captureViewport;
            graphicsDevice.SetRenderTarget(null);
            graphicsDevice.Clear(Engine.ClearColor);
            IsCapturingGameFrame = true;
            IsCapturingScannerOverlays = scannerOverlays;
            try {
                level.Render();
            } finally {
                IsCapturingGameFrame = false;
                IsCapturingScannerOverlays = false;
            }
            level.AfterRender();
            graphicsDevice.GetBackBufferData(captureViewport.Bounds, pixels, 0, pixels.Length);

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

using System;
using System.IO;
using StbImageWriteSharp;

namespace Celeste.Mod.Akron;

// Avoid FNA3D's RGB24 surface pitch assumption at non-four-aligned widths.
internal static class AkronJpegEncoder {
    internal static void Write(Stream stream, byte[] rgba, int width, int height) {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(rgba);
        if (!stream.CanWrite) throw new ArgumentException("The JPEG destination must be writable.", nameof(stream));
        if (width <= 0 || height <= 0 || width > ushort.MaxValue || height > ushort.MaxValue || (long) width * height > AkronCapture.MaxCapturePixels)
            throw new ArgumentOutOfRangeException(nameof(width), "JPEG dimensions exceed the capture bounds.");
        if (rgba.Length != checked(width * height * 4))
            throw new ArgumentException("JPEG pixels must be tightly packed RGBA.", nameof(rgba));
        int quality = 90;
        if (int.TryParse(Environment.GetEnvironmentVariable("FNA_GRAPHICS_JPEG_SAVE_QUALITY"), out int configured))
            quality = configured == 0 ? 90 : Math.Clamp(configured, 1, 100);
        // ImageWriter carries stream state, so never share it between saves.
        new ImageWriter().WriteJpg(rgba, width, height, ColorComponents.RedGreenBlueAlpha, stream, quality);
    }
}

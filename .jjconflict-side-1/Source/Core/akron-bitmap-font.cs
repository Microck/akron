using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

internal static class AkronBitmapFont {
    private const float SourcePixelSize = 18f;
    private const float SourceBaselineOffset = 0f;
    private const string AtlasResourceSuffix = "poppins-imgui-18.png";
    private const string MetricsResourceSuffix = "poppins-imgui-18.csv";

    private static readonly Dictionary<char, Glyph> Glyphs = new Dictionary<char, Glyph>();
    private static Texture2D atlas;
    private static float lineHeight = 32f;
    private static bool loadAttempted;

    public static bool Available {
        get {
            EnsureLoaded();
            return atlas != null && Glyphs.Count > 0;
        }
    }

    public static Vector2 Measure(string text, float pixelSize) {
        EnsureLoaded();
        if (atlas == null || string.IsNullOrEmpty(text)) {
            return Vector2.Zero;
        }

        float scale = pixelSize / SourcePixelSize;
        float width = 0f;
        float currentLineWidth = 0f;
        int lineCount = 1;
        foreach (char character in text) {
            if (character == '\n') {
                width = Math.Max(width, currentLineWidth);
                currentLineWidth = 0f;
                lineCount++;
                continue;
            }

            if (TryGetGlyph(character, out Glyph glyph)) {
                currentLineWidth += glyph.Advance * scale;
            }
        }

        width = Math.Max(width, currentLineWidth);
        return new Vector2(width, lineCount * lineHeight * scale);
    }

    public static void Draw(string text, Vector2 position, float pixelSize, Color color) {
        EnsureLoaded();
        if (atlas == null || string.IsNullOrEmpty(text)) {
            return;
        }

        float scale = pixelSize / SourcePixelSize;
        Vector2 cursor = position;
        foreach (char character in text) {
            if (character == '\n') {
                cursor.X = position.X;
                cursor.Y += lineHeight * scale;
                continue;
            }

            if (!TryGetGlyph(character, out Glyph glyph)) {
                continue;
            }

            if (glyph.Source.Width > 0 && glyph.Source.Height > 0) {
                Monocle.Draw.SpriteBatch.Draw(
                    atlas,
                    new Vector2(cursor.X + glyph.XOffset * scale, cursor.Y + (SourceBaselineOffset + glyph.YOffset) * scale),
                    glyph.Source,
                    color,
                    0f,
                    Vector2.Zero,
                    scale,
                    SpriteEffects.None,
                    0f);
            }
            cursor.X += glyph.Advance * scale;
        }
    }

    private static bool TryGetGlyph(char character, out Glyph glyph) {
        if (Glyphs.TryGetValue(character, out glyph)) {
            return true;
        }

        return Glyphs.TryGetValue('?', out glyph);
    }

    private static void EnsureLoaded() {
        if (loadAttempted) {
            return;
        }

        loadAttempted = true;
        Assembly assembly = typeof(AkronBitmapFont).Assembly;
        string atlasResource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(AtlasResourceSuffix, StringComparison.OrdinalIgnoreCase));
        string metricsResource = assembly.GetManifestResourceNames().FirstOrDefault(name => name.EndsWith(MetricsResourceSuffix, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(atlasResource) || string.IsNullOrWhiteSpace(metricsResource)) {
            return;
        }

        using Stream atlasStream = assembly.GetManifestResourceStream(atlasResource);
        using Stream metricsStream = assembly.GetManifestResourceStream(metricsResource);
        if (atlasStream == null || metricsStream == null || Engine.Graphics?.GraphicsDevice == null) {
            return;
        }

        atlas = Texture2D.FromStream(Engine.Graphics.GraphicsDevice, atlasStream);
        PremultiplyAtlasAlpha(atlas);
        using StreamReader reader = new StreamReader(metricsStream);
        while (!reader.EndOfStream) {
            string line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length == 2 && string.Equals(parts[0], "lineHeight", StringComparison.OrdinalIgnoreCase)) {
                lineHeight = float.Parse(parts[1], CultureInfo.InvariantCulture);
                continue;
            }

            if (parts.Length != 8) {
                continue;
            }

            char character = (char) int.Parse(parts[0], CultureInfo.InvariantCulture);
            Rectangle source = new Rectangle(
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture),
                int.Parse(parts[3], CultureInfo.InvariantCulture),
                int.Parse(parts[4], CultureInfo.InvariantCulture));
            float advance = float.Parse(parts[5], CultureInfo.InvariantCulture);
            float xOffset = float.Parse(parts[6], CultureInfo.InvariantCulture);
            float yOffset = float.Parse(parts[7], CultureInfo.InvariantCulture);
            Glyphs[character] = new Glyph(source, advance, xOffset, yOffset);
        }
    }

    private static void PremultiplyAtlasAlpha(Texture2D texture) {
        Color[] pixels = new Color[texture.Width * texture.Height];
        texture.GetData(pixels);

        for (int index = 0; index < pixels.Length; index++) {
            Color pixel = pixels[index];
            if (pixel.A == 0) {
                pixels[index] = Color.Transparent;
                continue;
            }

            pixels[index] = new Color(
                (byte) (pixel.R * pixel.A / 255),
                (byte) (pixel.G * pixel.A / 255),
                (byte) (pixel.B * pixel.A / 255),
                pixel.A);
        }

        texture.SetData(pixels);
    }

    private readonly struct Glyph {
        public Glyph(Rectangle source, float advance, float xOffset, float yOffset) {
            Source = source;
            Advance = advance;
            XOffset = xOffset;
            YOffset = yOffset;
        }

        public Rectangle Source { get; }
        public float Advance { get; }
        public float XOffset { get; }
        public float YOffset { get; }
    }
}

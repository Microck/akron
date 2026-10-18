using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class MapAlphaBlendTests {
    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(254)]
    public void SameColorOverlapPreservesSaturatedChannels(int alpha) {
        foreach (var color in new[] { new Color(255, 0, 0, alpha), new Color(0, 0, 255, alpha), new Color(255, 255, 255, alpha), new Color(254, 69, 0, alpha) }) {
            byte[] row = { 77, color.R, color.G, color.B, color.A, 88 };
            AkronScreenshotScanner.BlendOverRowPixel(row, 1, color);
            Assert.Equal(color.R, row[1]); Assert.Equal(color.G, row[2]); Assert.Equal(color.B, row[3]);
            Assert.Equal((byte)Math.Floor((alpha / 255d + alpha / 255d * (1 - alpha / 255d)) * 255), row[4]);
            Assert.Equal(77, row[0]); Assert.Equal(88, row[5]);
        }
    }

    [Fact]
    public void ContrastingAndBoundaryColorsMatchIndependentFloatingSourceOver() {
        foreach (int sa in new[] { 0, 1, 40, 64, 128, 254, 255 })
        foreach (int da in new[] { 0, 1, 40, 64, 128, 254, 255 }) {
            Color source = new(255, 37, 0, sa);
            byte[] row = { 9, 88, 255, (byte)da };
            double s = sa / 255d, d = da / 255d, a = s + d * (1 - s);
            byte Channel(int src, int dst) => a == 0 ? (byte)0 : (byte)Math.Floor((src * s + dst * d * (1 - s)) / a + 1e-10);
            byte[] expected = { Channel(255, 9), Channel(37, 88), Channel(0, 255), (byte)Math.Floor(a * 255 + 1e-10) };
            AkronScreenshotScanner.BlendOverRowPixel(row, 0, source);
            Assert.Equal(expected, row);
        }
    }

    [NativeEverestFact]
    public void StreamingPngPipelinePreservesOverlapAndTransparentGaps() {
        var instance = typeof(AkronModule).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previous = instance.GetValue(null);
        var module = (AkronModule)RuntimeHelpers.GetUninitializedObject(typeof(AkronModule));
        var settings = new AkronModuleSettings { ScreenshotScannerImageFormat = AkronScreenshotImageFormat.Png };
        string dir = Path.Combine(Path.GetTempPath(), "akron-alpha-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            typeof(EverestModule).GetField("<_Settings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(module, settings);
            instance.SetValue(null, module);
            Type scanner = typeof(AkronScreenshotScanner);
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            string tile = Path.Combine(dir, "tile.png");
            Color red = new(255, 0, 0, 128);
            using (var output = File.Create(tile)) scanner.GetMethod("SavePngFromPixels", flags)!.Invoke(null,
                new object[] { output, new[] { red, red, red, red }, 2, 2 });
            var rooms = new[] {
                new AkronScreenshotMergedRoom("a", tile, dir, new Rectangle(0,0,2,2),2,2,2,2,1),
                new AkronScreenshotMergedRoom("b", tile, dir, new Rectangle(1,0,2,2),2,2,2,2,1)
            };
            foreach (bool scaledAndClipped in new[] { false, true }) {
                var placements = new[] {
                    new AkronScreenshotMapPlacement("a",scaledAndClipped ? -1 : 0,0,scaledAndClipped ? 4 : 2,2,1),
                    new AkronScreenshotMapPlacement("b",1,0,2,2,1)
                };
                string map = Path.Combine(dir, "map.png");
                scanner.GetMethod("SaveMergedMapImageStreaming", flags)!.Invoke(null, new object[] { map, rooms, placements, 4, 3 });
                object[] args = { map, 0, 0 };
                Color[] pixels = (Color[])scanner.GetMethod("LoadPngPixels", flags)!.Invoke(null, args)!;
                Assert.Equal(4, args[1]); Assert.Equal(3, args[2]);
                Assert.Equal(red, pixels[0]);
                Assert.Equal(new Color(255,0,0,191), pixels[1]);
                Assert.Equal(scaledAndClipped ? new Color(255,0,0,191) : red, pixels[2]);
                Assert.Equal(new Color(0,0,0,0), pixels[3]);
                Assert.Equal(new Color(0,0,0,0), pixels[8]);
            }
        } finally {
            instance.SetValue(null, previous);
            Directory.Delete(dir, true);
        }
    }
}

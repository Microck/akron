using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class JpegEncoderTests {
    [Theory]
    [InlineData(1, 1)] [InlineData(2, 3)] [InlineData(3, 2)] [InlineData(4, 4)]
    [InlineData(1177, 17)] [InlineData(1178, 18)] [InlineData(1179, 17)] [InlineData(1180, 18)]
    public void EncodesEveryRowAlignmentAndLeavesStreamOpen(int width, int height) {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
            int i = (y * width + x) * 4;
            pixels[i] = (byte)(x < width / 2 ? 220 : 20);
            pixels[i + 1] = (byte)(y < height / 2 ? 200 : 30);
            pixels[i + 2] = 70; pixels[i + 3] = 255;
        }
        using var stream = new MemoryStream();
        AkronJpegEncoder.Write(stream, pixels, width, height);
        byte[] encoded = stream.ToArray();
        Assert.Equal(0xff, encoded[0]); Assert.Equal(0xd8, encoded[1]);
        Assert.Equal(0xff, encoded[^2]); Assert.Equal(0xd9, encoded[^1]);
        Assert.True(stream.CanWrite);
        using var second = new MemoryStream();
        AkronJpegEncoder.Write(second, pixels, width, height);
        Assert.Equal(encoded, second.ToArray());
    }

    [Theory]
    [InlineData(65536, 1)] [InlineData(1, 65536)]
    [InlineData(0, 1)] [InlineData(1, 0)] [InlineData(-1, 2)] [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsInvalidDimensionsBeforeAllocation(int width, int height) {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentOutOfRangeException>(() => AkronJpegEncoder.Write(stream, Array.Empty<byte>(), width, height));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void ValidatesBuffersAndWritableDestination() {
        using var stream = new MemoryStream();
        Assert.Throws<ArgumentNullException>(() => AkronJpegEncoder.Write(null!, new byte[4], 1, 1));
        Assert.Throws<ArgumentNullException>(() => AkronJpegEncoder.Write(stream, null!, 1, 1));
        Assert.Throws<ArgumentException>(() => AkronJpegEncoder.Write(stream, new byte[3], 1, 1));
        Assert.Throws<ArgumentException>(() => AkronJpegEncoder.Write(stream, new byte[5], 1, 1));
        using var readOnly = new MemoryStream(new byte[4], false);
        Assert.Throws<ArgumentException>(() => AkronJpegEncoder.Write(readOnly, new byte[4], 1, 1));
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class SnapshotBundleTests {
    [Fact]
    public void RoundTripPreservesArbitraryBytesAndSharedDocuments() {
        using var files = new BundleFiles();
        byte[] first = new byte[192 * 1024 + 17];
        new Random(423).NextBytes(first);
        // Include long base64 runs, padding, escaped strings, and numeric lexemes.
        Encoding.ASCII.GetBytes(new string('A', 70001)).CopyTo(first, 31);
        Encoding.UTF8.GetBytes("\"AAAA==\\\"\",-0,1.00000000000000000001,1e+300,é").CopyTo(first, 70100);
        byte[] second = (byte[])first.Clone();
        second[^1] ^= 1;
        var expected = new Dictionary<int, byte[]> { [2] = first, [99] = second };
        var sources = new[] { files.Source(99, second), files.Source(2, first) };
        Dictionary<int, string> written = AkronSnapshotBundle.Write(files.Bundle, sources);
        var visited = new List<int>();
        using var stream = File.OpenRead(files.Bundle);
        Dictionary<int, string> read = AkronSnapshotBundle.Read(stream, (slot, document) => {
            visited.Add(slot);
            using var restored = new MemoryStream();
            document.CopyTo(restored, 997);
            Assert.Equal(expected[slot], restored.ToArray());
        });
        Assert.Equal(new[] { 2, 99 }, visited);
        foreach (int slot in visited) {
            string hash = Convert.ToHexString(SHA256.HashData(expected[slot])).ToLowerInvariant();
            Assert.Equal(hash, written[slot]);
            Assert.Equal(hash, read[slot]);
        }
    }

    [Fact]
    public void ChangedSourceCannotProduceAnArchive() {
        using var files = new BundleFiles();
        AkronSnapshotBundle.Source source = files.Source(1, Encoding.UTF8.GetBytes("original"));
        File.AppendAllText(source.Path, "changed");
        Assert.Throws<InvalidDataException>(() => AkronSnapshotBundle.Write(files.Bundle, new[] { source }));
        Assert.False(File.Exists(files.Bundle));
        Assert.Empty(Directory.GetDirectories(files.DirectoryPath));
    }

    [Fact]
    public void CancellationPreservesAnExistingDestination() {
        using var files = new BundleFiles();
        AkronSnapshotBundle.Source source = files.Source(1, Encoding.UTF8.GetBytes("original"));
        File.WriteAllText(files.Bundle, "previous export");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => AkronSnapshotBundle.Write(files.Bundle, new[] { source }, cancellation.Token));
        Assert.Equal("previous export", File.ReadAllText(files.Bundle));
        Assert.Empty(Directory.GetDirectories(files.DirectoryPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsTrailingAndTruncatedBrotli(bool trailing) {
        using var files = new BundleFiles();
        AkronSnapshotBundle.Write(files.Bundle, new[] { files.Source(1, Encoding.UTF8.GetBytes("snapshot")) });
        byte[] valid = File.ReadAllBytes(files.Bundle);
        byte[] invalid = new byte[valid.Length + (trailing ? 1 : -1)];
        Array.Copy(valid, invalid, Math.Min(valid.Length, invalid.Length));
        using var stream = new MemoryStream(invalid);
        Action read = () => AkronSnapshotBundle.Read(stream, (_, document) => document.CopyTo(Stream.Null));
        if (trailing) Assert.Throws<InvalidDataException>(read);
        else Assert.Throws<EndOfStreamException>(read);
    }

    [Fact]
    public void RejectsAConsumerThatLeavesDocumentBytesUnread() {
        using var files = new BundleFiles();
        AkronSnapshotBundle.Write(files.Bundle, new[] { files.Source(1, Encoding.UTF8.GetBytes("snapshot")) });
        using var stream = File.OpenRead(files.Bundle);
        Assert.Throws<InvalidDataException>(() => AkronSnapshotBundle.Read(stream, (_, _) => { }));
    }

    [Theory]
    [InlineData(4097, 0, 0)]
    [InlineData(0, 100, 0)]
    [InlineData(0, 1, 100)]
    public void RejectsOutOfRangeBundleHeaders(int dictionaryCount, int documents, int slot) {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(Encoding.ASCII.GetBytes("AKRSB001"));
            writer.Write(dictionaryCount);
            writer.Write(documents);
            writer.Write(slot);
        }
        using MemoryStream encoded = LiteralBundle(raw.ToArray());
        Assert.Throws<InvalidDataException>(() => AkronSnapshotBundle.Read(encoded, (_, document) => document.CopyTo(Stream.Null)));
    }

    [Fact]
    public void ReadsAnIndependentlyConstructedDictionaryReference() {
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(Encoding.ASCII.GetBytes("AKRSB001"));
            writer.Write(1); writer.Write(3); writer.Write(Encoding.ASCII.GetBytes("abc"));
            writer.Write(1); writer.Write(7); writer.Write(6);
            writer.Write((byte)1); writer.Write(0);
            writer.Write((byte)0); writer.Write(3); writer.Write(Encoding.ASCII.GetBytes("def"));
        }
        using MemoryStream encoded = LiteralBundle(raw.ToArray());
        AkronSnapshotBundle.Read(encoded, (slot, document) => {
            Assert.Equal(7, slot);
            using var restored = new StreamReader(document, leaveOpen: true);
            Assert.Equal("abcdef", restored.ReadToEnd());
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnforcesExpandedPackBudgetBeforeDeliveringTheCrossingDocument(bool overBudget) {
        const int blockBytes = 65536;
        int[] lengths = { 384 * 1024 * 1024, 384 * 1024 * 1024, 256 * 1024 * 1024 };
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(Encoding.ASCII.GetBytes("AKRSB001"));
            writer.Write(1);
            writer.Write(blockBytes);
            writer.Write(new byte[blockBytes]);
            writer.Write(lengths.Length + (overBudget ? 1 : 0));
            for (int i = 0; i < lengths.Length; i++) {
                writer.Write(i + 1);
                writer.Write(lengths[i]);
                for (int remaining = lengths[i]; remaining > 0; remaining -= blockBytes) {
                    writer.Write((byte)1);
                    writer.Write(0);
                }
            }
            if (overBudget) {
                writer.Write(4);
                writer.Write(1);
                writer.Write((byte)0);
                writer.Write(1);
                writer.Write((byte)0);
            }
        }
        using MemoryStream encoded = LiteralBundle(raw.ToArray());
        var visited = new List<int>();
        var buffer = new byte[blockBytes];
        long consumed = 0;
        Action read = () => AkronSnapshotBundle.Read(encoded, (slot, document) => {
            visited.Add(slot);
            int count;
            while ((count = document.Read(buffer)) > 0) consumed += count;
        });
        if (overBudget) Assert.Throws<InvalidDataException>(read);
        else read();
        Assert.Equal(new[] { 1, 2, 3 }, visited);
        Assert.Equal(1024L * 1024 * 1024, consumed);
    }

    [Fact]
    public void RejectsExcessiveOneByteCommands() {
        const int length = 2048;
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(Encoding.ASCII.GetBytes("AKRSB001"));
            writer.Write(0); writer.Write(1); writer.Write(1); writer.Write(length);
            for (int index = 0; index < length; index++) {
                writer.Write((byte)0); writer.Write(1); writer.Write((byte)0);
            }
        }
        using MemoryStream encoded = LiteralBundle(raw.ToArray());
        Assert.Throws<InvalidDataException>(() => AkronSnapshotBundle.Read(encoded,
            (_, document) => document.CopyTo(Stream.Null)));
    }

    [Fact]
    public void RejectsTinyFrameBurstAfterAFullFrame() {
        const int tailBytes = 2000;
        int length = AkronSnapshotBundle.BlockBytes + tailBytes;
        using var raw = new MemoryStream();
        using (var writer = new BinaryWriter(raw, Encoding.UTF8, leaveOpen: true)) {
            writer.Write(Encoding.ASCII.GetBytes("AKRSB001"));
            writer.Write(0); writer.Write(1); writer.Write(1); writer.Write(length);
            writer.Write((byte)0); writer.Write(AkronSnapshotBundle.BlockBytes);
            writer.Write(new byte[AkronSnapshotBundle.BlockBytes]);
            writer.Write((byte)0); writer.Write(tailBytes); writer.Write(new byte[tailBytes]);
        }
        byte[] bytes = raw.ToArray();
        using var encoded = new MemoryStream();
        using (var brotli = new BrotliStream(encoded, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var writer = new BinaryWriter(brotli)) {
            writer.Write((byte)0); writer.Write(AkronSnapshotBundle.BlockBytes);
            writer.Write(bytes, 0, AkronSnapshotBundle.BlockBytes);
            for (int index = AkronSnapshotBundle.BlockBytes; index < bytes.Length; index++) {
                writer.Write((byte)0); writer.Write(1); writer.Write(bytes[index]);
            }
        }
        encoded.Position = 0;
        Assert.Throws<InvalidDataException>(() => AkronSnapshotBundle.Read(encoded,
            (_, document) => document.CopyTo(Stream.Null)));
    }

    [Fact]
    public void WriterRoundTripsMinimumPackedRunsAndTinyLiteralSeparators() {
        using var files = new BundleFiles();
        byte[] expected = new byte[129 * 2048];
        var random = new Random(912);
        for (int index = 0; index < expected.Length; index++)
            expected[index] = index % 129 == 128 ? (byte)0 : (byte)random.Next('A', 'Z' + 1);
        AkronSnapshotBundle.Write(files.Bundle, new[] { files.Source(1, expected) });
        using var encoded = File.OpenRead(files.Bundle);
        AkronSnapshotBundle.Read(encoded, (_, document) => {
            using var restored = new MemoryStream();
            document.CopyTo(restored);
            Assert.Equal(expected, restored.ToArray());
        });
    }

    private static MemoryStream LiteralBundle(byte[] raw) {
        var encoded = new MemoryStream();
        using (var brotli = new BrotliStream(encoded, CompressionLevel.Fastest, leaveOpen: true))
        using (var writer = new BinaryWriter(brotli)) {
            for (int offset = 0; offset < raw.Length; offset += AkronSnapshotBundle.BlockBytes) {
                int count = Math.Min(AkronSnapshotBundle.BlockBytes, raw.Length - offset);
                writer.Write((byte)0); writer.Write(count); writer.Write(raw, offset, count);
            }
        }
        encoded.Position = 0;
        return encoded;
    }

    private sealed class BundleFiles : IDisposable {
        internal string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "akron-bundle-" + Guid.NewGuid().ToString("N"));
        internal string Bundle => Path.Combine(DirectoryPath, "bundle.br");
        internal BundleFiles() => Directory.CreateDirectory(DirectoryPath);
        internal AkronSnapshotBundle.Source Source(int slot, byte[] bytes) {
            string path = Path.Combine(DirectoryPath, slot + ".json.gz");
            using (var file = File.Create(path))
            using (var gzip = new GZipStream(file, CompressionLevel.Fastest)) gzip.Write(bytes);
            using var source = File.OpenRead(path);
            return new AkronSnapshotBundle.Source(slot, path, Convert.ToHexString(SHA256.HashData(source)));
        }
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}

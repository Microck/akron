using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Celeste.Mod.Akron;

// An opaque byte transport: graph serialization and local save files stay unchanged.
internal static class AkronSnapshotBundle {
    internal const string EntryName = "startpos/snapshots.bin.br";
    internal const int BlockBytes = 65536;
    internal const int MaxDictionaryBytes = 16 * 1024 * 1024;
    internal const int MaxDocumentBytes = 384 * 1024 * 1024;
    private const int MaxDictionaryItems = 4096;
    private const int MaxDiscoveryItems = 65536;
    private const long MaxDiscoveryBytes = 128L * 1024 * 1024;
    private const long MaxEncodedBytes = 509L * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("AKRSB001");
    private static readonly uint[] Gear = CreateGear();

    internal sealed record Source(int Slot, string Path, string Sha256);
    private sealed record Document(Source Source, int Length, string Sha256);
    private sealed class Chunk {
        internal string Hash;
        internal long Offset;
        internal int Length;
        internal int Count;
        internal double Score;
    }

    // Discovery is bounded independently of pack size. Candidate bytes live on disk,
    // rather than retaining every raw snapshot or every unique chunk in memory.
    internal static Dictionary<int, string> Write(string destination, IReadOnlyList<Source> sources,
            CancellationToken cancellationToken = default) {
        if (sources.Count is < 1 or > 99 || sources.Any(source => source.Slot is < 1 or > 99) ||
                sources.Select(source => source.Slot).Distinct().Count() != sources.Count)
            throw new InvalidDataException("Invalid snapshot bundle slots.");
        string staging = destination + "." + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try {
            using var spool = new FileStream(Path.Combine(staging, "chunks"), FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None);
            var chunks = new Dictionary<string, Chunk>(StringComparer.Ordinal);
            var documents = new List<Document>();
            foreach (Source source in sources.OrderBy(source => source.Slot)) {
                using FileStream file = OpenSource(source);
                using var gzip = new GZipStream(file, CompressionMode.Decompress);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var reader = new ChunkReader(gzip);
                int length = 0;
                while (reader.Read() is int count && count > 0) {
                    cancellationToken.ThrowIfCancellationRequested();
                    length = checked(length + count);
                    if (length > MaxDocumentBytes) throw new InvalidDataException("Snapshot is too large.");
                    ReadOnlySpan<byte> bytes = reader.Bytes;
                    hash.AppendData(bytes);
                    string key = Convert.ToHexString(SHA256.HashData(bytes));
                    if (chunks.TryGetValue(key, out Chunk chunk)) {
                        chunk.Count++;
                    } else if (chunks.Count < MaxDiscoveryItems && spool.Length + count <= MaxDiscoveryBytes) {
                        chunks.Add(key, new Chunk { Hash = key, Offset = spool.Length, Length = count, Count = 1 });
                        spool.Position = spool.Length;
                        spool.Write(bytes);
                    }
                }
                if (length == 0) throw new InvalidDataException("Snapshot is empty.");
                documents.Add(new Document(source, length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()));
            }
            var buffer = new byte[BlockBytes];
            foreach (Chunk chunk in chunks.Values.Where(chunk => chunk.Count > 1)) {
                cancellationToken.ThrowIfCancellationRequested();
                spool.Position = chunk.Offset;
                spool.ReadExactly(buffer.AsSpan(0, chunk.Length));
                using var estimate = new MemoryStream();
                using (var deflate = new DeflateStream(estimate, CompressionLevel.Fastest, leaveOpen: true))
                    deflate.Write(buffer, 0, chunk.Length);
                if (estimate.Length > 128)
                    chunk.Score = (double)estimate.Length * (chunk.Count - 1) / chunk.Length;
            }
            var dictionary = new List<byte[]>();
            var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
            int dictionaryBytes = 0;
            foreach (Chunk chunk in chunks.Values.Where(chunk => chunk.Score > 0)
                    .OrderByDescending(chunk => chunk.Score).ThenBy(chunk => chunk.Hash, StringComparer.Ordinal)) {
                if (dictionary.Count == MaxDictionaryItems) break;
                if (dictionaryBytes + chunk.Length > MaxDictionaryBytes) continue;
                var bytes = new byte[chunk.Length];
                spool.Position = chunk.Offset;
                spool.ReadExactly(bytes);
                indexes.Add(chunk.Hash, dictionary.Count);
                dictionary.Add(bytes);
                dictionaryBytes += bytes.Length;
            }
            string plainPath = Path.Combine(staging, "plain.br");
            WriteCandidate(plainPath, documents, Array.Empty<byte[]>(), new Dictionary<string, int>(), cancellationToken);
            string selectedPath = plainPath;
            if (dictionary.Count > 0) {
                string sharedPath = Path.Combine(staging, "shared.br");
                WriteCandidate(sharedPath, documents, dictionary, indexes, cancellationToken);
                if (new FileInfo(sharedPath).Length < new FileInfo(plainPath).Length) selectedPath = sharedPath;
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(selectedPath, destination);
            return documents.ToDictionary(document => document.Source.Slot, document => document.Sha256);
        } finally {
            Directory.Delete(staging, recursive: true);
        }
    }

    private static FileStream OpenSource(Source source) {
        var file = new FileStream(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try {
            string actual = Convert.ToHexString(SHA256.HashData(file));
            if (!actual.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Snapshot changed since capture. Export it again.");
            file.Position = 0;
            return file;
        } catch {
            file.Dispose();
            throw;
        }
    }

    private static void WriteCandidate(string path, IReadOnlyList<Document> documents,
            IReadOnlyList<byte[]> dictionary, Dictionary<string, int> indexes, CancellationToken token) {
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var brotli = new BrotliWriter(file);
        using var frames = new FrameWriter(brotli);
        using var writer = new BinaryWriter(frames, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(dictionary.Count);
        foreach (byte[] bytes in dictionary) { writer.Write(bytes.Length); writer.Write(bytes); }
        writer.Write(documents.Count);
        foreach (Document document in documents) {
            token.ThrowIfCancellationRequested();
            writer.Write(document.Source.Slot);
            writer.Write(document.Length);
            using FileStream source = OpenSource(document.Source);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var reader = new ChunkReader(gzip);
            int length = 0;
            while (reader.Read() is int count && count > 0) {
                token.ThrowIfCancellationRequested();
                length = checked(length + count);
                if (length > document.Length) throw new InvalidDataException("Snapshot changed while exporting.");
                hash.AppendData(reader.Bytes);
                if (indexes.Count > 0 && indexes.TryGetValue(Convert.ToHexString(SHA256.HashData(reader.Bytes)), out int index)) {
                    writer.Write((byte)1);
                    writer.Write(index);
                } else {
                    writer.Write((byte)0);
                    writer.Write(count);
                    writer.Write(reader.Bytes);
                }
            }
            if (length != document.Length || !Convert.ToHexString(hash.GetHashAndReset())
                    .Equals(document.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Snapshot changed while exporting.");
        }
        frames.Complete();
        brotli.Complete();
    }

    // The callback must consume each document before returning. The caller can stage
    // its normal local save, then commit only after all metadata and hashes agree.
    internal static Dictionary<int, string> Read(Stream source, Action<int, Stream> readDocument) {
        using var brotli = new BrotliReader(source);
        using var frames = new FrameReader(brotli);
        using var reader = new BinaryReader(frames, Encoding.UTF8, leaveOpen: true);
        Span<byte> magic = stackalloc byte[8];
        frames.ReadExactly(magic);
        if (!magic.SequenceEqual(Magic)) throw new InvalidDataException("Unsupported snapshot bundle.");
        int count = ReadNumber(reader, 0, MaxDictionaryItems);
        var dictionary = new byte[count][];
        int dictionaryBytes = 0;
        for (int i = 0; i < count; i++) {
            int length = ReadNumber(reader, 1, BlockBytes);
            dictionaryBytes += length;
            if (dictionaryBytes > MaxDictionaryBytes) throw new InvalidDataException("Snapshot dictionary is too large.");
            dictionary[i] = new byte[length];
            frames.ReadExactly(dictionary[i]);
        }
        int documentCount = ReadNumber(reader, 1, 99);
        var hashes = new Dictionary<int, string>();
        int previousSlot = 0;
        for (int i = 0; i < documentCount; i++) {
            int slot = ReadNumber(reader, previousSlot + 1, 99);
            int length = ReadNumber(reader, 1, MaxDocumentBytes);
            using var document = new DocumentReader(reader, dictionary, length);
            readDocument(slot, document);
            if (document.Remaining != 0) throw new InvalidDataException("Snapshot reader did not consume the document.");
            hashes.Add(slot, document.FinishHash());
            previousSlot = slot;
        }
        if (frames.ReadByte() != -1) throw new InvalidDataException("Trailing snapshot bundle bytes.");
        return hashes;
    }

    private static int ReadNumber(BinaryReader reader, int minimum, int maximum) {
        uint value = reader.ReadUInt32();
        if (value < minimum || value > maximum) throw new InvalidDataException("Invalid snapshot bundle length or index.");
        return (int)value;
    }

    private sealed class ChunkReader(Stream source) {
        private readonly byte[] buffer = new byte[BlockBytes];
        private int available;
        private int previous;
        internal ReadOnlySpan<byte> Bytes => buffer.AsSpan(0, previous);
        internal int Read() {
            buffer.AsSpan(previous, available - previous).CopyTo(buffer);
            available -= previous;
            while (available < buffer.Length) {
                int count = source.Read(buffer, available, buffer.Length - available);
                if (count == 0) break;
                available += count;
            }
            uint hash = 0;
            previous = 0;
            while (previous < available) {
                hash = unchecked((hash << 1) + Gear[buffer[previous++]]);
                if (previous >= 4096 && ((hash & 16383) == 0 || previous == BlockBytes)) break;
            }
            return previous;
        }
    }

    private static uint[] CreateGear() {
        var gear = new uint[256];
        uint seed = 0x9e3779b9;
        for (int i = 0; i < gear.Length; i++) {
            seed ^= seed << 13; seed ^= seed >> 17; seed ^= seed << 5;
            gear[i] = seed;
        }
        return gear;
    }

    private abstract class ForwardStream : Stream {
        public override bool CanSeek => false;
        public override bool CanRead => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    }

    private sealed class BrotliWriter(Stream destination) : ForwardStream {
        private BrotliEncoder encoder = new BrotliEncoder(11, 24);
        private readonly byte[] buffer = new byte[BlockBytes];
        private long written;
        public override bool CanWrite => true;
        public override void Write(ReadOnlySpan<byte> bytes) => Encode(bytes, false);
        internal void Complete() => Encode(ReadOnlySpan<byte>.Empty, true);
        private void Encode(ReadOnlySpan<byte> bytes, bool final) {
            OperationStatus status;
            do {
                status = encoder.Compress(bytes, buffer, out int consumed, out int produced, final);
                if (status == OperationStatus.InvalidData) throw new InvalidDataException("Snapshot compression failed.");
                written += produced;
                if (written > MaxEncodedBytes) throw new InvalidDataException("Snapshot bundle is too large.");
                destination.Write(buffer, 0, produced);
                bytes = bytes[consumed..];
            } while (status == OperationStatus.DestinationTooSmall || (final && status != OperationStatus.Done));
        }
        protected override void Dispose(bool disposing) { if (disposing) encoder.Dispose(); base.Dispose(disposing); }
    }

    private sealed class BrotliReader(Stream source) : ForwardStream {
        private BrotliDecoder decoder = new BrotliDecoder();
        private readonly byte[] input = new byte[BlockBytes];
        private int offset;
        private int available;
        private bool ended;
        public override bool CanRead => true;
        public override int Read(Span<byte> bytes) {
            if (bytes.IsEmpty || ended) return 0;
            while (true) {
                if (offset == available) { available = source.Read(input); offset = 0; }
                OperationStatus status = decoder.Decompress(input.AsSpan(offset, available - offset), bytes,
                    out int consumed, out int produced);
                offset += consumed;
                if (status == OperationStatus.InvalidData) throw new InvalidDataException("Invalid Brotli snapshot bundle.");
                if (status == OperationStatus.Done) {
                    ended = true;
                    if (offset != available || source.ReadByte() != -1)
                        throw new InvalidDataException("Trailing Brotli snapshot bytes.");
                } else if (status == OperationStatus.NeedMoreData && available == 0) {
                    throw new EndOfStreamException("Truncated Brotli snapshot bundle.");
                }
                if (produced > 0 || ended) return produced;
            }
        }
        protected override void Dispose(bool disposing) { if (disposing) decoder.Dispose(); base.Dispose(disposing); }
    }

    // Buffer runs across Write boundaries. A full run is divisible by four, so its
    // binary frame needs no padding and can be expanded without losing any bytes.
    private sealed class FrameWriter(Stream destination) : ForwardStream {
        private readonly byte[] literal = new byte[BlockBytes];
        private readonly byte[] run = new byte[BlockBytes];
        private readonly byte[] binary = new byte[BlockBytes * 3 / 4];
        private int literalCount;
        private int runCount;
        public override bool CanWrite => true;
        public override void Write(ReadOnlySpan<byte> bytes) {
            foreach (byte value in bytes) {
                if (value is >= (byte)'A' and <= (byte)'Z' or >= (byte)'a' and <= (byte)'z' or
                        >= (byte)'0' and <= (byte)'9' or (byte)'+' or (byte)'/') {
                    run[runCount++] = value;
                    if (runCount == run.Length) FlushRun();
                } else { FlushRun(); Literal(value); }
            }
        }
        private void Literal(byte value) {
            literal[literalCount++] = value;
            if (literalCount == literal.Length) FlushLiteral();
        }
        private void FlushRun() {
            int packed = runCount >= 128 ? runCount / 4 * 4 : 0;
            if (packed > 0) {
                FlushLiteral();
                Base64.DecodeFromUtf8(run.AsSpan(0, packed), binary, out _, out int produced);
                Frame(1, binary.AsSpan(0, produced));
            }
            for (int i = packed; i < runCount; i++) Literal(run[i]);
            runCount = 0;
        }
        private void FlushLiteral() {
            if (literalCount > 0) Frame(0, literal.AsSpan(0, literalCount));
            literalCount = 0;
        }
        private void Frame(byte tag, ReadOnlySpan<byte> bytes) {
            Span<byte> header = stackalloc byte[5];
            header[0] = tag;
            BinaryPrimitives.WriteUInt32LittleEndian(header[1..], (uint)bytes.Length);
            destination.Write(header);
            destination.Write(bytes);
        }
        internal void Complete() { FlushRun(); FlushLiteral(); }
    }

    private sealed class FrameReader(Stream source) : ForwardStream {
        private readonly byte[] buffer = new byte[BlockBytes];
        private readonly byte[] binary = new byte[BlockBytes * 3 / 4];
        private int offset;
        private int available;
        public override bool CanRead => true;
        public override int Read(Span<byte> bytes) {
            if (bytes.IsEmpty) return 0;
            if (offset == available) {
                int tag = source.ReadByte();
                if (tag == -1) return 0;
                Span<byte> header = stackalloc byte[4];
                source.ReadExactly(header);
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(header);
                if (length == 0 || length > BlockBytes || tag is not (0 or 1))
                    throw new InvalidDataException("Invalid snapshot frame.");
                if (tag == 1) {
                    if (length > binary.Length || length % 3 != 0) throw new InvalidDataException("Invalid base64 frame.");
                    source.ReadExactly(binary.AsSpan(0, (int)length));
                    Base64.EncodeToUtf8(binary.AsSpan(0, (int)length), buffer, out _, out available);
                } else {
                    available = (int)length;
                    source.ReadExactly(buffer.AsSpan(0, available));
                }
                offset = 0;
            }
            int count = Math.Min(bytes.Length, available - offset);
            buffer.AsSpan(offset, count).CopyTo(bytes);
            offset += count;
            return count;
        }
    }

    private sealed class DocumentReader(BinaryReader reader, byte[][] dictionary, int length) : ForwardStream {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] reference;
        private int commandRemaining;
        private int referenceOffset;
        internal int Remaining { get; private set; } = length;
        public override bool CanRead => true;
        public override int Read(Span<byte> bytes) {
            if (bytes.IsEmpty || Remaining == 0) return 0;
            if (commandRemaining == 0) {
                int tag = reader.ReadByte();
                if (tag == 0) {
                    commandRemaining = ReadNumber(reader, 1, BlockBytes);
                    reference = null;
                } else if (tag == 1 && dictionary.Length > 0) {
                    reference = dictionary[ReadNumber(reader, 0, dictionary.Length - 1)];
                    referenceOffset = 0;
                    commandRemaining = reference.Length;
                } else throw new InvalidDataException("Invalid snapshot command.");
                if (commandRemaining > Remaining) throw new InvalidDataException("Snapshot command exceeds document length.");
            }
            int count = Math.Min(bytes.Length, commandRemaining);
            if (reference == null) reader.BaseStream.ReadExactly(bytes[..count]);
            else { reference.AsSpan(referenceOffset, count).CopyTo(bytes); referenceOffset += count; }
            hash.AppendData(bytes[..count]);
            commandRemaining -= count;
            Remaining -= count;
            return count;
        }
        internal string FinishHash() => Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        protected override void Dispose(bool disposing) { if (disposing) hash.Dispose(); base.Dispose(disposing); }
    }
}

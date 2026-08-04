using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Globalization;
using System.Text.Json;

namespace Celeste.Mod.Akron;

public sealed class AkronArchiveManifest {
    public string Format { get; set; } = AkronArchive.Format;
    public int FormatVersion { get; set; } = AkronArchive.FormatVersion;
    public string Kind { get; set; } = string.Empty;
    public int KindVersion { get; set; } = 1;
    public string CreatedBy { get; set; } = "Akron";
    public string CreatedAt { get; set; } = string.Empty;
    public AkronArchiveTarget Target { get; set; } = new AkronArchiveTarget();
}

public sealed class AkronArchiveTarget {
    public string Game { get; set; } = "Celeste";
    public string MapSid { get; set; } = string.Empty;
}

public static class AkronArchive {
    public const string Format = "akron-archive";
    public const int FormatVersion = 1;
    public const string Extension = ".akr";
    public const string ManifestEntryName = "manifest.json";

    private const int MaxManifestBytes = 16 * 1024;
    private const int MaxKindLength = 64;
    private const int MaxMapSidLength = 256;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void WriteSinglePayloadArchive(string path, AkronArchiveManifest manifest, string payloadEntryName, string payloadJson) {
        WritePayloadArchive(path, manifest, payloadEntryName, payloadJson, new Dictionary<string, string>());
    }

    public static void WritePayloadArchive(
        string path,
        AkronArchiveManifest manifest,
        string payloadEntryName,
        string payloadJson,
        IReadOnlyDictionary<string, string> attachmentPaths
    ) {
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Archive path is required.", nameof(path));
        }

        ValidatePayloadEntryName(payloadEntryName);
        ValidateManifest(manifest, manifest.Kind);
        attachmentPaths ??= new Dictionary<string, string>();
        foreach (KeyValuePair<string, string> attachment in attachmentPaths) {
            ValidateArchiveEntryName(attachment.Key);
            if (string.Equals(attachment.Key, ManifestEntryName, StringComparison.Ordinal) ||
                string.Equals(attachment.Key, payloadEntryName, StringComparison.Ordinal)) {
                throw new ArgumentException("Archive entry names must be unique.", nameof(attachmentPaths));
            }
            if (string.IsNullOrWhiteSpace(attachment.Value) || !File.Exists(attachment.Value)) {
                throw new FileNotFoundException("Archive attachment not found.", attachment.Value);
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        if (File.Exists(path)) {
            File.Delete(path);
        }

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, payloadEntryName, payloadJson ?? string.Empty);
        foreach (KeyValuePair<string, string> attachment in attachmentPaths.OrderBy(pair => pair.Key, StringComparer.Ordinal)) {
            WriteFileEntry(archive, attachment.Key, attachment.Value);
        }
    }

    public static string ReadSinglePayloadArchive(
        string path,
        string expectedKind,
        string payloadEntryName,
        int maxPayloadBytes,
        out AkronArchiveManifest manifest) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new FileNotFoundException("Archive not found.", path);
        }

        if (maxPayloadBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes), "Payload size limit must be positive.");
        }

        return ReadPayloadArchiveCore(
            path,
            expectedKind,
            payloadEntryName,
            maxPayloadBytes,
            maxAttachmentCount: 0,
            maxTotalAttachmentBytes: 0,
            requireExactEntrySet: true,
            out manifest,
            out _);
    }

    public static string ReadPayloadArchive(
        string path,
        string expectedKind,
        string payloadEntryName,
        int maxPayloadBytes,
        int maxAttachmentCount,
        long maxTotalAttachmentBytes,
        out AkronArchiveManifest manifest,
        out string[] attachmentNames
    ) {
        return ReadPayloadArchiveCore(
            path,
            expectedKind,
            payloadEntryName,
            maxPayloadBytes,
            maxAttachmentCount,
            maxTotalAttachmentBytes,
            requireExactEntrySet: false,
            out manifest,
            out attachmentNames);
    }

    private static string ReadPayloadArchiveCore(
        string path,
        string expectedKind,
        string payloadEntryName,
        int maxPayloadBytes,
        int maxAttachmentCount,
        long maxTotalAttachmentBytes,
        bool requireExactEntrySet,
        out AkronArchiveManifest manifest,
        out string[] attachmentNames
    ) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new FileNotFoundException("Archive not found.", path);
        }
        if (maxPayloadBytes <= 0 || maxAttachmentCount < 0 || maxTotalAttachmentBytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes), "Archive size limits are invalid.");
        }

        ValidatePayloadEntryName(payloadEntryName);
        using ZipArchive archive = ZipFile.OpenRead(path);
        ValidateEntryNames(archive);

        if (requireExactEntrySet) {
            HashSet<string> expectedEntries = new HashSet<string>(StringComparer.Ordinal) {
                ManifestEntryName,
                payloadEntryName
            };
            if (archive.Entries.Count != expectedEntries.Count) {
                throw new InvalidDataException("Archive must contain exactly one manifest and one payload.");
            }
            foreach (ZipArchiveEntry entry in archive.Entries) {
                if (!expectedEntries.Contains(entry.FullName)) {
                    throw new InvalidDataException("Archive contains an unexpected entry: " + entry.FullName);
                }
            }
        }

        ZipArchiveEntry manifestEntry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException("Archive is missing manifest.json.");
        if (manifestEntry.Length > MaxManifestBytes) {
            throw new InvalidDataException("Archive manifest is too large.");
        }

        string manifestJson = ReadEntryText(manifestEntry, MaxManifestBytes);
        ValidateManifestJson(manifestJson);
        manifest = JsonSerializer.Deserialize<AkronArchiveManifest>(manifestJson, JsonOptions)
            ?? throw new InvalidDataException("Archive manifest is invalid.");
        ValidateManifest(manifest, expectedKind);

        ZipArchiveEntry payloadEntry = archive.GetEntry(payloadEntryName)
            ?? throw new InvalidDataException("Archive is missing " + payloadEntryName + ".");
        if (payloadEntry.Length > maxPayloadBytes) {
            throw new InvalidDataException("Archive payload is too large.");
        }

        attachmentNames = archive.Entries
            .Select(entry => entry.FullName)
            .Where(name => !string.Equals(name, ManifestEntryName, StringComparison.Ordinal) &&
                           !string.Equals(name, payloadEntryName, StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (attachmentNames.Length > maxAttachmentCount ||
            attachmentNames.Sum(name => archive.GetEntry(name)?.Length ?? 0L) > maxTotalAttachmentBytes) {
            throw new InvalidDataException("Archive attachments exceed their size limit.");
        }

        return ReadEntryText(payloadEntry, maxPayloadBytes);
    }

    public static byte[] ReadBinaryEntry(string path, string entryName, int maxBytes) {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
            throw new FileNotFoundException("Archive not found.", path);
        }
        if (maxBytes <= 0) {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), "Entry size limit must be positive.");
        }

        ValidateArchiveEntryName(entryName);
        using ZipArchive archive = ZipFile.OpenRead(path);
        ValidateEntryNames(archive);
        ZipArchiveEntry entry = archive.GetEntry(entryName)
            ?? throw new InvalidDataException("Archive is missing " + entryName + ".");
        if (entry.Length > maxBytes) {
            throw new InvalidDataException("Archive entry is too large.");
        }
        return ReadEntryBytes(entry, maxBytes);
    }

    private static void ValidateManifest(AkronArchiveManifest manifest, string expectedKind) {
        if (manifest == null) {
            throw new InvalidDataException("Archive manifest is missing.");
        }

        if (!string.Equals(manifest.Format, Format, StringComparison.Ordinal)) {
            throw new InvalidDataException("Archive format is unsupported.");
        }

        if (manifest.FormatVersion != FormatVersion) {
            throw new InvalidDataException("Archive format version is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Kind) || manifest.Kind.Length > MaxKindLength) {
            throw new InvalidDataException("Archive kind is missing.");
        }

        if (!string.Equals(manifest.Kind, expectedKind, StringComparison.Ordinal)) {
            throw new InvalidDataException("Archive kind is " + manifest.Kind + ", expected " + expectedKind + ".");
        }

        if (manifest.KindVersion != 1) {
            throw new InvalidDataException("Archive kind version is unsupported.");
        }

        if (!string.Equals(manifest.CreatedBy, "Akron", StringComparison.Ordinal)) {
            throw new InvalidDataException("Archive creator is invalid.");
        }

        if (!IsValidUtcTimestamp(manifest.CreatedAt)) {
            throw new InvalidDataException("Archive creation timestamp is invalid.");
        }

        if (manifest.Target == null || !string.Equals(manifest.Target.Game, "Celeste", StringComparison.Ordinal) ||
            manifest.Target.MapSid == null || manifest.Target.MapSid.Length > MaxMapSidLength) {
            throw new InvalidDataException("Archive target is invalid.");
        }
    }

    internal static bool IsValidUtcTimestamp(string value) {
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 64 && value.EndsWith("Z", StringComparison.Ordinal) &&
               DateTimeOffset.TryParseExact(
                   value,
                   new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" },
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                   out _);
    }

    private static void ValidateManifestJson(string json) {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        HashSet<string> expectedRoot = new HashSet<string>(StringComparer.Ordinal) {
            "format", "formatVersion", "kind", "kindVersion", "createdBy", "createdAt", "target"
        };
        JsonProperty[] rootProperties = root.ValueKind == JsonValueKind.Object ? root.EnumerateObject().ToArray() : Array.Empty<JsonProperty>();
        if (rootProperties.Length != expectedRoot.Count ||
            !rootProperties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expectedRoot) ||
            !root.TryGetProperty("target", out JsonElement target) ||
            target.ValueKind != JsonValueKind.Object) {
            throw new InvalidDataException("Archive manifest fields are invalid.");
        }

        HashSet<string> expectedTarget = new HashSet<string>(StringComparer.Ordinal) { "game", "mapSid" };
        JsonProperty[] targetProperties = target.EnumerateObject().ToArray();
        if (targetProperties.Length != expectedTarget.Count ||
            !targetProperties.Select(property => property.Name).ToHashSet(StringComparer.Ordinal).SetEquals(expectedTarget)) {
            throw new InvalidDataException("Archive manifest target fields are invalid.");
        }
    }

    private static void ValidateEntryNames(ZipArchive archive) {
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in archive.Entries) {
            string name = entry.FullName;
            try {
                ValidateArchiveEntryName(name);
            } catch (ArgumentException) {
                throw new InvalidDataException("Archive contains an unexpected entry: " + name);
            }
            if (!names.Add(name)) {
                throw new InvalidDataException("Archive contains a duplicate entry: " + name);
            }
        }
    }

    private static void ValidatePayloadEntryName(string payloadEntryName) {
        if (string.IsNullOrWhiteSpace(payloadEntryName) ||
            payloadEntryName.Contains('\\') ||
            payloadEntryName.Contains('/') ||
            payloadEntryName.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(payloadEntryName) ||
            string.Equals(payloadEntryName, ManifestEntryName, StringComparison.Ordinal)) {
            throw new ArgumentException("Payload entry name must be a simple archive file name.", nameof(payloadEntryName));
        }
    }

    private static void ValidateArchiveEntryName(string entryName) {
        if (string.IsNullOrWhiteSpace(entryName) ||
            entryName.Contains('\\') ||
            entryName.StartsWith("/", StringComparison.Ordinal) ||
            entryName.EndsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(entryName) ||
            entryName.Split('/').Any(segment => string.IsNullOrWhiteSpace(segment) || segment is "." or "..")) {
            throw new ArgumentException("Archive entry name is unsafe.", nameof(entryName));
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, string content) {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static void WriteFileEntry(ZipArchive archive, string name, string path) {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using Stream destination = entry.Open();
        using FileStream source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        source.CopyTo(destination);
    }

    private static string ReadEntryText(ZipArchiveEntry entry, int maxBytes) {
        return System.Text.Encoding.UTF8.GetString(ReadEntryBytes(entry, maxBytes));
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry, int maxBytes) {
        using Stream stream = entry.Open();
        using MemoryStream buffer = new MemoryStream();
        byte[] chunk = new byte[4096];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0) {
            if (buffer.Length + read > maxBytes) {
                throw new InvalidDataException("Archive entry exceeds its size limit.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

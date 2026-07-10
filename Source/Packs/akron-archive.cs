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
        if (string.IsNullOrWhiteSpace(path)) {
            throw new ArgumentException("Archive path is required.", nameof(path));
        }

        ValidatePayloadEntryName(payloadEntryName);
        ValidateManifest(manifest, manifest.Kind);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

        if (File.Exists(path)) {
            File.Delete(path);
        }

        using ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, ManifestEntryName, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteEntry(archive, payloadEntryName, payloadJson ?? string.Empty);
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

        ValidatePayloadEntryName(payloadEntryName);
        using ZipArchive archive = ZipFile.OpenRead(path);
        ValidateEntrySet(archive, payloadEntryName);

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

        return ReadEntryText(payloadEntry, maxPayloadBytes);
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

    private static void ValidateEntrySet(ZipArchive archive, string payloadEntryName) {
        HashSet<string> allowed = new HashSet<string>(StringComparer.Ordinal) {
            ManifestEntryName,
            payloadEntryName
        };

        if (archive.Entries.Count != allowed.Count) {
            throw new InvalidDataException("Archive must contain exactly one manifest and one payload.");
        }

        foreach (ZipArchiveEntry entry in archive.Entries) {
            string name = entry.FullName;
            if (string.IsNullOrWhiteSpace(name) ||
                name.Contains('\\') ||
                Path.IsPathRooted(name) ||
                name.Contains("../", StringComparison.Ordinal) ||
                name.Contains("/../", StringComparison.Ordinal) ||
                !allowed.Contains(name)) {
                throw new InvalidDataException("Archive contains an unexpected entry: " + name);
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

    private static void WriteEntry(ZipArchive archive, string name, string content) {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using StreamWriter writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string ReadEntryText(ZipArchiveEntry entry, int maxBytes) {
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

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}

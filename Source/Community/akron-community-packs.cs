using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronCommunityPackIndex {
    public string Format { get; set; } = AkronCommunityPacks.IndexFormat;
    public int Version { get; set; } = 2;
    public List<AkronCommunityPackEntry> Packs { get; set; } = new List<AkronCommunityPackEntry>();
}

public sealed class AkronCommunityPackEntry {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AkronSetupSection Section { get; set; } = AkronSetupSection.Whole;
    public string MapSid { get; set; } = string.Empty;
    public string MapUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorAvatarUrl { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> ImageUrls { get; set; } = new List<string>();
    public List<AkronCommunityPackImage> Images { get; set; } = new List<AkronCommunityPackImage>();
    public int DownloadCount { get; set; }
    public string UpdatedUtc { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new List<string>();

    [JsonIgnore]
    internal Uri CatalogUri { get; set; }
}

public sealed class AkronCommunityPackImage {
    public string Url { get; set; } = string.Empty;
    public string RoomName { get; set; } = string.Empty;
}

public sealed class AkronCommunityPackFilter {
    public string MapSid { get; set; } = string.Empty;
    public AkronSetupSection Section { get; set; } = AkronSetupSection.Whole;
    public string Query { get; set; } = string.Empty;
}

public sealed class AkronCommunityPackSearchResult {
    public AkronCommunityPackSearchResult(
        IReadOnlyList<AkronCommunityPackEntry> entries,
        string status,
        DateTime? fetchedUtc) {
        Entries = entries ?? Array.Empty<AkronCommunityPackEntry>();
        Status = status ?? string.Empty;
        FetchedUtc = fetchedUtc;
    }

    public IReadOnlyList<AkronCommunityPackEntry> Entries { get; }
    public string Status { get; }
    public DateTime? FetchedUtc { get; }
}

public static class AkronCommunityPacks {
    public const string IndexFormat = "akron-community-pack-index-v2";
    public const int IndexVersion = 2;
    public const string DefaultIndexUrl = "https://akron.micr.dev/catalog/index.json";

    private const int MaxIndexBytes = 1024 * 1024;
    private const int MaxPackBytes = 4 * 1024 * 1024;
    private const int MaxCatalogPacks = 512;
    private const int MaxCatalogImagesPerPack = 16;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient Http = CreateSafeHttpClient(RequestTimeout);
    private static readonly Regex Sha256Pattern = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };

    private static List<AkronCommunityPackEntry> cachedEntries = new List<AkronCommunityPackEntry>();
    private static DateTime? cachedFetchedUtc;
    private static string lastStatus = "Not connected.";
    private static Task<RefreshOutcome> refreshTask;
    private static Task<DownloadOutcome> downloadTask;
    private static bool refreshInProgress;
    private static bool downloadInProgress;
    private static Func<string> setupDirectoryProvider = AkronSetupPacks.GetSetupDirectory;

    public static AkronCommunityPackSearchResult Search(AkronCommunityPackFilter filter) {
        CompleteRefreshIfReady();
        return new AkronCommunityPackSearchResult(Filter(cachedEntries, filter).ToList(), lastStatus, cachedFetchedUtc);
    }

    public static bool RefreshInProgress {
        get {
            CompleteRefreshIfReady();
            return refreshInProgress;
        }
    }

    public static string ResolveIndexUrl(string indexUrl) {
        return string.IsNullOrWhiteSpace(indexUrl) ? DefaultIndexUrl : indexUrl.Trim();
    }

    public static void BeginRefresh(string indexUrl) {
        if (refreshInProgress) {
            return;
        }

        refreshInProgress = true;
        lastStatus = "Connecting...";
        refreshTask = Task.Run(() => RefreshCore(indexUrl));
    }

    public static bool BeginDownload(AkronCommunityPackEntry entry, out string message) {
        CompleteDownloadIfReady(out _, out _, out _);
        message = string.Empty;
        if (downloadInProgress) {
            message = "Download already in progress.";
            return false;
        }

        if (entry == null || string.IsNullOrWhiteSpace(entry.DownloadUrl)) {
            message = "No download URL.";
            return false;
        }

        downloadInProgress = true;
        downloadTask = Task.Run(() => DownloadCore(entry));
        return true;
    }

    public static bool CompleteDownloadIfReady(out AkronCommunityPackEntry entry, out string path, out string message) {
        entry = null;
        path = string.Empty;
        message = string.Empty;
        if (downloadTask == null || !downloadTask.IsCompleted) {
            return false;
        }

        DownloadOutcome outcome = downloadTask.GetAwaiter().GetResult();
        downloadTask = null;
        downloadInProgress = false;
        entry = outcome.Entry;
        path = outcome.Path;
        message = outcome.Message;
        return outcome.Success;
    }

    public static AkronCommunityPackSearchResult Refresh(string indexUrl, AkronCommunityPackFilter filter) {
        RefreshOutcome outcome = RefreshCore(indexUrl);
        ApplyRefreshOutcome(outcome);
        CompleteRefreshIfReady();
        return Search(filter);
    }

    public static bool Import(AkronCommunityPackEntry entry, out string message) {
        message = string.Empty;
        try {
            DownloadOutcome outcome = DownloadCore(entry);
            if (!outcome.Success) {
                message = outcome.Message;
                return false;
            }

            bool imported = AkronSetupPacks.Import(outcome.Path, entry.Section);
            message = imported ? "Imported " + entry.Title + "." : "Import failed.";
            return imported;
        } catch (Exception exception) when (exception is IOException || exception is HttpRequestException || exception is TaskCanceledException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException || exception is FormatException) {
            message = exception.Message;
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron community pack: " + exception);
            Engine.Scene?.Add(new AkronToast("Community import failed."));
            return false;
        }
    }

    internal static void SetSetupDirectoryProviderForTest(Func<string> provider) {
        setupDirectoryProvider = provider ?? AkronSetupPacks.GetSetupDirectory;
    }

    internal static bool CompleteRefreshForTesting(TimeSpan timeout) {
        Task<RefreshOutcome> task = refreshTask;
        if (task == null) {
            refreshInProgress = false;
            return true;
        }

        if (!task.Wait(timeout)) {
            return false;
        }

        ApplyRefreshOutcome(task.GetAwaiter().GetResult());
        refreshTask = null;
        refreshInProgress = false;
        return true;
    }

    private static RefreshOutcome RefreshCore(string indexUrl) {
        try {
            List<AkronCommunityPackEntry> entries = LoadIndex(indexUrl).Packs
                .Where(IsUsableEntry)
                .OrderByDescending(entry => ParseUtc(entry.UpdatedUtc) ?? DateTime.MinValue)
                .ThenBy(entry => entry.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new RefreshOutcome(
                entries,
                DateTime.UtcNow,
                "Connected. " + entries.Count.ToString(CultureInfo.InvariantCulture) + " pack(s) indexed.");
        } catch (Exception exception) when (exception is IOException || exception is JsonException || exception is InvalidDataException || exception is HttpRequestException || exception is TaskCanceledException || exception is ArgumentException || exception is FormatException) {
            return new RefreshOutcome(cachedEntries, cachedFetchedUtc, "Connection failed: " + exception.Message);
        }
    }

    private static DownloadOutcome DownloadCore(AkronCommunityPackEntry entry) {
        if (entry == null || string.IsNullOrWhiteSpace(entry.DownloadUrl)) {
            return new DownloadOutcome(entry, string.Empty, "No download URL.", false);
        }

        try {
            Directory.CreateDirectory(setupDirectoryProvider());
            string fileName = SanitizeFileName(string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title) + AkronArchive.Extension;
            string path = Path.Combine(setupDirectoryProvider(), "community-" + fileName);
            try {
                DownloadPack(entry, path);
                AkronSetupPack pack = AkronSetupPacks.Read(path, out AkronArchiveManifest manifest);
                if (pack.Section != entry.Section || !AkronSetupPacks.IsCommunitySection(pack.Section)) {
                    throw new InvalidDataException("Pack section does not match the catalog.");
                }
                if (!string.IsNullOrWhiteSpace(entry.MapSid) &&
                    !string.Equals(entry.MapSid, manifest.Target?.MapSid, StringComparison.Ordinal)) {
                    throw new InvalidDataException("Pack map does not match the catalog.");
                }
            } catch {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
                throw;
            }
            return new DownloadOutcome(entry, path, "Downloaded " + entry.Title + ".", true);
        } catch (Exception exception) when (exception is IOException || exception is HttpRequestException || exception is TaskCanceledException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException || exception is FormatException) {
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron community pack: " + exception);
            return new DownloadOutcome(entry, string.Empty, exception.Message, false);
        }
    }

    private static void CompleteRefreshIfReady() {
        if (refreshTask == null || !refreshTask.IsCompleted) {
            return;
        }

        ApplyRefreshOutcome(refreshTask.GetAwaiter().GetResult());
        refreshTask = null;
        refreshInProgress = false;
    }

    private static void ApplyRefreshOutcome(RefreshOutcome outcome) {
        cachedEntries = outcome.Entries.ToList();
        cachedFetchedUtc = outcome.FetchedUtc;
        lastStatus = outcome.Status;
    }

    public static AkronCommunityPackIndex ParseIndex(string json) {
        if (string.IsNullOrWhiteSpace(json)) {
            throw new InvalidDataException("Community index is empty.");
        }

        AkronCommunityPackIndex index = JsonSerializer.Deserialize<AkronCommunityPackIndex>(json, JsonOptions)
            ?? throw new InvalidDataException("Community index is invalid.");
        if (!string.Equals(index.Format, IndexFormat, StringComparison.Ordinal)) {
            throw new InvalidDataException("Community index format is unsupported.");
        }

        if (index.Version != IndexVersion) {
            throw new InvalidDataException("Community index version is unsupported.");
        }

        index.Packs ??= new List<AkronCommunityPackEntry>();
        if (index.Packs.Count > MaxCatalogPacks) {
            throw new InvalidDataException("Community index has too many packs.");
        }
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (AkronCommunityPackEntry entry in index.Packs) {
            ValidateCatalogEntry(entry);
            if (!ids.Add(entry.Id)) {
                throw new InvalidDataException("Community index contains a duplicate pack id.");
            }
        }
        return index;
    }

    public static IEnumerable<AkronCommunityPackEntry> Filter(IEnumerable<AkronCommunityPackEntry> entries, AkronCommunityPackFilter filter) {
        filter ??= new AkronCommunityPackFilter();
        string mapSid = (filter.MapSid ?? string.Empty).Trim();
        string query = (filter.Query ?? string.Empty).Trim();
        AkronSetupSection section = filter.Section;

        IEnumerable<AkronCommunityPackEntry> filtered = (entries ?? Enumerable.Empty<AkronCommunityPackEntry>())
            .Where(IsUsableEntry);

        if (!string.IsNullOrWhiteSpace(mapSid)) {
            filtered = filtered.Where(entry => string.Equals(entry.MapSid, mapSid, StringComparison.OrdinalIgnoreCase));
        }

        if (section != AkronSetupSection.Whole) {
            filtered = filtered.Where(entry => entry.Section == section);
        }

        foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            string current = token;
            filtered = filtered.Where(entry => EntryContains(entry, current));
        }

        return filtered;
    }

    public static string DescribeCategory(AkronSetupSection section) {
        return section == AkronSetupSection.Whole ? "All" : AkronSetupPacks.FormatSection(section);
    }

    public static IReadOnlyList<AkronCommunityPackImage> GetPreviewImages(AkronCommunityPackEntry entry) {
        if (entry == null) {
            return Array.Empty<AkronCommunityPackImage>();
        }

        List<AkronCommunityPackImage> images = new List<AkronCommunityPackImage>();
        foreach (AkronCommunityPackImage image in entry.Images ?? new List<AkronCommunityPackImage>()) {
            if (!string.IsNullOrWhiteSpace(image?.Url)) {
                images.Add(new AkronCommunityPackImage {
                    Url = image.Url.Trim(),
                    RoomName = image.RoomName?.Trim() ?? string.Empty
                });
            }
        }

        foreach (string imageUrl in entry.ImageUrls ?? new List<string>()) {
            if (!string.IsNullOrWhiteSpace(imageUrl)) {
                images.Add(new AkronCommunityPackImage {
                    Url = imageUrl.Trim(),
                    RoomName = string.Empty
                });
            }
        }

        if (!string.IsNullOrWhiteSpace(entry.ImageUrl)) {
            images.Add(new AkronCommunityPackImage {
                Url = entry.ImageUrl.Trim(),
                RoomName = string.Empty
            });
        }

        return images
            .GroupBy(image => image.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static AkronCommunityPackIndex LoadIndex(string indexUrl) {
        Uri uri = new Uri(ResolveIndexUrl(indexUrl), UriKind.Absolute);
        string json;
        if (uri.Scheme == Uri.UriSchemeFile) {
            ValidateLocalFileUri(uri, "Community index");
            byte[] bytes = ReadFileBytesCapped(uri.LocalPath, MaxIndexBytes, "Community index is too large.");
            json = System.Text.Encoding.UTF8.GetString(bytes);
        } else if (uri.Scheme == Uri.UriSchemeHttps) {
            ValidatePublicHttpsUri(uri, "Community index");
            byte[] bytes = ReadHttpBytesCapped(uri, MaxIndexBytes, "Community index is too large.");
            json = System.Text.Encoding.UTF8.GetString(bytes);
        } else {
            throw new InvalidDataException("Remote community indexes must use HTTPS; local indexes must use file URLs.");
        }

        if (json.Length > MaxIndexBytes) {
            throw new InvalidDataException("Community index is too large.");
        }

        AkronCommunityPackIndex index = ParseIndex(json);
        foreach (AkronCommunityPackEntry entry in index.Packs) {
            entry.CatalogUri = uri;
            ResolveCatalogResourceUri(entry, entry.DownloadUrl, "Pack");
            foreach (AkronCommunityPackImage image in GetPreviewImages(entry)) {
                ResolveCatalogResourceUri(entry, image.Url, "Preview image");
            }
        }
        return index;
    }

    private static void DownloadPack(AkronCommunityPackEntry entry, string destinationPath) {
        ValidateCatalogEntry(entry);
        Uri uri = ResolveCatalogResourceUri(entry, entry.DownloadUrl, "Pack");
        byte[] bytes;
        if (uri.Scheme == Uri.UriSchemeFile) {
            bytes = ReadFileBytesCapped(uri.LocalPath, MaxPackBytes, "Pack is too large.");
        } else if (uri.Scheme == Uri.UriSchemeHttps) {
            bytes = ReadHttpBytesCapped(uri, MaxPackBytes, "Pack is too large.");
        } else {
            throw new InvalidDataException("Pack URL is not allowed by its catalog.");
        }

        if (bytes.Length > MaxPackBytes || bytes.Length != entry.SizeBytes) {
            throw new InvalidDataException("Pack size does not match the catalog.");
        }

        byte[] expectedHash = Convert.FromHexString(entry.Sha256);
        byte[] actualHash = SHA256.HashData(bytes);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash)) {
            throw new InvalidDataException("Pack checksum does not match the catalog.");
        }

        File.WriteAllBytes(destinationPath, bytes);
    }

    private static byte[] ReadFileBytesCapped(string path, int maxBytes, string tooLargeMessage) {
        FileInfo file = new FileInfo(path);
        if (file.Length > maxBytes) {
            throw new InvalidDataException(tooLargeMessage);
        }

        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > maxBytes) {
            throw new InvalidDataException(tooLargeMessage);
        }
        return bytes;
    }

    private static byte[] ReadHttpBytesCapped(Uri uri, int maxBytes, string tooLargeMessage) {
        ValidatePublicHttpsUri(uri, "Remote resource");
        using HttpResponseMessage response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        if ((int)response.StatusCode is >= 300 and < 400) {
            throw new InvalidDataException("Remote redirects are not allowed.");
        }
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maxBytes) {
            throw new InvalidDataException(tooLargeMessage);
        }

        using Stream stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
        using MemoryStream buffer = new MemoryStream(Math.Min(maxBytes, 81920));
        byte[] chunk = new byte[81920];
        int total = 0;
        while (true) {
            int read = stream.Read(chunk, 0, Math.Min(chunk.Length, maxBytes + 1 - total));
            if (read == 0) {
                return buffer.ToArray();
            }

            total += read;
            if (total > maxBytes) {
                throw new InvalidDataException(tooLargeMessage);
            }

            buffer.Write(chunk, 0, read);
        }
    }

    private static bool IsUsableEntry(AkronCommunityPackEntry entry) {
        return entry != null &&
               !string.IsNullOrWhiteSpace(entry.Id) &&
               !string.IsNullOrWhiteSpace(entry.Title) &&
               !string.IsNullOrWhiteSpace(entry.DownloadUrl);
    }

    private static void ValidateCatalogEntry(AkronCommunityPackEntry entry) {
        if (!IsUsableEntry(entry) || !AkronSetupPacks.IsCommunitySection(entry.Section)) {
            throw new InvalidDataException("Community index contains an invalid pack entry.");
        }

        if (!Sha256Pattern.IsMatch(entry.Sha256 ?? string.Empty)) {
            throw new InvalidDataException("Community pack SHA-256 is missing or invalid.");
        }

        if (entry.SizeBytes <= 0) {
            throw new InvalidDataException("Community pack size is missing or invalid.");
        }

        if (entry.SizeBytes > MaxPackBytes) {
            throw new InvalidDataException("Pack is too large.");
        }

        if ((entry.Tags?.Count ?? 0) > 32) {
            throw new InvalidDataException("Community pack has too many tags.");
        }
        if ((entry.ImageUrls?.Count ?? 0) > MaxCatalogImagesPerPack || (entry.Images?.Count ?? 0) > MaxCatalogImagesPerPack) {
            throw new InvalidDataException("Community pack has too many preview images.");
        }

        ValidateCatalogText(entry.Id, 256, "pack id");
        ValidateCatalogText(entry.Title, 256, "pack title");
        ValidateCatalogText(entry.MapSid, 256, "map SID");
        ValidateOptionalCatalogText(entry.Description, 4096, "description");
        ValidateOptionalCatalogText(entry.AuthorName, 256, "author name");
        ValidateOptionalCatalogText(entry.MapUrl, 2048, "map URL");
        ValidateCatalogText(entry.DownloadUrl, 2048, "download URL");
        ValidateOptionalCatalogText(entry.ImageUrl, 2048, "preview URL");
        ValidateOptionalCatalogText(entry.AuthorAvatarUrl, 2048, "author avatar URL");
        foreach (string tag in entry.Tags ?? new List<string>()) {
            ValidateCatalogText(tag, 64, "tag");
        }
        foreach (string imageUrl in entry.ImageUrls ?? new List<string>()) {
            ValidateCatalogText(imageUrl, 2048, "preview URL");
        }
        foreach (AkronCommunityPackImage image in entry.Images ?? new List<AkronCommunityPackImage>()) {
            if (image == null) {
                throw new InvalidDataException("Community pack preview image is invalid.");
            }
            ValidateCatalogText(image.Url, 2048, "preview URL");
            ValidateOptionalCatalogText(image.RoomName, 256, "preview room name");
        }
    }

    private static void ValidateCatalogText(string value, int maximum, string label) {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) {
            throw new InvalidDataException("Community pack " + label + " is invalid.");
        }
    }

    private static void ValidateOptionalCatalogText(string value, int maximum, string label) {
        if ((value?.Length ?? 0) > maximum) {
            throw new InvalidDataException("Community pack " + label + " is too long.");
        }
    }

    internal static Uri ResolveCatalogResourceUri(AkronCommunityPackEntry entry, string resourceUrl, string label) {
        if (entry == null || string.IsNullOrWhiteSpace(resourceUrl)) {
            throw new InvalidDataException(label + " URL is missing.");
        }

        Uri resource = new Uri(resourceUrl, UriKind.Absolute);
        Uri catalog = entry.CatalogUri;
        if (catalog == null) {
            if (resource.Scheme == Uri.UriSchemeFile) {
                ValidateLocalFileUri(resource, label);
                return resource;
            }

            Uri officialCatalog = new Uri(DefaultIndexUrl, UriKind.Absolute);
            ValidateHttpsUriShape(resource, label);
            if (!HasSameOrigin(resource, officialCatalog)) {
                throw new InvalidDataException(label + " URL is outside the approved catalog origin.");
            }
            return resource;
        }

        if (catalog.Scheme == Uri.UriSchemeFile) {
            ValidateLocalFileUri(resource, label);
            string catalogDirectory = Path.GetDirectoryName(Path.GetFullPath(catalog.LocalPath)) ?? Path.GetPathRoot(catalog.LocalPath);
            string resourcePath = Path.GetFullPath(resource.LocalPath);
            string prefix = catalogDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!resourcePath.StartsWith(prefix, StringComparison.Ordinal)) {
                throw new InvalidDataException(label + " file is outside the local catalog directory.");
            }
            return resource;
        }

        ValidateHttpsUriShape(resource, label);
        if (!HasSameOrigin(resource, catalog)) {
            throw new InvalidDataException(label + " URL is outside the approved catalog origin.");
        }
        return resource;
    }

    private static bool HasSameOrigin(Uri left, Uri right) {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(left.IdnHost, right.IdnHost, StringComparison.OrdinalIgnoreCase) &&
               left.Port == right.Port;
    }

    private static void ValidateLocalFileUri(Uri uri, string label) {
        if (uri.Scheme != Uri.UriSchemeFile || uri.IsUnc || !string.IsNullOrEmpty(uri.Host)) {
            throw new InvalidDataException(label + " must be an explicit local file URL.");
        }
    }

    private static void ValidatePublicHttpsUri(Uri uri, string label) {
        ValidateHttpsUriShape(uri, label);

        IPAddress[] addresses;
        try {
            addresses = Dns.GetHostAddresses(uri.DnsSafeHost);
        } catch (Exception exception) when (exception is System.Net.Sockets.SocketException || exception is ArgumentException) {
            throw new InvalidDataException(label + " host could not be resolved.", exception);
        }

        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress)) {
            throw new InvalidDataException(label + " host resolves to a private or local address.");
        }
    }

    private static void ValidateHttpsUriShape(Uri uri, string label) {
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || string.IsNullOrWhiteSpace(uri.IdnHost)) {
            throw new InvalidDataException(label + " must use HTTPS.");
        }
    }

    private static bool IsUnsafeAddress(IPAddress address) {
        if (address.IsIPv4MappedToIPv6) {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None) || address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast || address.IsIPv6SiteLocal) {
            return true;
        }

        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length == 4) {
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 169 && bytes[1] == 254) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 198 && bytes[1] is 18 or 19) ||
                   (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                   (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113) ||
                   bytes[0] >= 224 ||
                   bytes[0] == 0;
        }

        return bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC;
    }

    internal static HttpClient CreateSafeHttpClient(TimeSpan timeout) {
        SocketsHttpHandler handler = new SocketsHttpHandler {
            AllowAutoRedirect = false,
            ConnectCallback = ConnectToPublicEndpoint
        };
        return new HttpClient(handler) { Timeout = timeout };
    }

    private static async ValueTask<Stream> ConnectToPublicEndpoint(SocketsHttpConnectionContext context, CancellationToken cancellationToken) {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(IsUnsafeAddress)) {
            throw new HttpRequestException("Remote host resolves to a private or local address.");
        }

        Exception lastError = null;
        foreach (IPAddress address in addresses) {
            Socket socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            } catch (Exception exception) when (exception is SocketException || exception is OperationCanceledException) {
                socket.Dispose();
                lastError = exception;
                if (exception is OperationCanceledException) {
                    throw;
                }
            }
        }

        throw new HttpRequestException("Could not connect to the remote host.", lastError);
    }

    private static bool EntryContains(AkronCommunityPackEntry entry, string token) {
        return Contains(entry.Title, token) ||
               Contains(entry.Description, token) ||
               Contains(entry.AuthorName, token) ||
               Contains(entry.MapUrl, token) ||
               Contains(AkronSetupPacks.FormatSection(entry.Section), token) ||
               (entry.Tags ?? new List<string>()).Any(tag => Contains(tag, token));
    }

    private static bool Contains(string value, string token) {
        return (value ?? string.Empty).IndexOf(token ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static DateTime? ParseUtc(string value) {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static string SanitizeFileName(string value) {
        string name = string.IsNullOrWhiteSpace(value) ? "community-pack" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars()) {
            name = name.Replace(invalid, '-');
        }

        return name;
    }

    private sealed class RefreshOutcome {
        public RefreshOutcome(IReadOnlyList<AkronCommunityPackEntry> entries, DateTime? fetchedUtc, string status) {
            Entries = entries ?? Array.Empty<AkronCommunityPackEntry>();
            FetchedUtc = fetchedUtc;
            Status = status ?? string.Empty;
        }

        public IReadOnlyList<AkronCommunityPackEntry> Entries { get; }
        public DateTime? FetchedUtc { get; }
        public string Status { get; }
    }

    private sealed class DownloadOutcome {
        public DownloadOutcome(AkronCommunityPackEntry entry, string path, string message, bool success) {
            Entry = entry;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
            Success = success;
        }

        public AkronCommunityPackEntry Entry { get; }
        public string Path { get; }
        public string Message { get; }
        public bool Success { get; }
    }
}

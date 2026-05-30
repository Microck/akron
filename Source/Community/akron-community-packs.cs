using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public sealed class AkronCommunityPackIndex {
    public string Format { get; set; } = AkronCommunityPacks.IndexFormat;
    public int Version { get; set; } = 1;
    public List<AkronCommunityPackEntry> Packs { get; set; } = new List<AkronCommunityPackEntry>();
}

public sealed class AkronCommunityPackEntry {
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AkronProfileSection Section { get; set; } = AkronProfileSection.Whole;
    public string MapSid { get; set; } = string.Empty;
    public string MapUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorAvatarUrl { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public int DownloadCount { get; set; }
    public string UpdatedUtc { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new List<string>();
}

public sealed class AkronCommunityPackFilter {
    public string MapSid { get; set; } = string.Empty;
    public AkronProfileSection Section { get; set; } = AkronProfileSection.Whole;
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
    public const string IndexFormat = "akron-community-pack-index-v1";
    public const string DefaultIndexUrl = "https://akron.micr.dev/catalog/index.json";

    private const int MaxIndexBytes = 1024 * 1024;
    private const int MaxPackBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly HttpClient Http = new HttpClient {
        Timeout = RequestTimeout
    };
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static List<AkronCommunityPackEntry> cachedEntries = new List<AkronCommunityPackEntry>();
    private static DateTime? cachedFetchedUtc;
    private static string lastStatus = "Not connected.";
    private static Task<RefreshOutcome> refreshTask;
    private static Task<DownloadOutcome> downloadTask;
    private static bool refreshInProgress;
    private static bool downloadInProgress;
    private static Func<string> profileDirectoryProvider = AkronProfilePacks.GetProfileDirectory;

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

            bool imported = AkronProfilePacks.Import(outcome.Path, entry.Section);
            message = imported ? "Imported " + entry.Title + "." : "Import failed.";
            return imported;
        } catch (Exception exception) when (exception is IOException || exception is HttpRequestException || exception is TaskCanceledException || exception is UnauthorizedAccessException || exception is InvalidDataException || exception is ArgumentException || exception is FormatException) {
            message = exception.Message;
            Logger.Log(LogLevel.Warn, nameof(AkronModule), "Failed to import Akron community pack: " + exception);
            Engine.Scene?.Add(new AkronToast("Community import failed."));
            return false;
        }
    }

    internal static void SetProfileDirectoryProviderForTest(Func<string> provider) {
        profileDirectoryProvider = provider ?? AkronProfilePacks.GetProfileDirectory;
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
            Directory.CreateDirectory(profileDirectoryProvider());
            string fileName = SanitizeFileName(string.IsNullOrWhiteSpace(entry.Title) ? entry.Id : entry.Title) + AkronArchive.Extension;
            string path = Path.Combine(profileDirectoryProvider(), "community-" + fileName);
            DownloadPack(entry.DownloadUrl, path);
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

        index.Packs ??= new List<AkronCommunityPackEntry>();
        return index;
    }

    public static IEnumerable<AkronCommunityPackEntry> Filter(IEnumerable<AkronCommunityPackEntry> entries, AkronCommunityPackFilter filter) {
        filter ??= new AkronCommunityPackFilter();
        string mapSid = (filter.MapSid ?? string.Empty).Trim();
        string query = (filter.Query ?? string.Empty).Trim();
        AkronProfileSection section = filter.Section;

        IEnumerable<AkronCommunityPackEntry> filtered = (entries ?? Enumerable.Empty<AkronCommunityPackEntry>())
            .Where(IsUsableEntry);

        if (!string.IsNullOrWhiteSpace(mapSid)) {
            filtered = filtered.Where(entry => string.Equals(entry.MapSid, mapSid, StringComparison.OrdinalIgnoreCase));
        }

        if (section != AkronProfileSection.Whole) {
            filtered = filtered.Where(entry => entry.Section == section);
        }

        foreach (string token in query.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) {
            string current = token;
            filtered = filtered.Where(entry => EntryContains(entry, current));
        }

        return filtered;
    }

    public static string DescribeCategory(AkronProfileSection section) {
        return section == AkronProfileSection.Whole ? "All" : AkronProfilePacks.FormatSection(section);
    }

    private static AkronCommunityPackIndex LoadIndex(string indexUrl) {
        Uri uri = new Uri(ResolveIndexUrl(indexUrl), UriKind.Absolute);
        string json;
        if (uri.Scheme == Uri.UriSchemeFile) {
            byte[] bytes = ReadFileBytesCapped(uri.LocalPath, MaxIndexBytes, "Community index is too large.");
            json = System.Text.Encoding.UTF8.GetString(bytes);
        } else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) {
            byte[] bytes = ReadHttpBytesCapped(uri, MaxIndexBytes, "Community index is too large.");
            json = System.Text.Encoding.UTF8.GetString(bytes);
        } else {
            throw new InvalidDataException("Community index URL must use http, https, or file.");
        }

        if (json.Length > MaxIndexBytes) {
            throw new InvalidDataException("Community index is too large.");
        }

        return ParseIndex(json);
    }

    private static void DownloadPack(string url, string destinationPath) {
        Uri uri = new Uri(url, UriKind.Absolute);
        byte[] bytes;
        if (uri.Scheme == Uri.UriSchemeFile) {
            bytes = ReadFileBytesCapped(uri.LocalPath, MaxPackBytes, "Pack is too large.");
        } else if (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) {
            bytes = ReadHttpBytesCapped(uri, MaxPackBytes, "Pack is too large.");
        } else {
            throw new InvalidDataException("Pack URL must use http, https, or file.");
        }

        if (bytes.Length > MaxPackBytes) {
            throw new InvalidDataException("Pack is too large.");
        }

        File.WriteAllBytes(destinationPath, bytes);
    }

    private static byte[] ReadFileBytesCapped(string path, int maxBytes, string tooLargeMessage) {
        FileInfo file = new FileInfo(path);
        if (file.Length > maxBytes) {
            throw new InvalidDataException(tooLargeMessage);
        }

        return File.ReadAllBytes(path);
    }

    private static byte[] ReadHttpBytesCapped(Uri uri, int maxBytes, string tooLargeMessage) {
        using HttpResponseMessage response = Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
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

    private static bool EntryContains(AkronCommunityPackEntry entry, string token) {
        return Contains(entry.Title, token) ||
               Contains(entry.Description, token) ||
               Contains(entry.AuthorName, token) ||
               Contains(entry.MapUrl, token) ||
               Contains(AkronProfilePacks.FormatSection(entry.Section), token) ||
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Win32;
using Monocle;

namespace Celeste.Mod.Akron;

internal sealed class AkronDiagnosticReport {
    public int SchemaVersion { get; set; } = 1;
    public string ReportId { get; set; }
    public string CreatedUtc { get; set; }
    public string Description { get; set; } = string.Empty;
    public Dictionary<string, string> Context { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, string> Versions { get; set; } = new Dictionary<string, string>();
    public Dictionary<string, string> System { get; set; } = new Dictionary<string, string>();
    public List<AkronDiagnosticMod> Mods { get; set; } = new List<AkronDiagnosticMod>();
    public List<AkronDiagnosticLog> Logs { get; set; } = new List<AkronDiagnosticLog>();
}

internal sealed class AkronDiagnosticMod {
    public string Name { get; set; }
    public string Version { get; set; }
}

internal sealed class AkronDiagnosticLog {
    public string Name { get; set; }
    public string Text { get; set; }
    public bool Truncated { get; set; }
}

internal sealed record AkronDiagnosticStatus(string Phase, string Message, string ReportId = "") {
    public bool Busy => Phase is "collecting" or "uploading" or "canceling";
}

internal static class AkronDiagnostics {
    internal const int MaxRequestBytes = 4 * 1024 * 1024;
    internal const int MaxLogBytes = 1024 * 1024;
    internal const int MaxDescriptionLength = 4000;
    private const int MaxResponseBytes = 4096;
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly object UploadLock = new object();
    private static CancellationTokenSource activeCancellation;
    private static AkronDiagnosticStatus status = new AkronDiagnosticStatus("idle", "No diagnostics have been sent.");
    // These patterns run only on the upload worker. A timeout aborts the report rather than sending unredacted text.
    private static readonly Regex AuthenticationScheme = Pattern(@"\b(?:Bearer|Basic)[ \t]+[A-Za-z0-9._~+/-]+=*");
    private static readonly Regex SensitiveLine = Pattern(@"^.*(?:password|passwd|pwd\s*[:=]|secret|token|authorization|authentication|cookie|api[ _-]?key|access[ _-]?key|private[ _-]?key|credential|connection[ _-]?string|username|user[ _-]?name|hostname|machine[ _-]?name|profile[ _-]?name|save[ _-]?name|environment\s*[:=]).*$", RegexOptions.Multiline);
    private static readonly Regex SensitiveContinuation = Pattern(@"^.*(?:password|passwd|pwd|secret|token|authorization|cookie|api[ _-]?key|access[ _-]?key|credential)[^\r\n]*[:=][ \t]*\r?\n[^\r\n]*", RegexOptions.Multiline);
    private static readonly Regex PrivateKey = Pattern(@"-----BEGIN [^-]*PRIVATE KEY-----[\s\S]*?(?:-----END [^-]*PRIVATE KEY-----|\z)|(?:^[A-Za-z0-9+/=]+\r?\n)+-----END [^-]*PRIVATE KEY-----", RegexOptions.Multiline);
    private static readonly Regex Url = Pattern(@"\b(?:https?|wss?|ftp|file)://[^\s<>""']+");
    private static readonly Regex WindowsPath = Pattern(@"(?:[a-z]:[\\/]|\\\\)[^\r\n""<>|]*");
    private static readonly Regex UnixPath = Pattern(@"(?<![a-z0-9])/(?:home|Users|root|tmp|var|opt|mnt|media|run|private|Applications|Volumes|usr|data|app)(?:/[^\r\n""<>]*)?");
    private static readonly Regex Email = Pattern(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b");
    private static readonly Regex Address = Pattern(@"\b(?:[0-9]{1,3}\.){3}[0-9]{1,3}\b|(?<!\w)(?:[0-9a-f]{0,4}:){2,}[0-9a-f:.]*(?!\w)");
    private static readonly Regex EncodedSecret = Pattern(@"\b(?:eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+|gh[pousr]_[A-Za-z0-9_]+|github_pat_[A-Za-z0-9_]+|AKIA[A-Z0-9]{16}|[A-Za-z0-9+/=_-]{48,})\b");

    internal static AkronDiagnosticStatus Status => Volatile.Read(ref status);

    internal static string ResolveEndpoint(string uploadBase) {
        string endpoint = AkronCommunityPackUploads.ResolveEndpoint(uploadBase) + "/diagnostics";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            string.IsNullOrEmpty(uri.IdnHost) || endpoint.Length > 180) {
            throw new InvalidDataException("Set the community upload endpoint to an HTTPS URL without credentials, query, or fragment.");
        }
        return uri.AbsoluteUri;
    }

    // Called only by the consent menu on the game thread. No live game objects leave this method.
    internal static bool StartConsentedUpload(string endpoint, string description) {
        lock (UploadLock) {
            if (activeCancellation != null) return false;
            string id = Guid.NewGuid().ToString("N");
            try {
                Snapshot snapshot = CaptureSnapshot(id, description);
                CancellationTokenSource cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                activeCancellation = cancellation;
                SetStatus("collecting", "Reading and redacting bounded log tails...", id);
                _ = Task.Run(() => CollectAndUploadAsync(snapshot, endpoint, cancellation));
                return true;
            } catch (Exception) {
                SetStatus("failed", "Diagnostics could not capture game details. Reopen this menu and try again, or share the report ID with a maintainer.", id);
                return false;
            }
        }
    }

    internal static void Cancel() {
        lock (UploadLock) {
            if (activeCancellation == null || !Status.Busy) return;
            SetStatus("canceling", "Canceling the request. The server may already have received it.", Status.ReportId);
            activeCancellation.Cancel();
        }
    }

    private static void SetStatus(string phase, string message, string id) {
        lock (UploadLock) {
            Volatile.Write(ref status, new AkronDiagnosticStatus(phase, message, id));
        }
    }

    private static Snapshot CaptureSnapshot(string id, string description) {
        AkronDiagnosticReport report = new AkronDiagnosticReport {
            ReportId = id,
            CreatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Description = description
        };
        report.Versions["celeste"] = Celeste.Instance?.Version?.ToString() ?? "unknown";
        report.Versions["akron"] = AkronModule.Instance?.Metadata?.VersionString ?? "unknown";
        report.Versions["akronBuild"] = typeof(AkronModule).Assembly.ManifestModule.ModuleVersionId.ToString("N");
        report.Versions["everest"] = typeof(Everest).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
            typeof(Everest).Assembly.GetName().Version?.ToString() ?? "unknown";
        foreach (EverestModule module in Everest.Modules) {
            if (module?.Metadata == null) continue;
            if (report.Mods.Count == 1024) {
                report.Context["modsTruncated"] = "true";
                break;
            }
            report.Mods.Add(new AkronDiagnosticMod { Name = Limit(module.Metadata.Name, 256), Version = Limit(module.Metadata.VersionString, 256) });
            if (string.Equals(module.Metadata.Name, "Everest", StringComparison.OrdinalIgnoreCase)) {
                report.Versions["everest"] = module.Metadata.VersionString;
            }
        }
        report.Context["scene"] = Engine.Scene?.GetType().Name ?? "none";
        if (Engine.Scene is Level level && level.Session != null) {
            report.Context["mapSid"] = level.Session.Area.GetSID() ?? "";
            report.Context["room"] = level.Session.Level ?? "";
            report.Context["mode"] = level.Session.Area.Mode.ToString();
            report.Context["paused"] = level.Paused.ToString();
            report.Context["transitioning"] = level.Transitioning.ToString();
        }
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        if (settings != null) {
            report.Context["safeMode"] = settings.SafeMode.ToString();
            report.Context["logging"] = settings.Logging.ToString();
            report.Context["loggingLevel"] = settings.LoggingLevel.ToString();
        }
        try { report.System["gpu"] = GraphicsAdapter.DefaultAdapter.Description ?? "unknown"; }
        catch (Exception) { report.System["gpu"] = "unavailable"; }
        return new Snapshot(report, Everest.PathGame, AkronLog.GetLogDirectory(), AkronPerformanceTelemetry.RecordingPath);
    }

    private static async Task CollectAndUploadAsync(Snapshot snapshot, string endpoint, CancellationTokenSource cancellation) {
        string id = snapshot.Report.ReportId;
        try {
            CancellationToken token = cancellation.Token;
            Regex privateValuePattern = CreatePrivateValuePattern(new[] { snapshot.GamePath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName });
            AddSystemDetails(snapshot.Report.System);
            RedactFields(snapshot.Report, privateValuePattern);
            string[] names = { "log.txt", "akron-current.log", "akron-previous.log", "performance.jsonl" };
            string[] paths = {
                Path.Combine(snapshot.GamePath, "log.txt"),
                Path.Combine(snapshot.LogDirectory, "akron-current.log"),
                Path.Combine(snapshot.LogDirectory, "akron-1.log"),
                ResolvePerformancePath(snapshot)
            };
            for (int index = 0; index < paths.Length; index++) {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrEmpty(paths[index])) continue;
                try {
                    AkronDiagnosticLog log = ReadLogTail(snapshot.GamePath, paths[index], names[index], MaxLogBytes, privateValuePattern);
                    snapshot.Report.Logs.Add(log);
                } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException) {
                    snapshot.Report.Context["logUnavailable" + index.ToString(CultureInfo.InvariantCulture)] = names[index];
                }
            }
            byte[] body = SerializeBounded(snapshot.Report);
            token.ThrowIfCancellationRequested();
            SetStatus("uploading", "Uploading diagnostics. Keep this menu open; Cancel stops the request.", id);
            using HttpClient http = AkronCommunityPacks.CreateSafeHttpClient(TimeSpan.FromMinutes(2));
            await SendAsync(http, endpoint, body, id, token).ConfigureAwait(false);
            SetStatus("received", "Report uploaded and queued for Akron's private Discord support channel. Keep the report ID if you need to follow up.", id);
        } catch (OperationCanceledException) {
            SetStatus("canceled", "Upload canceled or timed out. Receipt is unconfirmed; a maintainer can check the report ID. Reopen consent to send a new report.", id);
        } catch (HttpRequestException exception) {
            string reason = exception.StatusCode switch {
                HttpStatusCode.TooManyRequests => "The upload limit was reached. Wait a minute before sending a new report.",
                HttpStatusCode.Conflict => "This report ID already exists. Ask a maintainer to check it, or send a new report.",
                HttpStatusCode.RequestEntityTooLarge => "The service rejected the report size. Share the report ID and update Akron before sending again.",
                HttpStatusCode.NotFound => "The diagnostics service is unavailable at this endpoint. Check the community upload endpoint or contact a maintainer.",
                _ => "Check your connection and community upload endpoint, then open consent to send a new report. A maintainer can check the report ID."
            };
            SetStatus("failed", "Diagnostics receipt is unconfirmed. " + reason, id);
        } catch (Exception) {
            // Exception messages can contain paths, credentials, or server text. Never echo them to the report or UI.
            SetStatus("failed", "Diagnostics could not be safely prepared or its receipt verified. Update Akron or share the report ID with a maintainer. No automatic retry was made.", id);
        } finally {
            lock (UploadLock) {
                if (ReferenceEquals(activeCancellation, cancellation)) activeCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    private static string ResolvePerformancePath(Snapshot snapshot) {
        string directory = Path.GetFullPath(Path.Combine(snapshot.GamePath, "Saves", AkronPerformanceTelemetry.PerfDirectoryName));
        if (!string.IsNullOrEmpty(snapshot.PerformancePath) &&
            string.Equals(Path.GetDirectoryName(Path.GetFullPath(snapshot.PerformancePath)), directory, StringComparison.Ordinal)) {
            return snapshot.PerformancePath;
        }
        if (!Directory.Exists(directory)) return null;
        // Only this recorder's files, never arbitrary JSONL or save files. Enumeration and metadata reads stay on the worker.
        return Directory.EnumerateFiles(directory, "akron-perf-*.jsonl", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path)).MaxBy(file => file.LastWriteTimeUtc)?.FullName;
    }

    internal static AkronDiagnosticLog ReadLogTail(string gamePath, string path, string name, int maxBytes, Regex privateValuePattern) {
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gamePath));
        path = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(root, path);
        if (Path.IsPathRooted(relative) || relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
            throw new IOException("Diagnostic logs must be inside the game directory.");
        }
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        // The game root is trusted. Its ancestors may be Steam links or Wine drive mappings.
        for (string candidate = path; !string.Equals(candidate, root, comparison); candidate = Path.GetDirectoryName(candidate)) {
            if ((File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0) {
                throw new IOException("Linked files are not included in diagnostics.");
            }
        }
        using FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long length = stream.Length;
        int count = (int) Math.Min(length, maxBytes);
        byte[] bytes = new byte[count];
        stream.Seek(Math.Max(0, length - count), SeekOrigin.Begin);
        int read = stream.ReadAtLeast(bytes, count, throwOnEndOfStream: false);
        bool truncated = length > count;
        string text = Encoding.UTF8.GetString(bytes, 0, read);
        if (truncated) {
            // Do not expose a partial credential line or a partial UTF-8 character at the tail boundary.
            int newline = text.IndexOf('\n');
            text = newline < 0 ? string.Empty : text.Substring(newline + 1);
        }
        text = Redact(text, privateValuePattern);
        string bounded = Utf8Tail(text, maxBytes);
        return new AkronDiagnosticLog { Name = name, Text = bounded, Truncated = truncated || bounded.Length != text.Length };
    }

    internal static Regex CreatePrivateValuePattern(string[] privateValues) {
        string[] localValues = (privateValues ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => new[] { value, value.Replace('\\', '/'), value.Replace("\\", "\\\\") })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(value => value.Length)
            .Select(Regex.Escape)
            .ToArray();
        return localValues.Length > 0 ? Pattern(string.Join("|", localValues)) : null;
    }

    internal static string Redact(string text, Regex privateValuePattern, bool redactAddresses = true) {
        text ??= string.Empty;
        text = PrivateKey.Replace(text, "[private key removed]");
        text = AuthenticationScheme.Replace(text, "[credential removed]");
        text = SensitiveContinuation.Replace(text, "[sensitive value removed]");
        text = SensitiveLine.Replace(text, "[sensitive line removed]");
        text = Url.Replace(text, "[url removed]");
        text = WindowsPath.Replace(text, "[path removed]");
        text = UnixPath.Replace(text, "[path removed]");
        text = Email.Replace(text, "[email removed]");
        if (redactAddresses) text = Address.Replace(text, "[address removed]");
        text = EncodedSecret.Replace(text, "[credential removed]");
        if (privateValuePattern != null) {
            // One pass over the input: replacement markers must never become new matches.
            text = privateValuePattern.Replace(text, "[local value removed]");
        }
        return text;
    }

    private static void RedactFields(AkronDiagnosticReport report, Regex privateValuePattern) {
        foreach (Dictionary<string, string> fields in new[] { report.Context, report.Versions, report.System }) {
            foreach (string key in fields.Keys.ToArray()) fields[key] = Limit(Redact(fields[key], privateValuePattern, redactAddresses: false), 1024);
        }
        foreach (AkronDiagnosticMod mod in report.Mods) {
            mod.Name = Limit(Redact(mod.Name, privateValuePattern, redactAddresses: false), 256);
            mod.Version = Limit(Redact(mod.Version, privateValuePattern, redactAddresses: false), 256);
        }
    }

    internal static byte[] SerializeBounded(AkronDiagnosticReport report) {
        foreach (AkronDiagnosticLog log in report.Logs) {
            string text = Utf8Tail(log.Text, MaxLogBytes);
            log.Truncated |= text.Length != log.Text.Length;
            log.Text = text;
        }
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        while (body.Length > MaxRequestBytes) {
            AkronDiagnosticLog largest = report.Logs.OrderByDescending(log => log.Text.Length).FirstOrDefault();
            if (largest == null || largest.Text.Length == 0) throw new InvalidDataException("Diagnostic metadata exceeds the report limit.");
            // Bound the encoded request too: quotes, controls and Unicode can expand in JSON.
            largest.Text = Utf8Tail(largest.Text, Encoding.UTF8.GetByteCount(largest.Text) / 2);
            largest.Truncated = true;
            body = JsonSerializer.SerializeToUtf8Bytes(report, JsonOptions);
        }
        return body;
    }

    private static string Utf8Tail(string text, int maxBytes) {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        int start = text.Length;
        int bytes = 0;
        while (start > 0) {
            int width = start >= 2 && char.IsLowSurrogate(text[start - 1]) && char.IsHighSurrogate(text[start - 2]) ? 2 : 1;
            int size = Encoding.UTF8.GetByteCount(text.AsSpan(start - width, width));
            if (bytes + size > maxBytes) break;
            bytes += size;
            start -= width;
        }
        return text.Substring(start);
    }

    internal static async Task SendAsync(HttpClient http, string endpoint, byte[] body, string reportId, CancellationToken token) {
        if (body.Length > MaxRequestBytes) throw new InvalidDataException("Diagnostic report is too large.");
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) {
            throw new InvalidDataException("Diagnostics require HTTPS.");
        }
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength > MaxResponseBytes) throw new InvalidDataException("Diagnostic response is too large.");
        using Stream stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        byte[] bytes = new byte[MaxResponseBytes + 1];
        int count = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken: token).ConfigureAwait(false);
        if (count > MaxResponseBytes) throw new InvalidDataException("Diagnostic response is too large.");
        if (response.StatusCode != HttpStatusCode.Created) throw new HttpRequestException("Diagnostic upload failed.", null, response.StatusCode);
        using JsonDocument document = JsonDocument.Parse(bytes.AsMemory(0, count));
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("reportId", out JsonElement receivedId) || receivedId.GetString() != reportId ||
            !root.TryGetProperty("status", out JsonElement receivedStatus) || receivedStatus.GetString() != "received") {
            throw new InvalidDataException("Diagnostic receipt does not match this report.");
        }
    }

    private static Regex Pattern(string expression, RegexOptions options = RegexOptions.None) =>
        new Regex(expression, options | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    private static string Limit(string text, int length) {
        text ??= "";
        if (text.Length <= length) return text;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text.Substring(0, length);
    }

    private static void AddSystemDetails(Dictionary<string, string> system) {
        system["os"] = RuntimeInformation.OSDescription;
        system["runtime"] = RuntimeInformation.FrameworkDescription;
        system["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString();
        system["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString();
        system["logicalProcessors"] = Environment.ProcessorCount.ToString(CultureInfo.InvariantCulture);
        system["runtimeMemoryLimitBytes"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes.ToString(CultureInfo.InvariantCulture);
        system["cpu"] = "unavailable";
        system["ramBytes"] = "unavailable";
        try {
            if (OperatingSystem.IsLinux()) {
                string cpuInfo = ReadSystemText("/proc/cpuinfo");
                string model = cpuInfo.Split('\n').FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal) || line.StartsWith("Hardware", StringComparison.Ordinal));
                if (model != null) system["cpu"] = model.Substring(model.IndexOf(':') + 1).Trim();
                string memory = ReadSystemText("/proc/meminfo").Split('\n').FirstOrDefault(line => line.StartsWith("MemTotal:", StringComparison.Ordinal));
                if (memory != null && ulong.TryParse(memory.Substring(9).Replace("kB", "").Trim(), out ulong kib)) system["ramBytes"] = (kib * 1024).ToString(CultureInfo.InvariantCulture);
            } else if (OperatingSystem.IsWindows()) {
                using RegistryKey key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                system["cpu"] = key?.GetValue("ProcessorNameString") as string ?? "unavailable";
                MemoryStatus memory = new MemoryStatus { Length = (uint) Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref memory)) system["ramBytes"] = memory.TotalPhysical.ToString(CultureInfo.InvariantCulture);
            } else if (OperatingSystem.IsMacOS()) {
                system["cpu"] = ReadSysctl("machdep.cpu.brand_string", false);
                system["ramBytes"] = ReadSysctl("hw.memsize", true);
            }
        } catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is System.Security.SecurityException || exception is DllNotFoundException || exception is EntryPointNotFoundException) {
            // Optional hardware fields remain explicitly unavailable; do not fall back to environment variables or shell commands.
        }
    }

    private static string ReadSystemText(string path) {
        using FileStream stream = File.OpenRead(path);
        byte[] bytes = new byte[64 * 1024];
        int count = stream.ReadAtLeast(bytes, bytes.Length, throwOnEndOfStream: false);
        return Encoding.UTF8.GetString(bytes, 0, count);
    }

    private static string ReadSysctl(string key, bool number) {
        byte[] value = new byte[1024];
        nuint length = (nuint) value.Length;
        if (sysctlbyname(key, value, ref length, IntPtr.Zero, 0) != 0) return "unavailable";
        return number && length == 8 ? BitConverter.ToUInt64(value, 0).ToString(CultureInfo.InvariantCulture) : Encoding.UTF8.GetString(value, 0, (int) length).TrimEnd('\0');
    }

    [DllImport("libSystem.B.dylib", SetLastError = true)]
    private static extern int sysctlbyname(string name, byte[] value, ref nuint length, IntPtr newValue, nuint newLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    private sealed record Snapshot(AkronDiagnosticReport Report, string GamePath, string LogDirectory, string PerformancePath);
}

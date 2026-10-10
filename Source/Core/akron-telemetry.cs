using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Sentry;
using Sentry.Protocol;

namespace Celeste.Mod.Akron;

internal enum AkronFailurePhase
{
    Startup,
    Initialize,
    Content,
    Overlay,
    Settings,
    DeferredAction,
    StartPosPersist,
    StartPosApply,
    StartPosCapture,
    StartPosRestore
}

// This client belongs to Akron, not the Celeste process. Never use SentrySdk.Init here:
// other mods own their exceptions, scopes, native handlers and process lifetime.
internal static class AkronTelemetry
{
    private static readonly object Sync = new object();
    private static AkronErrorReporter reporter;
    internal static string Destination { get; private set; } = string.Empty;
    internal static bool IsConfigured => !string.IsNullOrEmpty(Destination);
    internal static bool IsEnabled { get { lock (Sync) return reporter != null; } }

    internal static void Configure(bool consent, string dsn, string release, HttpMessageHandler transport = null)
    {
        Stop(flush: false);
        Destination = AkronErrorReporter.TryGetDestination(dsn, out string host) ? host : string.Empty;
        if (!consent || !IsConfigured) return;
        try
        {
            lock (Sync)
            {
                reporter = new AkronErrorReporter(dsn, release, transport);
            }
        }
        catch (Exception)
        {
            // Reporting must never prevent the mod from loading or leak its configuration to a log.
        }
    }

    internal static string ResolveDsn()
    {
        string configured = Environment.GetEnvironmentVariable("AKRON_SENTRY_DSN");
        return configured ?? typeof(AkronTelemetry).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "AkronSentryDsn")?.Value ?? string.Empty;
    }

    internal static void Capture(Exception exception, AkronFailurePhase phase)
    {
        AkronErrorReporter current;
        lock (Sync) current = reporter;
        current?.Capture(exception, phase);
    }

    internal static void Stop(bool flush = true)
    {
        AkronErrorReporter previous;
        lock (Sync)
        {
            previous = reporter;
            reporter = null;
        }
        previous?.Stop(flush);
    }
}

// Kept independent of live game objects so consent and the actual SDK payload can be tested headlessly.
internal sealed class AkronErrorReporter
{
    internal const int MaxReportsPerSession = 32;
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly Regex Identifier = new Regex(@"^[A-Za-z0-9_.+`<> {}(),\[\]:&?=-]{1,512}$", RegexOptions.CultureInvariant);
    private static readonly Regex HexIdentifier = new Regex(@"^[A-Fa-f0-9-]{1,128}$", RegexOptions.CultureInvariant);
    private readonly object sync = new object();
    private readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
    private readonly CancellationTokenSource revoked = new CancellationTokenSource();
    private readonly SentryClient client;
    private readonly AkronTelemetryWorker worker;
    private readonly string release;
    private Task shutdown = Task.CompletedTask;
    private bool stopped;

    internal AkronErrorReporter(string dsn, string version, HttpMessageHandler transport = null)
    {
        if (!TryGetDestination(dsn, out _)) throw new ArgumentException("A valid HTTPS Sentry DSN is required.");
        release = "akron@" + SafeVersion(version);
        SentryOptions options = new SentryOptions
        {
            Dsn = dsn,
            Release = release,
            Environment = "production",
            SendDefaultPii = false,
            IsEnvironmentUser = false,
            AutoSessionTracking = false,
            AttachStacktrace = false,
            ReportAssembliesMode = ReportAssembliesMode.None,
            StackTraceMode = StackTraceMode.Original,
            MaxBreadcrumbs = 0,
            MaxQueueItems = MaxReportsPerSession,
            TracesSampleRate = 0,
            EnableLogs = false,
            CaptureFailedRequests = false,
            SendClientReports = false,
            DisableFileWrite = true,
            DisableSentryHttpMessageHandler = true,
            ShutdownTimeout = ShutdownTimeout,
            FlushTimeout = ShutdownTimeout
        };
        worker = new AkronTelemetryWorker(options,
            new ConsentHandler(revoked.Token, transport ?? new HttpClientHandler { AllowAutoRedirect = false }));
        options.BackgroundWorker = worker;
        options.AddInAppInclude("Celeste.Mod.Akron.");
        options.SetBeforeSend(Sanitize);
        // The standalone client does not register SDK integrations or change the global hub.
        try { client = new SentryClient(options); }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    internal SentryId Capture(Exception exception, AkronFailurePhase phase)
    {
        if (exception == null || exception is OperationCanceledException || !Enum.IsDefined(phase)) return SentryId.Empty;
        try
        {
            lock (sync)
            {
                if (stopped || reported.Count >= MaxReportsPerSession) return SentryId.Empty;
                string frame = new StackTrace(exception, false).GetFrames()?
                    .Select(value => value.GetMethod())
                    .FirstOrDefault(method => method?.DeclaringType?.Namespace?.StartsWith("Celeste.Mod.Akron", StringComparison.Ordinal) == true)?.Name ?? string.Empty;
                string key = phase + ":" + exception.GetType().FullName + ":" + frame;
                if (!reported.Add(key)) return SentryId.Empty;
                SentryEvent entry = new SentryEvent(exception);
                entry.SetTag("akron.phase", phase.ToString());
                return client.CaptureEvent(entry);
            }
        }
        catch (Exception)
        {
            // No recursive reporting and no change to the caller's recovery path.
            return SentryId.Empty;
        }
    }

    internal void Stop(bool flush)
    {
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            if (!flush) revoked.Cancel();
        }
        // Revocation prevents queued or subsequent sends immediately. Already-sent requests
        // cannot be recalled. Do not wait for network delivery while the player changes consent.
        if (!flush)
        {
            shutdown = Task.Run(DisposeClient);
            return;
        }
        DisposeClient();
    }

    private void DisposeClient()
    {
        try { client.Dispose(); }
        catch (Exception) { }
        finally
        {
            revoked.Cancel();
            worker.Dispose();
        }
    }

    internal Task FlushAsync() => client.FlushAsync(ShutdownTimeout);

    internal async Task WaitForShutdownAsync()
    {
        await shutdown.ConfigureAwait(false);
        await worker.Completion.ConfigureAwait(false);
    }

    // A new event is an allowlist, not a blacklist: exception messages/data, user identity,
    // request data, breadcrumbs, host/device context, other mods and raw paths cannot survive.
    internal SentryEvent Sanitize(SentryEvent source)
    {
        if (revoked.IsCancellationRequested || !source.Tags.TryGetValue("akron.phase", out string phase) ||
            !Enum.TryParse(phase, out AkronFailurePhase parsed) || !Enum.IsDefined(parsed)) return null;
        // The public constructor generates a new ID. FromJson is the SDK's public API
        // for preserving identity; seed it with these two trusted scalar fields only.
        using JsonDocument identity = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            event_id = source.EventId.ToString(),
            timestamp = source.Timestamp.ToString("O", CultureInfo.InvariantCulture)
        }));
        SentryEvent clean = SentryEvent.FromJson(identity.RootElement);
        clean.Level = SentryLevel.Error;
        clean.Logger = "Akron";
        clean.Release = release;
        clean.Environment = "production";
        clean.Sdk.Name = source.Sdk.Name;
        clean.Sdk.Version = source.Sdk.Version;
        clean.SetTag("akron.phase", parsed.ToString());
        Dictionary<string, string> imageIndexes = new Dictionary<string, string>();
        if (source.DebugImages != null)
        {
            clean.DebugImages = new List<DebugImage>();
            for (int index = 0; index < source.DebugImages.Count; index++)
            {
                DebugImage image = source.DebugImages[index];
                if (!string.Equals(FileName(image.CodeFile), "Akron.dll", StringComparison.OrdinalIgnoreCase)) continue;
                imageIndexes["rel:" + index.ToString(CultureInfo.InvariantCulture)] = "rel:" + clean.DebugImages.Count.ToString(CultureInfo.InvariantCulture);
                clean.DebugImages.Add(new DebugImage
                {
                    Type = "pe_dotnet",
                    CodeFile = "Akron.dll",
                    DebugFile = "Akron.pdb",
                    DebugId = SafeHex(image.DebugId),
                    DebugChecksum = SafeHex(image.DebugChecksum),
                    CodeId = SafeHex(image.CodeId)
                });
            }
        }
        List<SentryException> exceptions = new List<SentryException>();
        foreach (SentryException error in source.SentryExceptions?.Take(8) ?? Enumerable.Empty<SentryException>())
        {
            SentryStackTrace stack = new SentryStackTrace();
            foreach (SentryStackFrame frame in error.Stacktrace?.Frames?.TakeLast(64) ?? Enumerable.Empty<SentryStackFrame>())
            {
                if (frame.Module?.StartsWith("Celeste.Mod.Akron.", StringComparison.Ordinal) != true) continue;
                string addressMode = null;
                if (frame.AddressMode != null) imageIndexes.TryGetValue(frame.AddressMode, out addressMode);
                stack.Frames.Add(new SentryStackFrame
                {
                    Module = SafeIdentifier(frame.Module),
                    Function = SafeIdentifier(frame.Function),
                    Package = "Akron",
                    FileName = FileName(frame.FileName),
                    LineNumber = frame.LineNumber,
                    ColumnNumber = frame.ColumnNumber,
                    InApp = true,
                    AddressMode = addressMode,
                    FunctionId = addressMode == null ? null : frame.FunctionId,
                    InstructionAddress = addressMode == null ? null : frame.InstructionAddress
                });
            }
            exceptions.Add(new SentryException
            {
                Type = SafeIdentifier(error.Type),
                Value = "Akron " + parsed + " failed.",
                Stacktrace = stack.Frames.Count == 0 ? null : stack,
                Mechanism = new Mechanism { Type = "akron", Handled = true }
            });
        }
        clean.SentryExceptions = exceptions;
        return clean;
    }

    internal static bool TryGetDestination(string dsn, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(dsn) || !Uri.TryCreate(dsn, UriKind.Absolute, out Uri uri) ||
            uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.IdnHost) ||
            !Regex.IsMatch(uri.UserInfo, @"^[A-Za-z0-9]+$", RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(uri.AbsolutePath, @"^/(?:[A-Za-z0-9_-]+/)*[0-9]+$", RegexOptions.CultureInvariant) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        host = uri.IdnHost;
        return true;
    }

    private static string SafeVersion(string value) =>
        !string.IsNullOrWhiteSpace(value) && Regex.IsMatch(value, @"^[A-Za-z0-9_.+-]{1,96}$", RegexOptions.CultureInvariant) ? value : "unknown";
    private static string SafeIdentifier(string value) => value != null && Identifier.IsMatch(value) ? value : "unknown";
    private static string SafeHex(string value) => value != null && HexIdentifier.IsMatch(value) ? value : null;
    private static string FileName(string value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        string name = value.Replace('\\', '/').Split('/').Last();
        return Regex.IsMatch(name, @"^[A-Za-z0-9_.-]{1,128}$", RegexOptions.CultureInvariant) ? name : null;
    }

    private sealed class ConsentHandler : DelegatingHandler
    {
        private readonly CancellationToken revoked;
        internal ConsentHandler(CancellationToken revoked, HttpMessageHandler inner) : base(inner) { this.revoked = revoked; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (revoked.IsCancellationRequested) return new HttpResponseMessage(HttpStatusCode.OK);
            using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(revoked, cancellationToken);
            return await base.SendAsync(request, linked.Token).ConfigureAwait(false);
        }
    }
}

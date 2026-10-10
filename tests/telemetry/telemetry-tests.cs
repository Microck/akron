using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sentry;
using Sentry.Protocol;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Celeste.Mod.Akron.Tests;

public sealed class TelemetryTests
{
    private const string Dsn = "https://publickey@example.invalid/1";

    [Theory]
    [InlineData(false, Dsn)]
    [InlineData(true, "")]
    [InlineData(true, "http://publickey@example.invalid/1")]
    [InlineData(true, "https://publickey:secret@example.invalid/1")]
    [InlineData(true, "https://publickey@example.invalid/1?secret=value")]
    public void MissingConsentOrValidDsnNeverStartsReporting(bool consent, string dsn)
    {
        RecordingHandler transport = new RecordingHandler();
        try
        {
            AkronTelemetry.Configure(consent, dsn, "1.2.3", transport);
            AkronTelemetry.Capture(BuildFailure(), AkronFailurePhase.Startup);
            Assert.False(AkronTelemetry.IsEnabled);
            Assert.Empty(transport.Bodies);
        }
        finally
        {
            AkronTelemetry.Stop(flush: false);
        }
    }

    [Fact]
    public void SanitizerPreservesIdentityAndOnlyAllowsAkronDiagnosticFields()
    {
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", new RecordingHandler());
        try
        {
            SentryEvent source = new SentryEvent(BuildFailure())
            {
                ServerName = "private-machine",
                Release = "private-version",
                Environment = "private-environment",
                Message = "private-message",
                User = new SentryUser { Username = "private-user", Email = "private@example.invalid" },
                Request = new SentryRequest { Url = "https://private.invalid/token", Data = "private-body" },
                SentryExceptions = new[] {
                    new SentryException {
                        Type = "IOException", Value = "private exception value",
                        Stacktrace = new SentryStackTrace { Frames = new List<SentryStackFrame> {
                            new SentryStackFrame { Module = "Other.Mod", Function = "private-method" },
                            new SentryStackFrame {
                                Module = "Celeste.Mod.Akron.AkronSaveLoadService", Function = "Restore",
                                Package = "private-assembly", FileName = "/home/private-user/AkronSaveLoad.cs",
                                AbsolutePath = "/home/private-user/AkronSaveLoad.cs", ContextLine = "private-source",
                                LineNumber = 123, AddressMode = "rel:1", FunctionId = 5, InstructionAddress = 7
                            }
                        } }
                    }
                },
                DebugImages = new List<DebugImage> {
                    new DebugImage { CodeFile = "/private/Other.dll", DebugId = "abcd" },
                    new DebugImage { CodeFile = @"C:\Users\private-user\Akron.dll", DebugFile = @"C:\private\Akron.pdb", DebugId = "abcd-1234", CodeId = "1234", DebugChecksum = "abcd" }
                }
            };
            source.SetTag("akron.phase", AkronFailurePhase.StartPosRestore.ToString());
            source.SetTag("private-tag", "private-value");
            source.SetExtra("private-extra", "private-data");
            source.AddBreadcrumb(new Breadcrumb("private-breadcrumb", "default"));
            source.Contexts["private-context"] = "private-value";
            SentryEvent clean = reporter.Sanitize(source);
            Assert.Equal(source.EventId, clean.EventId);
            Assert.Equal(source.Timestamp, clean.Timestamp);
            string json = Serialize(clean);
            Assert.DoesNotContain("private", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Other.Mod", json);
            Assert.DoesNotContain("Other.dll", json);
            Assert.Contains("AkronSaveLoad.cs", json);
            Assert.Contains("akron@1.2.3", json);
            SentryStackFrame frame = Assert.Single(Assert.Single(clean.SentryExceptions).Stacktrace.Frames);
            Assert.Equal("rel:0", frame.AddressMode);
            Assert.Equal(5, frame.FunctionId);
            Assert.Equal(7, frame.InstructionAddress);
            Assert.Equal("Akron.dll", Assert.Single(clean.DebugImages).CodeFile);
        }
        finally { reporter.Stop(flush: false); }
    }

    [Fact]
    public async Task ActualSdkEnvelopeIsScrubbedAndUsesTheReturnedEventId()
    {
        RecordingHandler transport = new RecordingHandler();
        bool globalWasEnabled = SentrySdk.IsEnabled;
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", transport);
        try
        {
            SentryId id = reporter.Capture(BuildFailure(), AkronFailurePhase.StartPosPersist);
            await reporter.FlushAsync();
            string body = Assert.Single(transport.Bodies);
            Assert.NotEqual(SentryId.Empty, id);
            Assert.Contains(id.ToString(), body);
            Assert.Contains("StartPosPersist", body);
            Assert.DoesNotContain("private", body, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(globalWasEnabled, SentrySdk.IsEnabled);
        }
        finally { reporter.Stop(flush: false); }
    }

    [Fact]
    public async Task RepeatedRendererFailuresAndCancellationDoNotFloodReports()
    {
        RecordingHandler transport = new RecordingHandler();
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", transport);
        try
        {
            Assert.NotEqual(SentryId.Empty, reporter.Capture(BuildFailure(), AkronFailurePhase.Overlay));
            for (int index = 0; index < 100; index++)
            {
                Assert.Equal(SentryId.Empty, reporter.Capture(BuildFailure(), AkronFailurePhase.Overlay));
            }
            Assert.Equal(SentryId.Empty, reporter.Capture(new OperationCanceledException(), AkronFailurePhase.StartPosPersist));
            Assert.NotEqual(SentryId.Empty, reporter.Capture(BuildFailure(), AkronFailurePhase.StartPosRestore));
            await reporter.FlushAsync();
            Assert.Equal(2, transport.Bodies.Count);
        }
        finally { reporter.Stop(flush: false); }
    }

    [Fact]
    public async Task IndependentFailuresHaveASessionLimit()
    {
        RecordingHandler transport = new RecordingHandler();
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", transport);
        try
        {
            foreach (AkronFailurePhase phase in Enum.GetValues<AkronFailurePhase>())
            {
                foreach (Exception exception in new Exception[] { new IOException(), new ArgumentException(), new InvalidOperationException(), new NotSupportedException() })
                {
                    reporter.Capture(exception, phase);
                }
            }
            await reporter.FlushAsync();
            Assert.Equal(AkronErrorReporter.MaxReportsPerSession, transport.Bodies.Count);
        }
        finally { reporter.Stop(flush: false); }
    }

    [Fact]
    public async Task RevokingConsentCancelsInFlightAndPreventsQueuedSends()
    {
        BlockingHandler transport = new BlockingHandler();
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", transport);
        reporter.Capture(BuildFailure(), AkronFailurePhase.Overlay);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        reporter.Capture(BuildFailure(), AkronFailurePhase.StartPosRestore);
        reporter.Stop(flush: false);
        await transport.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await reporter.FlushAsync();
        Assert.Equal(SentryId.Empty, reporter.Capture(BuildFailure(), AkronFailurePhase.Content));
        Assert.Equal(1, transport.StartCount);
    }

    [Fact]
    public async Task ShutdownIsBoundedAndIdempotent()
    {
        BlockingHandler transport = new BlockingHandler();
        AkronErrorReporter reporter = new AkronErrorReporter(Dsn, "1.2.3", transport);
        reporter.Capture(BuildFailure(), AkronFailurePhase.Overlay);
        await transport.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Stopwatch timer = Stopwatch.StartNew();
        reporter.Stop(flush: true);
        reporter.Stop(flush: true);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
        await transport.Canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static Exception BuildFailure()
    {
        try { throw new IOException("private-user /home/private-user/file secret=private-token"); }
        catch (Exception exception)
        {
            exception.Data["private-key"] = "private-value";
            return exception;
        }
    }

    private static string Serialize(SentryEvent entry)
    {
        using MemoryStream stream = new MemoryStream();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(stream)) entry.WriteTo(writer, null);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        internal ConcurrentQueue<string> Bodies { get; } = new ConcurrentQueue<string>();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using Stream content = await request.Content.ReadAsStreamAsync(token);
            using Stream decoded = request.Content.Headers.ContentEncoding.Contains("gzip") ? new GZipStream(content, CompressionMode.Decompress) : content;
            using StreamReader reader = new StreamReader(decoded);
            Bodies.Enqueue(await reader.ReadToEndAsync(token));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        internal TaskCompletionSource<bool> Started { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Canceled { get; } = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int StartCount;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref StartCount);
            Started.TrySetResult(true);
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { Canceled.TrySetResult(true); throw; }
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}

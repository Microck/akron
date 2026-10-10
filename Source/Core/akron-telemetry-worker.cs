using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Sentry;
using Sentry.Extensibility;
using Sentry.Http;
using Sentry.Protocol.Envelopes;

namespace Celeste.Mod.Akron;

// SentryClient.Dispose only flushes its default worker. Own the public worker and
// transport extension points so an unloaded mod cannot leave an idle SDK task behind.
internal sealed class AkronTelemetryWorker : HttpTransportBase, IBackgroundWorker, IDisposable
{
    private readonly object sync = new object();
    private readonly HttpClient httpClient;
    private readonly CancellationTokenSource shutdown = new CancellationTokenSource();
    private readonly int limit;
    private Task tail = Task.CompletedTask;
    private Task completion = Task.CompletedTask;
    private int queued;
    private bool stopped;

    internal AkronTelemetryWorker(SentryOptions options, HttpMessageHandler handler) : base(options)
    {
        limit = options.MaxQueueItems;
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public int QueuedItems => Volatile.Read(ref queued);
    internal Task Completion { get { lock (sync) return completion; } }

    public bool EnqueueEnvelope(Envelope envelope)
    {
        lock (sync)
        {
            if (stopped || queued >= limit) return false;
            Interlocked.Increment(ref queued);
            Task previous = tail;
            // Only accepted events schedule work. There is no task waiting on an empty queue.
            tail = Task.Run(() => SendAfterAsync(previous, envelope));
            return true;
        }
    }

    private async Task SendAfterAsync(Task previous, Envelope envelope)
    {
        try
        {
            await previous.ConfigureAwait(false);
            if (shutdown.IsCancellationRequested) return;
            Envelope processed = ProcessEnvelope(envelope);
            if (processed.Items.Count == 0) return;
            using HttpRequestMessage request = CreateRequest(processed);
            using HttpResponseMessage response = await httpClient.SendAsync(request, shutdown.Token).ConfigureAwait(false);
            // The SDK's default HTTP handler handles plain 429s. Normalize them for
            // the public transport base, which already applies Sentry rate-limit headers.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && !response.Headers.Contains("X-Sentry-Rate-Limits"))
            {
                double seconds = response.Headers.RetryAfter?.Delta?.TotalSeconds ??
                    (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? 60;
                string delay = Math.Ceiling(Math.Clamp(seconds, 0, int.MaxValue)).ToString(CultureInfo.InvariantCulture);
                response.Headers.TryAddWithoutValidation("X-Sentry-Rate-Limits", delay + "::organization");
            }
            await HandleResponseAsync(response, processed, shutdown.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Reporting is best effort. Cancellation and failed requests never escape to gameplay.
        }
        finally
        {
            envelope.Dispose();
            Interlocked.Decrement(ref queued);
        }
    }

    public async Task FlushAsync(TimeSpan timeout)
    {
        Task pending;
        lock (sync) pending = tail;
        try { await pending.WaitAsync(timeout).ConfigureAwait(false); }
        catch (TimeoutException) { }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (stopped) return;
            stopped = true;
            shutdown.Cancel();
            httpClient.Dispose();
            completion = FinishDisposalAsync(tail);
        }
    }

    private async Task FinishDisposalAsync(Task pending)
    {
        try { await pending.ConfigureAwait(false); }
        finally { shutdown.Dispose(); }
    }
}

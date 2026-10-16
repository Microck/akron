using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Celeste.Mod.Akron;

// Per-process diagnostics never write global recorder state from a late callback.
internal sealed class AkronEncoderDiagnostics {
    private readonly object sync = new object();
    private readonly Queue<string> lines = new Queue<string>();
    private readonly TaskCompletionSource<bool> ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Receive(string line) {
        if (line == null) {
            ended.TrySetResult(true);
            return;
        }
        if (string.IsNullOrWhiteSpace(line)) return;
        lock (sync) {
            lines.Enqueue(line.Length > 512 ? line.Substring(0, 512) : line);
            while (lines.Count > 8) lines.Dequeue();
        }
    }

    internal string ReadAfterExit() {
        // Process.WaitForExit(timeout) does not guarantee asynchronous stderr has drained.
        // Bound this diagnostic-only wait; never turn a warning into a failed encode.
        ended.Task.Wait(250);
        lock (sync) return string.Join(" | ", lines);
    }
}

internal static class AkronEncoderCompletion {
    internal static string FailureToast(string error) {
        string detail = (error ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (detail.Length > 64) detail = detail.Substring(0, 61) + "...";
        return string.IsNullOrEmpty(detail) ? "Recording failed." : "Recording failed: " + detail;
    }

    // Reserve a fresh destination atomically, protecting existing video and sidecar paths.
    // FFmpeg may overwrite only the empty file owned by this recording attempt.
    internal static string ReserveOutputPath(string requestedPath) {
        string directory = Path.GetDirectoryName(requestedPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(requestedPath);
        string extension = Path.GetExtension(requestedPath);
        for (int suffix = 0; ; suffix++) {
            string candidate = suffix == 0 ? requestedPath : Path.Combine(directory, stem + "-" + suffix + extension);
            if (File.Exists(candidate + ".akrclip")) continue;
            try {
                using FileStream reservation = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                return candidate;
            } catch (IOException) when (File.Exists(candidate)) {
                // An earlier recording or concurrent reservation owns this name.
            }
        }
    }

    internal static bool HasNonemptyOutput(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    // Separate cleanup operations so one broken pipe cannot skip process termination.
    // Delegates isolate process lifecycle tests from Celeste's runtime state.
    internal static string Finish(Stream input, Func<int, bool> waitForExit, Func<int> exitCode,
        Action kill, Action dispose, Func<string> diagnostics, Func<bool> hasOutput, string captureError = null) {
        List<string> errors = new List<string>();
        if (!string.IsNullOrWhiteSpace(captureError)) errors.Add(captureError);
        void Attempt(Action action) {
            try { action(); } catch (Exception ex) { errors.Add(ex.Message); }
        }
        Attempt(() => input?.Flush());
        Attempt(() => input?.Dispose());
        bool exited = false;
        Attempt(() => exited = waitForExit(3000));
        if (!exited) {
            errors.Add("FFmpeg did not exit normally within 3 seconds.");
            Attempt(kill);
            Attempt(() => waitForExit(1000));
        } else {
            Attempt(() => {
                int code = exitCode();
                if (code != 0) errors.Add("FFmpeg exited with code " + code + ".");
            });
        }
        string stderr = string.Empty;
        Attempt(() => stderr = diagnostics());
        Attempt(dispose);
        Attempt(() => {
            if (!hasOutput()) errors.Add("FFmpeg produced no nonempty video file.");
        });
        if (errors.Count == 0) return string.Empty;
        if (!string.IsNullOrWhiteSpace(stderr)) errors.Insert(0, stderr);
        return string.Join(" | ", errors);
    }
}

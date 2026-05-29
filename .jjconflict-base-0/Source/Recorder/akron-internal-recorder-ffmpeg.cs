using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronInternalRecorder {
    public static string BuildFfmpegArgumentsForTesting(AkronModuleSettings settings, int width, int height, int fps, string outputPath) {
        return BuildFfmpegArguments(settings, new RecorderSpec(width, height, fps, settings.RecordingCodec), outputPath);
    }

    public static string BuildAudioMuxArgumentsForTesting(string videoPath, AkronRecordedAudio audio, string outputPath) {
        return BuildAudioMuxArguments(videoPath, audio.Tracks.Where(track => track.HasSamples).ToList(), outputPath);
    }

    public static AkronRecordedAudio SliceRecordedAudioForTesting(AkronRecordedAudio audio, DateTime startUtc, DateTime endUtc) {
        return SliceRecordedAudio(audio, startUtc, endUtc);
    }

    public static string BuildCompletionConcatArgumentsForTesting(string concatList, string outputPath) {
        return BuildCompletionConcatArguments(concatList, outputPath);
    }

    private static string BuildFfmpegArguments(AkronModuleSettings settings, RecorderSpec spec, string outputPath) {
        List<string> args = BuildRawVideoInputArguments(spec);
        args.Add("-an");
        args.Add("-c:v");
        args.Add(AkronModuleSettings.GetRecordingCodecArgument(spec.Codec));
        AddEncoderPerformanceArguments(args, settings, spec);
        AddRateControlArguments(args, settings);
        AddKeyframeAndFilterArguments(args, settings, spec);
        args.Add("-y");
        args.Add(outputPath);
        return JoinArguments(args);
    }

    private static string BuildReplaySegmentArguments(AkronModuleSettings settings, RecorderSpec spec, string segmentPattern) {
        List<string> args = BuildRawVideoInputArguments(spec);
        args.Add("-an");
        args.Add("-c:v");
        args.Add(AkronModuleSettings.GetRecordingCodecArgument(spec.Codec));
        AddEncoderPerformanceArguments(args, settings, spec);
        AddRateControlArguments(args, settings);
        AddKeyframeAndFilterArguments(args, settings, spec);
        args.Add("-f");
        args.Add("segment");
        args.Add("-segment_time");
        args.Add(ReplaySegmentSeconds.ToString(CultureInfo.InvariantCulture));
        args.Add("-reset_timestamps");
        args.Add("1");
        args.Add("-segment_format");
        args.Add("matroska");
        args.Add("-y");
        args.Add(segmentPattern);
        return JoinArguments(args);
    }

    private static List<string> BuildRawVideoInputArguments(RecorderSpec spec) {
        return new List<string> {
            "-hide_banner",
            "-loglevel", "warning",
            "-f", "rawvideo",
            "-pix_fmt", "rgba",
            "-s", spec.Width.ToString(CultureInfo.InvariantCulture) + "x" + spec.Height.ToString(CultureInfo.InvariantCulture),
            "-r", spec.Framerate.ToString(CultureInfo.InvariantCulture),
            "-i", "-"
        };
    }

    private static void AddKeyframeAndFilterArguments(List<string> args, AkronModuleSettings settings, RecorderSpec spec) {
        int keyframe = AkronModuleSettings.ClampRecordingKeyframeIntervalSeconds(settings.RecordingKeyframeIntervalSeconds);
        if (keyframe > 0) {
            args.Add("-g");
            args.Add((keyframe * spec.Framerate).ToString(CultureInfo.InvariantCulture));
        }

        if (!string.IsNullOrWhiteSpace(settings.RecordingColorspaceArgs)) {
            args.Add("-vf");
            args.Add(settings.RecordingColorspaceArgs.Trim());
        }
    }

    private static void AddRateControlArguments(List<string> args, AkronModuleSettings settings) {
        int bitrate = AkronModuleSettings.ClampRecordingBitrateMbps(settings.RecordingBitrateMbps);
        switch (settings.RecordingRateControl) {
            case AkronRecordingRateControl.Cbr:
                args.Add("-b:v");
                args.Add(bitrate.ToString(CultureInfo.InvariantCulture) + "M");
                args.Add("-maxrate");
                args.Add(bitrate.ToString(CultureInfo.InvariantCulture) + "M");
                args.Add("-bufsize");
                args.Add((bitrate * 2).ToString(CultureInfo.InvariantCulture) + "M");
                break;
            case AkronRecordingRateControl.Vbr:
                args.Add("-b:v");
                args.Add(bitrate.ToString(CultureInfo.InvariantCulture) + "M");
                break;
            case AkronRecordingRateControl.Cqp:
                args.Add("-qp");
                args.Add(GetQuantizer(settings));
                break;
            case AkronRecordingRateControl.Lossless:
                args.Add("-qp");
                args.Add("0");
                break;
            default:
                args.Add("-crf");
                args.Add(GetQuantizer(settings));
                break;
        }
    }

    private static void AddEncoderPerformanceArguments(List<string> args, AkronModuleSettings settings, RecorderSpec spec) {
        if (spec.Codec != AkronRecordingCodec.Libx264) {
            return;
        }

        args.Add("-preset");
        args.Add(settings.RecordingQualityPreset switch {
            AkronRecordingQualityPreset.HighQuality => "fast",
            AkronRecordingQualityPreset.LowImpact => "ultrafast",
            AkronRecordingQualityPreset.Lossless => "ultrafast",
            _ => "veryfast"
        });
        args.Add("-tune");
        args.Add("zerolatency");
    }

    private static void WarnAboutUnsupportedCaptureSettings(AkronModuleSettings settings) {
        if (AkronInternalAudioRecorder.HasUnsupportedTrackRequest(settings)) {
            Engine.Scene?.Add(new AkronToast("Unsupported audio options: " + AkronInternalAudioRecorder.DescribeUnsupportedTrackRequest(settings)));
        }

        // Akron does not draw a live recorder preview surface in this backend, so
        // RecordingHidePreview is satisfied by leaving that preview surface absent.
    }

    private static string GetQuantizer(AkronModuleSettings settings) {
        return settings.RecordingQualityPreset switch {
            AkronRecordingQualityPreset.LowImpact => "28",
            AkronRecordingQualityPreset.HighQuality => "16",
            AkronRecordingQualityPreset.Lossless => "0",
            _ => "21"
        };
    }

    private static bool TryRemuxToMp4(string inputPath, out string outputPath, out string error) {
        outputPath = Path.ChangeExtension(inputPath, ".mp4");
        error = string.Empty;

        try {
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = JoinArguments(new[] {
                    "-hide_banner",
                    "-loglevel", "warning",
                    "-i", inputPath,
                    "-c", "copy",
                    "-y", outputPath
                }),
                WorkingDirectory = ResolveFfmpegWorkingDirectory(Path.GetDirectoryName(inputPath)),
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            if (process == null) {
                error = "FFmpeg did not start.";
                return false;
            }

            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(10000) || process.ExitCode != 0) {
                error = string.IsNullOrWhiteSpace(stderr) ? "FFmpeg remux failed." : stderr.Trim();
                return false;
            }

            return File.Exists(outputPath);
        } catch (Exception ex) {
            error = ex.Message;
            return false;
        }
    }

    private static AkronRecordedAudio SliceRecordedAudio(AkronRecordedAudio audio, DateTime startUtc, DateTime endUtc) {
        if (!audio.HasSamples || endUtc <= startUtc) {
            return new AkronRecordedAudio(new List<AkronRecordedAudioTrack>());
        }

        List<AkronRecordedAudioTrack> slicedTracks = new List<AkronRecordedAudioTrack>();
        foreach (AkronRecordedAudioTrack track in audio.Tracks.Where(track => track.HasSamples)) {
            if (track.StartedUtc == DateTime.MinValue || !File.Exists(track.Path)) {
                continue;
            }

            int frameBytes = Math.Max(1, track.Channels) * 2;
            long bytesPerSecond = Math.Max(1, track.SampleRate) * frameBytes;
            FileInfo sourceInfo = new FileInfo(track.Path);
            long sourceLength = sourceInfo.Length;
            if (sourceLength <= 0) {
                continue;
            }

            double startSeconds = Math.Max(0d, (startUtc - track.StartedUtc).TotalSeconds);
            double requestedSeconds = Math.Max(0d, (endUtc - startUtc).TotalSeconds);
            long startByte = AlignToFrame((long) Math.Floor(startSeconds * bytesPerSecond), frameBytes);
            long requestedBytes = AlignToFrame((long) Math.Ceiling(requestedSeconds * bytesPerSecond), frameBytes);
            if (startByte >= sourceLength || requestedBytes <= 0) {
                continue;
            }

            long bytesToCopy = Math.Min(requestedBytes, sourceLength - startByte);
            bytesToCopy = AlignToFrame(bytesToCopy, frameBytes);
            if (bytesToCopy <= 0) {
                continue;
            }

            string slicedPath = Path.Combine(Path.GetTempPath(), "akron-audio-slice-" + Guid.NewGuid().ToString("N") + ".s16le");
            try {
                CopyFileRange(track.Path, slicedPath, startByte, bytesToCopy);
                slicedTracks.Add(new AkronRecordedAudioTrack(slicedPath, track.DisplayName, track.SampleRate, track.Channels, bytesToCopy, startUtc, endUtc));
            } catch (Exception ex) {
                lastError = ex.Message;
                TryDeleteFile(slicedPath);
            }
        }

        return new AkronRecordedAudio(slicedTracks);
    }

    private static long AlignToFrame(long value, int frameBytes) {
        return frameBytes <= 1 ? value : value - value % frameBytes;
    }

    private static void CopyFileRange(string sourcePath, string destinationPath, long startByte, long bytesToCopy) {
        byte[] buffer = new byte[81920];
        using FileStream source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using FileStream destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        source.Seek(startByte, SeekOrigin.Begin);
        long remaining = bytesToCopy;
        while (remaining > 0) {
            int read = source.Read(buffer, 0, (int) Math.Min(buffer.Length, remaining));
            if (read <= 0) {
                break;
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static bool TryMuxAudioIntoVideo(string videoPath, AkronRecordedAudio audio, out string outputPath, out string error) {
        string extension = Path.GetExtension(videoPath);
        outputPath = Path.Combine(
            Path.GetDirectoryName(videoPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(videoPath) + "-audio" + extension);
        error = string.Empty;
        List<AkronRecordedAudioTrack> tracks = audio.Tracks.Where(track => track.HasSamples).ToList();
        if (tracks.Count == 0) {
            return false;
        }

        try {
            ProcessStartInfo startInfo = new ProcessStartInfo {
                FileName = ResolveFfmpegPath(),
                Arguments = BuildAudioMuxArguments(videoPath, tracks, outputPath),
                WorkingDirectory = ResolveFfmpegWorkingDirectory(Path.GetDirectoryName(videoPath)),
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            if (process == null) {
                error = "FFmpeg did not start.";
                return false;
            }

            string stderr = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000) || process.ExitCode != 0) {
                error = string.IsNullOrWhiteSpace(stderr) ? "FFmpeg audio mux failed." : stderr.Trim();
                return false;
            }

            return File.Exists(outputPath);
        } catch (Exception ex) {
            error = ex.Message;
            return false;
        } finally {
            foreach (AkronRecordedAudioTrack track in tracks) {
                TryDeleteFile(track.Path);
            }
        }
    }

    private static string BuildCompletionConcatArguments(string concatList, string outputPath) {
        return JoinArguments(new[] {
            "-hide_banner",
            "-loglevel", "warning",
            "-f", "concat",
            "-safe", "0",
            "-i", concatList,
            "-c", "copy",
            "-y", outputPath
        });
    }

    private static void DeleteRecordedAudioFiles(AkronRecordedAudio audio) {
        foreach (AkronRecordedAudioTrack track in audio.Tracks) {
            TryDeleteFile(track.Path);
        }
    }

    private static string BuildAudioMuxArguments(string videoPath, List<AkronRecordedAudioTrack> tracks, string outputPath) {
        string extension = Path.GetExtension(outputPath);
        string audioCodec = string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase) ? "libopus" : "aac";
        List<string> args = new List<string> {
            "-hide_banner",
            "-loglevel", "warning",
            "-i", videoPath
        };

        foreach (AkronRecordedAudioTrack track in tracks) {
            args.Add("-f");
            args.Add("s16le");
            args.Add("-ar");
            args.Add(track.SampleRate.ToString(CultureInfo.InvariantCulture));
            args.Add("-ac");
            args.Add(track.Channels.ToString(CultureInfo.InvariantCulture));
            args.Add("-i");
            args.Add(track.Path);
        }

        args.Add("-map");
        args.Add("0:v:0");
        for (int i = 0; i < tracks.Count; i++) {
            args.Add("-map");
            args.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ":a:0");
        }

        args.Add("-c:v");
        args.Add("copy");
        args.Add("-c:a");
        args.Add(audioCodec);

        for (int i = 0; i < tracks.Count; i++) {
            args.Add("-metadata:s:a:" + i.ToString(CultureInfo.InvariantCulture));
            args.Add("title=" + tracks[i].DisplayName);
        }

        if (!string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase)) {
            args.Add("-b:a");
            args.Add("192k");
        }

        args.Add("-shortest");
        args.Add("-y");
        args.Add(outputPath);
        return JoinArguments(args);
    }
}

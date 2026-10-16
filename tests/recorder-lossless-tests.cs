using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Reflection;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RecorderLosslessTests {
    [Theory]
    [InlineData(AkronRecordingCodec.LibVpxVp9, "-lossless", "1")]
    [InlineData(AkronRecordingCodec.Libx264, "-qp", "0")]
    public void LosslessUsesCapturedCodecRatherThanDefaultExportSettings(AkronRecordingCodec codec, string flag, string value) {
        var settings = new AkronModuleSettings { RecordingCodec = codec == AkronRecordingCodec.LibVpxVp9 ? AkronRecordingCodec.Libx264 : AkronRecordingCodec.LibVpxVp9,
            RecordingRateControl = AkronRecordingRateControl.Lossless };
        Type recorder = typeof(AkronInternalRecorder);
        Type specType = recorder.GetNestedType("RecorderSpec", BindingFlags.NonPublic)!;
        object spec = Activator.CreateInstance(specType, 32, 32, 30, codec)!;
        var args = new List<string>();
        recorder.GetMethod("AddRateControlArguments", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { args, settings, spec });
        Assert.Equal(new[] { flag, value }, args);
        foreach (string method in new[] { "BuildFfmpegArguments", "BuildReplaySegmentArguments" }) {
            string output = Path.Combine(Path.GetTempPath(), "akron-lossless.mkv");
            string generated = (string)recorder.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { settings, spec, output })!;
            Assert.Contains($"\"{flag}\" \"{value}\"", generated);
            if (codec == AkronRecordingCodec.LibVpxVp9) Assert.DoesNotContain("\"-qp\"", generated);
        }
    }

    [Theory]
    [InlineData(AkronRecordingRateControl.Crf, "-crf")]
    [InlineData(AkronRecordingRateControl.Cqp, "-qp")]
    public void SeparateQualityPresetDoesNotChangeRateControlSemantics(AkronRecordingRateControl rate, string flag) {
        var settings = new AkronModuleSettings { RecordingCodec = AkronRecordingCodec.LibVpxVp9,
            RecordingRateControl = rate, RecordingQualityPreset = AkronRecordingQualityPreset.Lossless };
        string args = AkronInternalRecorder.BuildFfmpegArgumentsForTesting(settings, 32, 32, 30, "/tmp/test.mkv");
        Assert.Contains($"\"{flag}\" \"0\"", args);
        Assert.DoesNotContain("\"-lossless\"", args);
    }

    [Fact]
    public void Vp9LosslessIgnoresQualityPresetQuantizer() {
        foreach (AkronRecordingQualityPreset quality in Enum.GetValues<AkronRecordingQualityPreset>()) {
            var settings = new AkronModuleSettings { RecordingCodec = AkronRecordingCodec.LibVpxVp9,
                RecordingRateControl = AkronRecordingRateControl.Lossless, RecordingQualityPreset = quality };
            string args = AkronInternalRecorder.BuildFfmpegArgumentsForTesting(settings, 32, 32, 30, "/tmp/test.mkv");
            Assert.Contains("\"-lossless\" \"1\"", args);
            Assert.DoesNotContain("\"-qp\"", args);
        }
    }

    [FfmpegLosslessFact]
    public void CpuLosslessEncodersRoundTripAtTheSamePixelFormat() {
        string folder = Path.Combine(Path.GetTempPath(), "akron-lossless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try {
            byte[] rgba = new byte[32 * 32 * 4 * 6];
            new Random(204).NextBytes(rgba);
            string reference = Path.Combine(folder, "reference.yuv");
            RunFfmpeg($"-hide_banner -loglevel warning -f rawvideo -pix_fmt rgba -s 32x32 -r 10 -i - -vf format=yuv420p -f rawvideo -y \"{reference}\"", rgba);
            foreach (AkronRecordingCodec codec in new[] { AkronRecordingCodec.LibVpxVp9, AkronRecordingCodec.Libx264 }) {
                string clip = Path.Combine(folder, codec + ".mkv");
                string decoded = Path.Combine(folder, codec + ".yuv");
                var settings = new AkronModuleSettings { RecordingCodec = codec,
                    RecordingRateControl = AkronRecordingRateControl.Lossless, RecordingColorspaceArgs = "format=yuv420p" };
                string diagnostic = RunFfmpeg(AkronInternalRecorder.BuildFfmpegArgumentsForTesting(settings, 32, 32, 10, clip), rgba);
                Assert.DoesNotContain("has not been used", diagnostic);
                Assert.DoesNotContain("Neither bitrate nor constrained quality", diagnostic);
                RunFfmpeg($"-hide_banner -loglevel warning -i \"{clip}\" -pix_fmt yuv420p -f rawvideo -y \"{decoded}\"", null);
                Assert.Equal(32 * 32 * 3 / 2 * 6, new FileInfo(decoded).Length);
                Assert.True(File.ReadAllBytes(reference).SequenceEqual(File.ReadAllBytes(decoded)));
            }
        } finally { Directory.Delete(folder, true); }
    }

    private static string RunFfmpeg(string arguments, byte[] input) {
        using var process = new Process { StartInfo = new ProcessStartInfo("ffmpeg", arguments) {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true } };
        Assert.True(process.Start());
        var errors = process.StandardError.ReadToEndAsync();
        var write = System.Threading.Tasks.Task.Run(() => {
            if (input != null) process.StandardInput.BaseStream.Write(input);
            process.StandardInput.Close();
        });
        try {
            if (!process.WaitForExit(10000)) { process.Kill(true); process.WaitForExit(1000); throw new TimeoutException("Bounded FFmpeg lossless regression timed out."); }
            Assert.True(write.Wait(1000));
            Assert.True(errors.Wait(1000));
            Assert.True(process.ExitCode == 0, errors.Result);
            return errors.Result;
        } finally { if (!process.HasExited) { process.Kill(true); process.WaitForExit(1000); } }
    }

    [Fact]
    public void ReplayExportPassesOwnedCapturedSpecToRateControl() {
        string file = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder/akron-internal-recorder-replay-export.cs");
        Assert.Contains("AddRateControlArguments(args, snapshot.Settings, snapshot.Spec);", File.ReadAllText(file));
    }
}

public sealed class FfmpegLosslessFactAttribute : FactAttribute {
    public FfmpegLosslessFactAttribute() {
        if (Environment.GetEnvironmentVariable("AKRON_FFMPEG_TESTS") != "1")
            Skip = "Requires local FFmpeg with libx264/libvpx-vp9; set AKRON_FFMPEG_TESTS=1.";
    }
}

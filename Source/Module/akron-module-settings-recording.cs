namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    public const string DefaultRecordingFilenameTemplate = "{chapter}-{room}-{timestamp}-d{death}-a{attempt}";
    public const int DefaultRecordingReplayBufferSeconds = 30;
    public const int CompletionRecordingReplayBufferSeconds = 300;

    public static string NormalizeRecordingFilenameTemplate(string template) {
        string normalized = string.IsNullOrWhiteSpace(template) ? DefaultRecordingFilenameTemplate : template.Trim();
        return normalized.Length > 128 ? normalized.Substring(0, 128) : normalized;
    }

    public static int ClampRecordingReplayBufferSeconds(int seconds) {
        return seconds <= 0 ? 0 : ClampValue(seconds, 5, 600);
    }

    public static int ClampRecordingClipSeconds(int seconds) {
        return ClampValue(seconds < 0 ? 0 : seconds, 0, 120);
    }

    public static int ClampRecordingAudioLevel(int level) {
        return ClampValue(level, 0, 200);
    }

    public static int ClampRecordingKeyframeIntervalSeconds(int seconds) {
        return ClampValue(seconds < 0 ? 0 : seconds, 0, 20);
    }

    public static int ClampRecordingFramerate(int framerate) {
        return ClampValue(framerate <= 0 ? 60 : framerate, 1, 360);
    }

    public static float ClampRecordingEndscreenDurationSeconds(float seconds) {
        return seconds < 0f ? 0f : ClampValue(seconds, 0f, 30f);
    }

    public static int ClampRecordingBitrateMbps(int bitrateMbps) {
        return ClampValue(bitrateMbps <= 0 ? 30 : bitrateMbps, 1, 1000);
    }

    public static int ClampRecordingResolutionX(int width) {
        return ClampValue(width <= 0 ? 1920 : width, 1, 15360);
    }

    public static int ClampRecordingResolutionY(int height) {
        return ClampValue(height <= 0 ? 1080 : height, 1, 8640);
    }

    public static string FormatRecordingContainer(AkronRecordingContainerFormat format) {
        return format switch {
            AkronRecordingContainerFormat.Mp4 => "MP4",
            AkronRecordingContainerFormat.Mov => "MOV",
            AkronRecordingContainerFormat.WebM => "WebM",
            _ => "MKV"
        };
    }

    public static string GetRecordingContainerExtension(AkronRecordingContainerFormat format) {
        return format switch {
            AkronRecordingContainerFormat.Mp4 => ".mp4",
            AkronRecordingContainerFormat.Mov => ".mov",
            AkronRecordingContainerFormat.WebM => ".webm",
            _ => ".mkv"
        };
    }

    public static string FormatRecordingCodec(AkronRecordingCodec codec) {
        return codec switch {
            AkronRecordingCodec.H264Nvenc => "H.264 NVENC",
            AkronRecordingCodec.H264Amf => "H.264 AMF",
            AkronRecordingCodec.HevcNvenc => "HEVC NVENC",
            AkronRecordingCodec.LibVpxVp9 => "VP9",
            _ => "x264"
        };
    }

    public static string GetRecordingCodecArgument(AkronRecordingCodec codec) {
        return codec switch {
            AkronRecordingCodec.H264Nvenc => "h264_nvenc",
            AkronRecordingCodec.H264Amf => "h264_amf",
            AkronRecordingCodec.HevcNvenc => "hevc_nvenc",
            AkronRecordingCodec.LibVpxVp9 => "libvpx-vp9",
            _ => "libx264"
        };
    }

    public static string FormatRecordingPreset(AkronRecordingPreset preset) {
        return preset switch {
            AkronRecordingPreset.Nvidia => "NVIDIA",
            AkronRecordingPreset.Amd => "AMD",
            _ => "CPU"
        };
    }

    public static string FormatRecordingReplayAutoStart(AkronRecordingReplayAutoStart mode) {
        return mode switch {
            AkronRecordingReplayAutoStart.InLevels => "In Levels",
            AkronRecordingReplayAutoStart.Always => "Always",
            _ => "Off"
        };
    }

    public static string FormatRecordingQualityPreset(AkronRecordingQualityPreset preset) {
        return preset switch {
            AkronRecordingQualityPreset.LowImpact => "Low Impact",
            AkronRecordingQualityPreset.HighQuality => "High Quality",
            AkronRecordingQualityPreset.Lossless => "Lossless",
            _ => "Balanced"
        };
    }
}

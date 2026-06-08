using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using FMOD;
using System.Runtime.InteropServices;

namespace Celeste.Mod.Akron;

public static class AkronInternalAudioRecorder {
    private static readonly object Sync = new object();

    private static readonly List<CapturedTrack> ActiveTracks = new List<CapturedTrack>();
    private static string lastError = string.Empty;

    public static string LastError => lastError;

    public static bool ShouldCaptureFullMix(AkronModuleSettings settings) {
        return settings.RecordingAudioFullMixTrack;
    }

    public static bool HasUnsupportedTrackRequest(AkronModuleSettings settings) {
        return settings.RecordingRecordMutedAudio && !HasAnyIsolatedGameTrack(settings);
    }

    public static string DescribeUnsupportedTrackRequest(AkronModuleSettings settings) {
        List<string> warnings = new List<string>();
        if (settings.RecordingRecordMutedAudio && !HasAnyIsolatedGameTrack(settings)) {
            warnings.Add("record-muted audio needs a music, SFX, or ambience bus track");
        }

        return string.Join("; ", warnings);
    }

    public static IReadOnlyList<string> GetPlannedTrackNamesForTesting(AkronModuleSettings settings) {
        return BuildTrackRequests(settings).Select(track => track.DisplayName).ToList();
    }

    public static bool TryStart(AkronModuleSettings settings, out string warning) {
        return TryStart(settings, AudioCapturePurpose.Manual, out warning);
    }

    public static bool TryStartReplayBuffer(AkronModuleSettings settings, out string warning) {
        return TryStart(settings, AudioCapturePurpose.ReplayBuffer, out warning);
    }

    public static bool IsReplayBufferCapturing {
        get {
            lock (Sync) {
                return ActiveTracks.Any(track => track.Purpose == AudioCapturePurpose.ReplayBuffer);
            }
        }
    }

    private static bool TryStart(AkronModuleSettings settings, AudioCapturePurpose purpose, out string warning) {
        warning = string.Empty;
        List<TrackRequest> trackRequests = BuildTrackRequests(settings);
        if (trackRequests.Count == 0) {
            return false;
        }

        lock (Sync) {
            if (ActiveTracks.Any(track => track.Purpose == purpose)) {
                return true;
            }
        }

        if (!Audio.AudioInitialized || !Audio.System.isValid()) {
            warning = "FMOD audio is not initialized.";
            lastError = warning;
            return false;
        }

        try {
            FMOD.System lowLevel;
            RESULT result = Audio.System.getLowLevelSystem(out lowLevel);
            if (result != RESULT.OK) {
                warning = "FMOD low-level system unavailable: " + result;
                lastError = warning;
                return false;
            }

            int detectedSampleRate;
            SPEAKERMODE speakerMode;
            int rawSpeakers;
            result = lowLevel.getSoftwareFormat(out detectedSampleRate, out speakerMode, out rawSpeakers);
            int captureSampleRate = result == RESULT.OK && detectedSampleRate > 0 ? detectedSampleRate : 48000;

            List<string> failures = new List<string>();
            List<CapturedTrack> startedTracks = new List<CapturedTrack>();
            foreach (TrackRequest request in trackRequests) {
                if (TryStartTrack(lowLevel, request, purpose, captureSampleRate, rawSpeakers, out CapturedTrack track, out string trackWarning)) {
                    startedTracks.Add(track);
                } else if (!string.IsNullOrWhiteSpace(trackWarning)) {
                    failures.Add(trackWarning);
                }
            }

            if (startedTracks.Count == 0) {
                warning = failures.Count == 0 ? "No FMOD recorder tracks could be attached." : string.Join("; ", failures);
                lastError = warning;
                StopTracks(startedTracks);
                return false;
            }

            lock (Sync) {
                ActiveTracks.AddRange(startedTracks);
                lastError = string.Empty;
            }

            if (failures.Count > 0) {
                warning = string.Join("; ", failures);
            }

            return true;
        } catch (Exception ex) {
            warning = ex.Message;
            lastError = ex.Message;
            Stop();
            return false;
        }
    }

    public static AkronRecordedAudio Stop() {
        return Stop(AudioCapturePurpose.Manual);
    }

    public static AkronRecordedAudio StopReplayBuffer() {
        return Stop(AudioCapturePurpose.ReplayBuffer);
    }

    private static AkronRecordedAudio Stop(AudioCapturePurpose purpose) {
        List<CapturedTrack> completedTracks;
        lock (Sync) {
            completedTracks = ActiveTracks.Where(track => track.Purpose == purpose).ToList();
            ActiveTracks.RemoveAll(track => track.Purpose == purpose);
        }

        StopTracks(completedTracks);
        List<AkronRecordedAudioTrack> audioTracks = new List<AkronRecordedAudioTrack>();
        DateTime stoppedUtc = DateTime.UtcNow;
        foreach (CapturedTrack track in completedTracks) {
            long bytes = 0;
            try {
                if (!string.IsNullOrWhiteSpace(track.RawPath) && File.Exists(track.RawPath)) {
                    bytes = new FileInfo(track.RawPath).Length;
                }
            } catch (Exception ex) {
                lastError = ex.Message;
            }

            audioTracks.Add(new AkronRecordedAudioTrack(track.RawPath, track.DisplayName, track.SampleRate, Math.Max(1, track.Channels), bytes, track.StartedUtc, stoppedUtc));
        }

        return new AkronRecordedAudio(audioTracks);
    }

    private static bool TryStartTrack(FMOD.System lowLevel, TrackRequest request, AudioCapturePurpose purpose, int detectedSampleRate, int rawSpeakers, out CapturedTrack track, out string warning) {
        track = null;
        warning = string.Empty;
        ChannelGroup targetGroup;
        FMOD.Studio.Bus targetBus = default;
        bool busLocked = false;
        RESULT result;

        if (string.IsNullOrWhiteSpace(request.BusPath)) {
            result = lowLevel.getMasterChannelGroup(out targetGroup);
            if (result != RESULT.OK) {
                warning = request.DisplayName + " channel group unavailable: " + result;
                return false;
            }
        } else {
            result = Audio.System.getBus(request.BusPath, out targetBus);
            if (result != RESULT.OK) {
                warning = request.DisplayName + " bus unavailable at " + request.BusPath + ": " + result;
                return false;
            }

            result = targetBus.lockChannelGroup();
            if (result == RESULT.OK) {
                busLocked = true;
            }

            result = targetBus.getChannelGroup(out targetGroup);
            if (result != RESULT.OK) {
                if (busLocked) {
                    targetBus.unlockChannelGroup();
                }

                warning = request.DisplayName + " channel group unavailable: " + result;
                return false;
            }
        }

        string rawPath = Path.Combine(Path.GetTempPath(), "akron-audio-" + request.Id + "-" + Guid.NewGuid().ToString("N") + ".s16le");
        CapturedTrack pendingTrack = new CapturedTrack {
            Id = request.Id,
            DisplayName = request.DisplayName,
            Purpose = purpose,
            RawPath = rawPath,
            RawStream = new FileStream(rawPath, FileMode.Create, FileAccess.Write, FileShare.Read),
            SampleRate = detectedSampleRate > 0 ? detectedSampleRate : 48000,
            Channels = rawSpeakers > 0 ? rawSpeakers : 2,
            Gain = AkronModuleSettings.ClampRecordingAudioLevel(request.Level) / 100f,
            ChannelGroup = targetGroup,
            Bus = targetBus,
            BusLocked = busLocked,
            StartedUtc = DateTime.UtcNow,
            Active = true
        };

        pendingTrack.ReadCallback = (ref DSP_STATE dspState, IntPtr inputBuffer, IntPtr outputBuffer, uint length, int inputChannels, ref int outputChannels) =>
            ReadAudio(pendingTrack, ref dspState, inputBuffer, outputBuffer, length, inputChannels, ref outputChannels);

        DSP_DESCRIPTION description = new DSP_DESCRIPTION {
            pluginsdkversion = VERSION.number,
            name = BuildDspName("Akron " + request.DisplayName),
            version = 1,
            numinputbuffers = 1,
            numoutputbuffers = 1,
            read = pendingTrack.ReadCallback
        };

        result = lowLevel.createDSP(ref description, out DSP dsp);
        if (result != RESULT.OK) {
            pendingTrack.Dispose();
            warning = request.DisplayName + " DSP creation failed: " + result;
            return false;
        }

        pendingTrack.Dsp = dsp;
        // FMOD inserts index 0 before the channel-group fader. Capturing there keeps
        // isolated bus tracks useful when the in-game music/SFX fader is muted.
        result = targetGroup.addDSP(0, pendingTrack.Dsp);
        if (result != RESULT.OK) {
            pendingTrack.Dispose();
            warning = request.DisplayName + " DSP attach failed: " + result;
            return false;
        }

        track = pendingTrack;
        return true;
    }

    private static RESULT ReadAudio(CapturedTrack track, ref DSP_STATE dspState, IntPtr inputBuffer, IntPtr outputBuffer, uint length, int inputChannels, ref int outputChannels) {
        outputChannels = inputChannels;
        int channelCount = Math.Max(1, inputChannels);
        int sampleCount = checked((int) length * channelCount);
        if (sampleCount <= 0 || inputBuffer == IntPtr.Zero || outputBuffer == IntPtr.Zero) {
            return RESULT.OK;
        }

        float[] samples = new float[sampleCount];
        Marshal.Copy(inputBuffer, samples, 0, sampleCount);
        Marshal.Copy(samples, 0, outputBuffer, sampleCount);

        byte[] pcm = new byte[sampleCount * 2];
        float currentGain;
        lock (track.Sync) {
            track.Channels = channelCount;
            currentGain = track.Gain;
        }

        for (int i = 0; i < samples.Length; i++) {
            float scaled = Math.Max(-1f, Math.Min(1f, samples[i] * currentGain));
            short value = (short) Math.Round(scaled * short.MaxValue, MidpointRounding.AwayFromZero);
            int offset = i * 2;
            pcm[offset] = (byte) (value & 0xFF);
            pcm[offset + 1] = (byte) ((value >> 8) & 0xFF);
        }

        lock (track.Sync) {
            if (track.Active && track.RawStream != null) {
                track.RawStream.Write(pcm, 0, pcm.Length);
            }
        }

        return RESULT.OK;
    }

    private static List<TrackRequest> BuildTrackRequests(AkronModuleSettings settings) {
        List<TrackRequest> tracks = new List<TrackRequest>();
        if (settings.RecordingAudioFullMixTrack) {
            tracks.Add(new TrackRequest("full-mix", "Full Mix", string.Empty, settings.RecordingAudioFullMixLevel));
        }

        if (settings.RecordingAudioMusicTrack) {
            tracks.Add(new TrackRequest("music", "Music", "bus:/music/tunes/mains", settings.RecordingAudioMusicLevel));
        }

        if (settings.RecordingAudioSfxTrack) {
            tracks.Add(new TrackRequest("sfx", "SFX", "bus:/gameplay_sfx/game", settings.RecordingAudioSfxLevel));
        }

        if (settings.RecordingAudioAmbienceTrack) {
            tracks.Add(new TrackRequest("ambience", "Ambience", "bus:/gameplay_sfx/ambience", settings.RecordingAudioAmbienceLevel));
        }

        return tracks;
    }

    private static bool HasAnyIsolatedGameTrack(AkronModuleSettings settings) {
        return settings.RecordingAudioMusicTrack ||
               settings.RecordingAudioSfxTrack ||
               settings.RecordingAudioAmbienceTrack;
    }

    private static void StopTracks(List<CapturedTrack> tracks) {
        foreach (CapturedTrack track in tracks) {
            track.Dispose();
        }
    }

    private static char[] BuildDspName(string name) {
        char[] chars = new char[32];
        string value = string.IsNullOrWhiteSpace(name) ? "Akron Recorder" : name;
        for (int i = 0; i < Math.Min(chars.Length - 1, value.Length); i++) {
            chars[i] = value[i];
        }

        return chars;
    }

    private readonly struct TrackRequest {
        public TrackRequest(string id, string displayName, string busPath, int level) {
            Id = id;
            DisplayName = displayName;
            BusPath = busPath;
            Level = level;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string BusPath { get; }
        public int Level { get; }
    }

    private enum AudioCapturePurpose {
        Manual,
        ReplayBuffer
    }

    private sealed class CapturedTrack : IDisposable {
        public readonly object Sync = new object();
        public string Id;
        public string DisplayName;
        public AudioCapturePurpose Purpose;
        public string RawPath;
        public FileStream RawStream;
        public int SampleRate;
        public int Channels;
        public float Gain;
        public ChannelGroup ChannelGroup;
        public FMOD.Studio.Bus Bus;
        public bool BusLocked;
        public DSP Dsp;
        public DSP_READCALLBACK ReadCallback;
        public DateTime StartedUtc;
        public bool Active;

        public void Dispose() {
            lock (Sync) {
                Active = false;
                try {
                    RawStream?.Flush();
                    RawStream?.Dispose();
                } catch (Exception ex) {
                    lastError = ex.Message;
                }

                RawStream = null;
            }

            try {
                if (ReadCallback != null) {
                    ChannelGroup.removeDSP(Dsp);
                }
            } catch (Exception ex) {
                lastError = ex.Message;
            }

            try {
                if (ReadCallback != null) {
                    Dsp.release();
                }
            } catch (Exception ex) {
                lastError = ex.Message;
            }

            try {
                if (BusLocked) {
                    Bus.unlockChannelGroup();
                }
            } catch (Exception ex) {
                lastError = ex.Message;
            }

            Dsp = default;
            ChannelGroup = default;
            Bus = default;
            ReadCallback = null;
            BusLocked = false;
        }
    }
}

public readonly struct AkronRecordedAudio {
    public AkronRecordedAudio(IReadOnlyList<AkronRecordedAudioTrack> tracks) {
        Tracks = tracks ?? new List<AkronRecordedAudioTrack>();
    }

    public IReadOnlyList<AkronRecordedAudioTrack> Tracks { get; }
    public bool HasSamples => Tracks.Any(track => track.HasSamples);

    public string Describe() {
        return Tracks.Count.ToString(CultureInfo.InvariantCulture) + " track(s)";
    }
}

public readonly struct AkronRecordedAudioTrack {
    public AkronRecordedAudioTrack(string path, string displayName, int sampleRate, int channels, long bytes) {
        Path = path;
        DisplayName = displayName;
        SampleRate = sampleRate;
        Channels = channels;
        Bytes = bytes;
        StartedUtc = DateTime.MinValue;
        EndedUtc = DateTime.MinValue;
    }

    public AkronRecordedAudioTrack(string path, string displayName, int sampleRate, int channels, long bytes, DateTime startedUtc, DateTime endedUtc) {
        Path = path;
        DisplayName = displayName;
        SampleRate = sampleRate;
        Channels = channels;
        Bytes = bytes;
        StartedUtc = startedUtc;
        EndedUtc = endedUtc;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public int SampleRate { get; }
    public int Channels { get; }
    public long Bytes { get; }
    public DateTime StartedUtc { get; }
    public DateTime EndedUtc { get; }
    public bool HasSamples => !string.IsNullOrWhiteSpace(Path) && Bytes > 0;
}

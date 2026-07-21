using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FMOD;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.Akron;

internal enum AkronAudioSplitterStartDecision {
    None,
    Stop,
    Defer,
    Start
}

internal readonly struct AkronAudioRoutePlan {
    private AkronAudioRoutePlan(bool split, string originalDeviceName, string musicDeviceName) {
        Split = split;
        OriginalDeviceName = NormalizeDeviceName(originalDeviceName);
        MusicDeviceName = split ? NormalizeDeviceName(musicDeviceName) : string.Empty;
    }

    public bool Split { get; }
    public string OriginalDeviceName { get; }
    public string MusicDeviceName { get; }

    public static AkronAudioRoutePlan Resolve(bool split, string mainDeviceName, string musicDeviceName, string sfxDeviceName) {
        return split
            ? new AkronAudioRoutePlan(true, sfxDeviceName, musicDeviceName)
            : new AkronAudioRoutePlan(false, mainDeviceName, string.Empty);
    }

    public static AkronAudioSplitterStartDecision ResolveStartDecision(bool configured, bool active, bool audioAvailable) {
        if (!configured) {
            return active ? AkronAudioSplitterStartDecision.Stop : AkronAudioSplitterStartDecision.None;
        }
        if (active) {
            return AkronAudioSplitterStartDecision.None;
        }
        return audioAvailable ? AkronAudioSplitterStartDecision.Start : AkronAudioSplitterStartDecision.Defer;
    }

    public static bool ShouldApplyMainRouteOnInitialize(bool configured, bool audioAvailable) {
        return !configured && audioAvailable;
    }

    private static string NormalizeDeviceName(string value) {
        return string.IsNullOrWhiteSpace(value) ? "Default" : value.Trim();
    }
}

internal sealed class AkronAudioPcmBuffer {
    private readonly float[] samples;
    private readonly int channelCount;
    private readonly int minimumBufferedSamples;
    // The cursors and sample slots form one ring-buffer transaction. Protecting
    // only the cursors lets a writer overwrite slots while the reader copies them.
    private readonly object sync = new object();
    private int primed;
    private long readPosition;
    private long writePosition;

    public AkronAudioPcmBuffer(int capacity, int minimumBufferedSamples = 0, int channelCount = 1) {
        samples = new float[Math.Max(1, capacity)];
        this.channelCount = Math.Max(1, channelCount);
        this.minimumBufferedSamples = Math.Clamp(minimumBufferedSamples, 0, samples.Length);
    }

    public int Capacity => samples.Length;

    public void Clear() {
        lock (sync) {
            readPosition = 0;
            writePosition = 0;
            primed = 0;
            Array.Clear(samples, 0, samples.Length);
        }
    }

    public void Write(float[] source) {
        if (source == null || source.Length == 0) {
            return;
        }

        unsafe {
            fixed (float* sourcePointer = source) {
                Write((IntPtr) sourcePointer, source.Length);
            }
        }
    }

    public int Read(float[] destination) {
        if (destination == null || destination.Length == 0) {
            return 0;
        }

        unsafe {
            fixed (float* destinationPointer = destination) {
                return Read((IntPtr) destinationPointer, destination.Length);
            }
        }
    }

    public unsafe void Write(IntPtr source, int count) {
        if (source == IntPtr.Zero || count <= 0) {
            return;
        }

        lock (sync) {
            float* sourceSamples = (float*) source;
            long start = writePosition;
            int sourceOffset = Math.Max(0, count - samples.Length);
            for (int index = sourceOffset; index < count; index++) {
                samples[(start + index) % samples.Length] = sourceSamples[index];
            }

            writePosition = start + count;
        }
    }

    public unsafe int Read(IntPtr destination, int count) {
        if (destination == IntPtr.Zero || count <= 0) {
            return 0;
        }

        lock (sync) {
            float* destinationSamples = (float*) destination;
            long write = writePosition;
            long read = readPosition;
            long oldestAvailable = Math.Max(0, write - samples.Length);
            if (read < oldestAvailable) {
                read = oldestAvailable;
            }

            long availableSamples = Math.Max(0, write - read);
            if (minimumBufferedSamples > 0) {
                if (primed == 0) {
                    if (availableSamples < minimumBufferedSamples) {
                        new Span<float>(destinationSamples, count).Clear();
                        return 0;
                    }
                    primed = 1;
                }

                if (availableSamples < count) {
                    // Do not consume a partial block. Silence one callback and wait
                    // for the producer to restore the configured latency cushion.
                    primed = 0;
                    new Span<float>(destinationSamples, count).Clear();
                    return 0;
                }

                long highWatermark = Math.Min(samples.Length, (long) minimumBufferedSamples * 2);
                if (availableSamples > highWatermark) {
                    long samplesToSkip = availableSamples - minimumBufferedSamples;
                    samplesToSkip -= samplesToSkip % channelCount;
                    read += samplesToSkip;
                    availableSamples -= samplesToSkip;
                }
            }

            int available = (int) Math.Min(count, availableSamples);
            for (int index = 0; index < available; index++) {
                destinationSamples[index] = samples[(read + index) % samples.Length];
            }
            for (int index = available; index < count; index++) {
                destinationSamples[index] = 0f;
            }

            readPosition = read + available;
            return available;
        }
    }
}

public static class AkronAudioSplitter {
    private const string MusicBusPath = "bus:/music";
    private const int DevicePollFrames = 120;
    private const int PcmBufferSeconds = 2;
    private const int StartRetryFrames = 30;

    private static readonly object LifecycleSync = new object();

    private static FMOD.System musicOutputSystem;
    private static Sound musicStream;
    private static Channel musicChannel;
    private static Bus sourceMusicBus;
    private static ChannelGroup sourceMusicGroup;
    private static DSP sourceCaptureDsp;
    private static AkronAudioPcmBuffer pcmBuffer;
    private static DSP_READCALLBACK sourceCaptureCallback;
    private static SOUND_PCMREADCALLBACK musicReadCallback;
    private static bool sourceBusLocked;
    private static bool hooksInstalled;
    private static bool active;
    private static int startRetryFrames;
    private static int devicePollFrames;
    private static string lastError = string.Empty;

    public static bool Active {
        get {
            lock (LifecycleSync) {
                return active;
            }
        }
    }

    public static string LastError {
        get {
            lock (LifecycleSync) {
                return lastError;
            }
        }
    }

    public static void Initialize() {
        lock (LifecycleSync) {
            ReconcileSetting();
            AkronModuleSettings settings = AkronModule.TryGetSettings();
            if (AkronAudioRoutePlan.ShouldApplyMainRouteOnInitialize(
                    settings?.AudioSplitter == true,
                    GetCelesteLowLevelSystem() != null)) {
                ApplyMainDevice();
            }
        }
    }

    public static void Load() {
        lock (LifecycleSync) {
            if (hooksInstalled) {
                return;
            }

            On.Celeste.Audio.Init += AudioOnInit;
            On.Celeste.Audio.Unload += AudioOnUnload;
            hooksInstalled = true;
        }
    }

    public static void Unload() {
        lock (LifecycleSync) {
            if (hooksInstalled) {
                On.Celeste.Audio.Init -= AudioOnInit;
                On.Celeste.Audio.Unload -= AudioOnUnload;
                hooksInstalled = false;
            }

            StopSplitOutput();
            ApplyMainDevice();
        }
    }

    public static void Update() {
        lock (LifecycleSync) {
            if (startRetryFrames > 0) {
                startRetryFrames--;
            }
            ReconcileSetting();
            if (!active || musicOutputSystem == null) {
                return;
            }

            RESULT updateResult = musicOutputSystem.update();
            if (updateResult != RESULT.OK) {
                FailAndRestoreMain("Music output update failed: " + updateResult);
                return;
            }

            devicePollFrames++;
            if (devicePollFrames >= DevicePollFrames) {
                devicePollFrames = 0;
                ApplyCurrentRouteDevices();
            }
        }
    }

    public static void SetEnabled(bool enabled) {
        lock (LifecycleSync) {
            AkronModuleSettings settings = AkronModule.TryGetSettings();
            if (settings == null) {
                return;
            }

            if (!enabled) {
                settings.AudioSplitter = false;
                StopSplitOutput();
                ApplyMainDevice();
                return;
            }

            AkronAudioSplitterStartAttempt attempt = TryStartSplitOutput(out string error);
            settings.AudioSplitter = attempt != AkronAudioSplitterStartAttempt.Failed;
            if (attempt == AkronAudioSplitterStartAttempt.Deferred) {
                lastError = string.Empty;
            } else if (attempt == AkronAudioSplitterStartAttempt.Failed) {
                lastError = error;
                ApplyMainDevice();
                Engine.Scene?.Add(new AkronToast("Audio Splitter: " + error));
            }
        }
    }

    public static void SetMainDevice(string deviceName) {
        SetDeviceSelection(AkronAudioDeviceRoute.Main, deviceName);
    }

    public static void SetMusicDevice(string deviceName) {
        SetDeviceSelection(AkronAudioDeviceRoute.Music, deviceName);
    }

    public static void SetSfxDevice(string deviceName) {
        SetDeviceSelection(AkronAudioDeviceRoute.Sfx, deviceName);
    }

    public static string Describe() {
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        if (settings?.AudioSplitter != true) {
            return "Off";
        }

        return Active ? "On" : "Unavailable";
    }

    public static IReadOnlyList<string> ListDevices() {
        lock (LifecycleSync) {
            return EnumerateDevices(GetCelesteLowLevelSystem()).ConvertAll(device => device.Name);
        }
    }

    public static int ReloadDevices() {
        lock (LifecycleSync) {
            List<AkronAudioDevice> devices = EnumerateDevices(GetCelesteLowLevelSystem());
            if (active) {
                ApplyCurrentRouteDevices(devices);
            } else {
                ApplyMainDevice(devices);
            }

            return devices.Count;
        }
    }

    public static string Status() {
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        if (settings == null) {
            return "unavailable";
        }

        string state = Active ? "on" : settings.AudioSplitter ? "error" : "off";
        string error = LastError;
        return "state=" + state +
               " main=" + settings.AudioSplitterMainDevice +
               " music=" + settings.AudioSplitterMusicDevice +
               " sfx=" + settings.AudioSplitterSfxDevice +
               (string.IsNullOrWhiteSpace(error) ? string.Empty : " error=" + error);
    }

    private static void ReconcileSetting() {
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        if (settings == null) {
            return;
        }

        AkronAudioSplitterStartDecision decision = AkronAudioRoutePlan.ResolveStartDecision(
            settings.AudioSplitter,
            active,
            GetCelesteLowLevelSystem() != null);
        switch (decision) {
            case AkronAudioSplitterStartDecision.None:
            case AkronAudioSplitterStartDecision.Defer:
                return;
            case AkronAudioSplitterStartDecision.Stop:
                StopSplitOutput();
                ApplyMainDevice();
                return;
            case AkronAudioSplitterStartDecision.Start:
                if (startRetryFrames > 0) {
                    return;
                }

                AkronAudioSplitterStartAttempt attempt = TryStartSplitOutput(out string error);
                if (attempt == AkronAudioSplitterStartAttempt.Failed) {
                    settings.AudioSplitter = false;
                    lastError = error;
                    ApplyMainDevice();
                    Logger.Log(LogLevel.Error, nameof(AkronModule), "Audio Splitter could not start: " + error);
                }
                return;
        }
    }

    private static AkronAudioSplitterStartAttempt TryStartSplitOutput(out string error) {
        error = string.Empty;
        if (active) {
            return AkronAudioSplitterStartAttempt.Started;
        }

        StopSplitOutput();
        FMOD.System celesteLowLevel = GetCelesteLowLevelSystem();
        if (celesteLowLevel == null) {
            startRetryFrames = StartRetryFrames;
            return AkronAudioSplitterStartAttempt.Deferred;
        }

        lastError = string.Empty;
        try {
            Require(celesteLowLevel.getSoftwareFormat(out int sampleRate, out SPEAKERMODE speakerMode, out int rawSpeakerCount), "read Celeste audio format");
            int channelCount = ResolveChannelCount(speakerMode, rawSpeakerCount);
            if (sampleRate <= 0 || channelCount <= 0) {
                throw new InvalidOperationException("Celeste reported an invalid audio format.");
            }

            celesteLowLevel.getDSPBufferSize(out uint dspBufferLength, out int dspBufferCount);
            int capacity = checked(sampleRate * channelCount * PcmBufferSeconds);
            int startupBuffer = checked((int) Math.Max(dspBufferLength, 1024u) * channelCount * 4);
            pcmBuffer = new AkronAudioPcmBuffer(capacity, startupBuffer, channelCount);

            Require(Factory.System_Create(out musicOutputSystem), "create the music output system");
            Require(musicOutputSystem.setSoftwareFormat(sampleRate, speakerMode, rawSpeakerCount), "configure the music output format");
            if (dspBufferLength > 0 && dspBufferCount > 0) {
                Require(musicOutputSystem.setDSPBufferSize(dspBufferLength, dspBufferCount), "configure the music output buffer");
            }

            AkronAudioRoutePlan route = AkronAudioRoutePlan.Resolve(
                true,
                AkronModule.Settings.AudioSplitterMainDevice,
                AkronModule.Settings.AudioSplitterMusicDevice,
                AkronModule.Settings.AudioSplitterSfxDevice);
            List<AkronAudioDevice> musicDevices = EnumerateDevices(musicOutputSystem);
            ApplyDevice(musicOutputSystem, route.MusicDeviceName, musicDevices, "Music");
            Require(musicOutputSystem.init(32, FMOD.INITFLAGS.NORMAL, IntPtr.Zero), "initialize the music output system");
            CreateMusicStream(sampleRate, channelCount, dspBufferLength);
            AttachMusicCapture(celesteLowLevel);
            ApplyDevice(celesteLowLevel, route.OriginalDeviceName, EnumerateDevices(celesteLowLevel), "SFX");
            Require(musicChannel.setPaused(false), "start the music output stream");

            active = true;
            devicePollFrames = 0;
            return AkronAudioSplitterStartAttempt.Started;
        } catch (AkronAudioSplitterDeferredException) {
            StopSplitOutput();
            startRetryFrames = StartRetryFrames;
            return AkronAudioSplitterStartAttempt.Deferred;
        } catch (Exception exception) {
            error = exception.Message;
            StopSplitOutput();
            return AkronAudioSplitterStartAttempt.Failed;
        }
    }

    private static void CreateMusicStream(int sampleRate, int channelCount, uint dspBufferLength) {
        musicReadCallback = ReadMusicOutput;
        CREATESOUNDEXINFO createInfo = new CREATESOUNDEXINFO {
            cbsize = Marshal.SizeOf<CREATESOUNDEXINFO>(),
            length = checked((uint) (sampleRate * channelCount * sizeof(float) * PcmBufferSeconds)),
            numchannels = channelCount,
            defaultfrequency = sampleRate,
            format = SOUND_FORMAT.PCMFLOAT,
            decodebuffersize = dspBufferLength > 0 ? dspBufferLength : 1024,
            pcmreadcallback = musicReadCallback
        };
        MODE mode = MODE.OPENUSER | MODE.CREATESTREAM | MODE.LOOP_NORMAL | MODE._2D;
        Require(musicOutputSystem.createSound(string.Empty, mode, ref createInfo, out musicStream), "create the music PCM stream");
        Require(musicOutputSystem.playSound(musicStream, null, true, out musicChannel), "create the music output channel");
    }

    private static void AttachMusicCapture(FMOD.System celesteLowLevel) {
        RESULT busResult = Audio.System.getBus(MusicBusPath, out sourceMusicBus);
        if (ShouldRetryMusicBusLookupAfter(busResult)) {
            throw new AkronAudioSplitterDeferredException();
        }
        Require(busResult, "find " + MusicBusPath);
        Require(sourceMusicBus.lockChannelGroup(), "lock the music bus");
        sourceBusLocked = true;
        RESULT flushResult = Audio.System.flushCommands();
        if (ShouldRetryStartAfter(flushResult)) {
            throw new AkronAudioSplitterDeferredException();
        }
        Require(flushResult, "flush the music bus lock");
        RESULT channelGroupResult = sourceMusicBus.getChannelGroup(out sourceMusicGroup);
        if (ShouldRetryStartAfter(channelGroupResult)) {
            throw new AkronAudioSplitterDeferredException();
        }
        Require(channelGroupResult, "get the music channel group");

        sourceCaptureCallback = CaptureAndSilenceMusic;
        DSP_DESCRIPTION description = new DSP_DESCRIPTION {
            pluginsdkversion = VERSION.number,
            name = BuildDspName("Akron Audio Splitter"),
            version = 1,
            numinputbuffers = 1,
            numoutputbuffers = 1,
            read = sourceCaptureCallback
        };
        Require(celesteLowLevel.createDSP(ref description, out sourceCaptureDsp), "create the music capture DSP");

        // TAIL receives the fully processed music signal. The callback forwards
        // that signal to Akron's output system and writes silence back to Celeste,
        // so snapshots, modded banks, and runtime event state stay authoritative.
        Require(sourceMusicGroup.addDSP(CHANNELCONTROL_DSP_INDEX.TAIL, sourceCaptureDsp), "attach the music capture DSP");
    }

    private static RESULT CaptureAndSilenceMusic(ref DSP_STATE state, IntPtr inputBuffer, IntPtr outputBuffer, uint length, int inputChannels, ref int outputChannels) {
        outputChannels = inputChannels;
        int channelCount = Math.Max(1, inputChannels);
        if (length > int.MaxValue / channelCount) {
            return RESULT.ERR_INVALID_PARAM;
        }

        int sampleCount = (int) length * channelCount;
        AkronAudioPcmBuffer buffer = pcmBuffer;
        if (buffer != null && inputBuffer != IntPtr.Zero && sampleCount > 0) {
            buffer.Write(inputBuffer, sampleCount);
        }

        if (outputBuffer != IntPtr.Zero && sampleCount > 0) {
            unsafe {
                new Span<float>((void*) outputBuffer, sampleCount).Clear();
            }
        }

        return RESULT.OK;
    }

    private static RESULT ReadMusicOutput(IntPtr soundRaw, IntPtr destination, uint byteCount) {
        if (byteCount > int.MaxValue) {
            return RESULT.ERR_INVALID_PARAM;
        }

        int sampleCount = (int) byteCount / sizeof(float);
        AkronAudioPcmBuffer buffer = pcmBuffer;
        if (buffer != null) {
            buffer.Read(destination, sampleCount);
        } else if (destination != IntPtr.Zero && sampleCount > 0) {
            unsafe {
                new Span<float>((void*) destination, sampleCount).Clear();
            }
        }
        return RESULT.OK;
    }

    private static void SetDeviceSelection(AkronAudioDeviceRoute route, string deviceName) {
        lock (LifecycleSync) {
            AkronModuleSettings settings = AkronModule.TryGetSettings();
            if (settings == null) {
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(deviceName) ? "Default" : deviceName.Trim();
            switch (route) {
                case AkronAudioDeviceRoute.Main:
                    settings.AudioSplitterMainDevice = normalized;
                    if (!active) {
                        ApplyMainDevice();
                    }
                    break;
                case AkronAudioDeviceRoute.Music:
                    settings.AudioSplitterMusicDevice = normalized;
                    if (active && musicOutputSystem != null) {
                        lastError = string.Empty;
                        ApplyDevice(musicOutputSystem, normalized, EnumerateDevices(musicOutputSystem), "Music");
                    }
                    break;
                case AkronAudioDeviceRoute.Sfx:
                    settings.AudioSplitterSfxDevice = normalized;
                    if (active) {
                        lastError = string.Empty;
                        ApplyDevice(GetCelesteLowLevelSystem(), normalized, EnumerateDevices(GetCelesteLowLevelSystem()), "SFX");
                    }
                    break;
            }
        }
    }

    private static void ApplyCurrentRouteDevices() {
        ApplyCurrentRouteDevices(EnumerateDevices(GetCelesteLowLevelSystem()));
    }

    private static void ApplyCurrentRouteDevices(List<AkronAudioDevice> devices) {
        FMOD.System celesteLowLevel = GetCelesteLowLevelSystem();
        if (celesteLowLevel == null || musicOutputSystem == null) {
            return;
        }

        AkronAudioRoutePlan route = AkronAudioRoutePlan.Resolve(
            true,
            AkronModule.Settings.AudioSplitterMainDevice,
            AkronModule.Settings.AudioSplitterMusicDevice,
            AkronModule.Settings.AudioSplitterSfxDevice);
        lastError = string.Empty;
        ApplyDevice(celesteLowLevel, route.OriginalDeviceName, devices, "SFX");
        ApplyDevice(musicOutputSystem, route.MusicDeviceName, EnumerateDevices(musicOutputSystem), "Music");
    }

    private static void ApplyMainDevice() {
        ApplyMainDevice(EnumerateDevices(GetCelesteLowLevelSystem()));
    }

    private static void ApplyMainDevice(List<AkronAudioDevice> devices) {
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        AkronAudioRoutePlan route = AkronAudioRoutePlan.Resolve(
            false,
            settings?.AudioSplitterMainDevice,
            settings?.AudioSplitterMusicDevice,
            settings?.AudioSplitterSfxDevice);
        ApplyDevice(GetCelesteLowLevelSystem(), route.OriginalDeviceName, devices, "Main");
    }

    private static void ApplyDevice(FMOD.System system, string selectedName, List<AkronAudioDevice> devices, string routeName) {
        if (system == null) {
            return;
        }

        string normalized = string.IsNullOrWhiteSpace(selectedName) ? "Default" : selectedName.Trim();
        int selectedListIndex = devices.FindIndex(device => string.Equals(device.Name, normalized, StringComparison.OrdinalIgnoreCase));
        if (selectedListIndex < 0) {
            selectedListIndex = devices.FindIndex(device => string.Equals(device.Name, "Default", StringComparison.OrdinalIgnoreCase));
            lastError = routeName + " device '" + normalized + "' is unavailable; using Default.";
        }

        AkronAudioDevice selected = selectedListIndex >= 0
            ? devices[selectedListIndex]
            : new AkronAudioDevice(0, "Default");

        if (system.getDriver(out int currentDriver) == RESULT.OK && currentDriver == selected.Index) {
            return;
        }

        RESULT result = system.setDriver(selected.Index);
        if (result != RESULT.OK) {
            RESULT fallbackResult = selected.Index == 0 ? result : system.setDriver(0);
            lastError = DescribeDeviceSwitchFailure(routeName, result, selected.Index, fallbackResult);
        }
    }

    private static List<AkronAudioDevice> EnumerateDevices(FMOD.System system) {
        List<AkronAudioDevice> devices = new List<AkronAudioDevice> {
            new AkronAudioDevice(0, "Default")
        };
        if (system == null || system.getNumDrivers(out int driverCount) != RESULT.OK) {
            return devices;
        }

        for (int index = 0; index < driverCount; index++) {
            StringBuilder name = new StringBuilder(256);
            if (system.getDriverInfo(index, name, name.Capacity, out _, out _, out _, out _) != RESULT.OK) {
                continue;
            }

            string displayName = name.ToString();
            if (string.IsNullOrWhiteSpace(displayName)) {
                displayName = "Device " + index.ToString(CultureInfo.InvariantCulture);
            }

            if (index == 0 && string.Equals(displayName, "Default", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            devices.Add(new AkronAudioDevice(index, displayName));
        }

        return devices;
    }

    private static FMOD.System GetCelesteLowLevelSystem() {
        try {
            if (!Audio.AudioInitialized || Audio.System == null || !Audio.System.isValid()) {
                return null;
            }
            return Audio.System.getLowLevelSystem(out FMOD.System lowLevel) == RESULT.OK ? lowLevel : null;
        } catch {
            return null;
        }
    }

    private static void AudioOnInit(On.Celeste.Audio.orig_Init orig) {
        orig();
        Initialize();
    }

    private static void AudioOnUnload(On.Celeste.Audio.orig_Unload orig) {
        lock (LifecycleSync) {
            // Release every object tied to Celeste's current FMOD system before
            // Celeste destroys that system. Initialize() rebuilds the route later.
            StopSplitOutput();
        }
        orig();
    }

    private static void FailAndRestoreMain(string error) {
        lastError = error;
        AkronModuleSettings settings = AkronModule.TryGetSettings();
        if (settings != null) {
            settings.AudioSplitter = false;
        }
        StopSplitOutput();
        ApplyMainDevice();
        Logger.Log(LogLevel.Error, nameof(AkronModule), "Audio Splitter stopped: " + error);
    }

    private static void StopSplitOutput() {
        active = false;
        startRetryFrames = 0;
        devicePollFrames = 0;

        TryFmod(() => sourceMusicGroup?.removeDSP(sourceCaptureDsp));
        TryFmod(() => sourceCaptureDsp?.release());
        if (sourceBusLocked) {
            TryFmod(() => sourceMusicBus?.unlockChannelGroup());
        }
        sourceBusLocked = false;
        sourceCaptureDsp = null;
        sourceMusicGroup = null;
        sourceMusicBus = null;
        sourceCaptureCallback = null;

        TryFmod(() => musicChannel?.stop());
        TryFmod(() => musicStream?.release());
        TryFmod(() => musicOutputSystem?.close());
        TryFmod(() => musicOutputSystem?.release());
        musicChannel = null;
        musicStream = null;
        musicOutputSystem = null;
        musicReadCallback = null;
        pcmBuffer?.Clear();
        pcmBuffer = null;
    }

    private static void TryFmod(Action operation) {
        try {
            operation?.Invoke();
        } catch {
        }
    }

    private static void Require(RESULT result, string operation) {
        if (result != RESULT.OK) {
            throw new InvalidOperationException("Could not " + operation + ": " + result + ".");
        }
    }

    internal static bool ShouldRetryStartAfter(RESULT result) {
        return result == RESULT.ERR_STUDIO_NOT_LOADED;
    }

    internal static bool ShouldRetryMusicBusLookupAfter(RESULT result) {
        return result == RESULT.ERR_EVENT_NOTFOUND || ShouldRetryStartAfter(result);
    }

    internal static string DescribeDeviceSwitchFailure(string routeName, RESULT selectedResult, int selectedIndex, RESULT fallbackResult) {
        if (selectedIndex == 0) {
            return routeName + " default device switch failed: " + selectedResult + ".";
        }
        if (fallbackResult == RESULT.OK) {
            return routeName + " device switch failed: " + selectedResult + "; using Default.";
        }
        return routeName + " device switch failed: " + selectedResult + "; Default fallback failed: " + fallbackResult + ".";
    }

    private static int ResolveChannelCount(SPEAKERMODE speakerMode, int rawSpeakerCount) {
        if (speakerMode == SPEAKERMODE.RAW && rawSpeakerCount > 0) {
            return rawSpeakerCount;
        }

        return speakerMode switch {
            SPEAKERMODE.MONO => 1,
            SPEAKERMODE.QUAD => 4,
            SPEAKERMODE.SURROUND => 5,
            SPEAKERMODE._5POINT1 => 6,
            SPEAKERMODE._7POINT1 => 8,
            SPEAKERMODE._7POINT1POINT4 => 12,
            _ => 2
        };
    }

    private static char[] BuildDspName(string name) {
        char[] result = new char[32];
        for (int index = 0; index < Math.Min(result.Length - 1, name.Length); index++) {
            result[index] = name[index];
        }
        return result;
    }

    private readonly struct AkronAudioDevice {
        public AkronAudioDevice(int index, string name) {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
    }

    private enum AkronAudioDeviceRoute {
        Main,
        Music,
        Sfx
    }

    private enum AkronAudioSplitterStartAttempt {
        Started,
        Deferred,
        Failed
    }

    private sealed class AkronAudioSplitterDeferredException : Exception {
    }
}

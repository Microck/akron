using System;
using System.Collections.Generic;
using System.Linq;
using Celeste;
using FMOD;
using FMOD.Studio;
using Monocle;

namespace Celeste.Mod.Akron;

// Event pitch changes playback speed too. Pitch-only DSP compensation separates
// the two controls while leaving unrelated buses/events untouched.
internal static class AkronAudioTransform {
    internal readonly record struct Plan(float Speed, float TargetPitch) {
        internal float[] Stages => BuildStages(TargetPitch / Speed);
        internal float CompensationGain => MathF.Min(1f, MathF.Sqrt(TargetPitch / Speed));
    }

    private sealed class Owner {
        internal EventInstance Event;
        internal FMOD.Studio.System System;
        internal ChannelGroup Group;
        internal IntPtr GroupHandle;
        internal float OriginalPitch;
        internal float AppliedPitch;
        internal Plan Plan;
        internal readonly List<DSP> Dsps = new();
        internal readonly HashSet<IntPtr> Attached = new();
        internal void TrackAttached(DSP dsp) => Attached.Add(dsp.getRaw());
        internal bool Retiring;
        internal bool PitchRestored;
        internal bool PauseOwned;
    }

    private static readonly Dictionary<IntPtr, Owner> Owners = new();
    private static readonly Dictionary<IntPtr, Plan> YieldedOwners = new();
    private static readonly HashSet<IntPtr> ReleasedEvents = new();
    private const string OwnershipConflict = "Another writer changed event pitch; toggle audio controls to reacquire.";
    private static bool initialized;
    private static bool hooksInstalled;
    internal static string LastError { get; private set; } = string.Empty;
    internal static int OwnedEventCount => Owners.Count;
    internal static int OwnedDspCount => Owners.Values.Sum(owner => owner.Dsps.Count);

    internal static float Normalize(float value) => !float.IsFinite(value) || value <= 0 ? 1f : Math.Clamp(value, .1f, 4f);

    internal static Plan Resolve(float speed, bool pitchEnabled, AkronPitchPolicy policy, float independentPitch) {
        speed = Normalize(speed);
        float target = !pitchEnabled || policy == AkronPitchPolicy.FollowSpeed ? speed :
            policy == AkronPitchPolicy.Independent ? Normalize(independentPitch) : 1f;
        return new Plan(speed, target);
    }

    internal static float[] BuildStages(float ratio) {
        if (!float.IsFinite(ratio) || ratio < .025f - .000001f || ratio > 40f + .00001f)
            throw new ArgumentOutOfRangeException(nameof(ratio));
        var stages = new List<float>(6);
        while (ratio > 2f) { stages.Add(2f); ratio /= 2f; }
        while (ratio < .5f) { stages.Add(.5f); ratio *= 2f; }
        if (Math.Abs(ratio - 1f) > .000001f) stages.Add(ratio);
        return stages.ToArray();
    }

    internal static void Initialize() {
        if (initialized) return;
        if (!hooksInstalled) {
            try {
                On.Celeste.Audio.Update += AudioOnUpdate;
                On.Celeste.Audio.Unload += AudioOnUnload;
                On.Celeste.Audio.CreateInstance += AudioOnCreateInstance;
                On.FMOD.Studio.EventInstance.release += EventOnRelease;
                hooksInstalled = true;
            } catch {
                DetachHooks();
                throw;
            }
        }
        initialized = true;
    }

    internal static void Unload() {
        initialized = false;
        Reset();
        // A native detach failure must retain its handles. Keep only cleanup
        // callbacks alive until retirement succeeds or the audio system dies.
        PruneReleasedEvents();
        if (CanDetachHooks(Owners.Count, ReleasedEvents.Count)) DetachHooks();
    }

    internal static bool CanDetachHooks(int owners, int released) => owners == 0 && released == 0;

    private static void PruneReleasedEvents() {
        EventInstance[] current = { Audio.CurrentMusicEventInstance, Audio.CurrentAmbienceEventInstance, Audio.currentAltMusicEvent };
        var live = new HashSet<IntPtr>(current.Where(instance => instance != null && instance.isValid()).Select(instance => instance.getRaw()));
        ReleasedEvents.RemoveWhere(handle => !live.Contains(handle));
    }

    private static void DetachHooks() {
        // A released looping/fading current event may remain valid. Keep creation
        // tracking alive until it retires, so both re-entry and address reuse are safe.
        if (!CanDetachHooks(Owners.Count, ReleasedEvents.Count)) return;
        On.Celeste.Audio.Update -= AudioOnUpdate;
        On.Celeste.Audio.Unload -= AudioOnUnload;
        On.Celeste.Audio.CreateInstance -= AudioOnCreateInstance;
        On.FMOD.Studio.EventInstance.release -= EventOnRelease;
        hooksInstalled = false;
        ReleasedEvents.Clear(); // Creation tracking is offline; old addresses cannot remain tombstoned.
    }

    private static void AudioOnUpdate(On.Celeste.Audio.orig_Update orig) {
        orig();
        PruneReleasedEvents();
        if (!initialized) {
            Reset();
            if (CanDetachHooks(Owners.Count, ReleasedEvents.Count)) DetachHooks();
            return;
        }
        // This is the sole reconciliation boundary, including menu music. Engine
        // and Level updates can be skipped by other mods or modal overlays.
        if (AkronModule.EnabledThisSession && AkronModule.TryGetSettings() != null)
            AkronRuntimeOptions.ApplyAudioSpeedAndPitch();
        else Reset();
    }

    private static void AudioOnUnload(On.Celeste.Audio.orig_Unload orig) {
        Reset();
        orig();
        // A failed earlier shutdown can leave owners from a different system.
        foreach (IntPtr key in Owners.Keys.ToArray()) {
            FMOD.Studio.System system = Owners[key].System;
            if (system == null || !system.isValid()) Owners.Remove(key);
        }
        PruneReleasedEvents();
        if (!initialized && CanDetachHooks(Owners.Count, ReleasedEvents.Count)) DetachHooks();
    }

    private static EventInstance AudioOnCreateInstance(On.Celeste.Audio.orig_CreateInstance orig, string path, Microsoft.Xna.Framework.Vector2? position) {
        EventInstance instance = orig(path, position);
        if (instance != null) ReleasedEvents.Remove(instance.getRaw());
        return instance;
    }

    private static RESULT EventOnRelease(On.FMOD.Studio.EventInstance.orig_release orig, EventInstance self) {
        IntPtr handle = self?.getRaw() ?? IntPtr.Zero;
        bool current = handle != IntPtr.Zero &&
            (Audio.CurrentMusicEventInstance?.getRaw() == handle ||
             Audio.CurrentAmbienceEventInstance?.getRaw() == handle ||
             Audio.currentAltMusicEvent?.getRaw() == handle);
        bool tracked = ShouldRememberRelease(Owners.ContainsKey(handle), YieldedOwners.ContainsKey(handle), current);
        if (self != null) {
            Remove(self.getRaw());
            YieldedOwners.Remove(self.getRaw());
        }
        RESULT result = orig(self);
        if (tracked && result == RESULT.OK) ReleasedEvents.Add(self.getRaw());
        return result;
    }

    internal static bool ShouldRememberRelease(bool owned, bool yielded, bool current) => owned || yielded || current;

    internal static float OriginalPitchForCapture(EventInstance instance, float observed) {
        return instance != null && Owners.TryGetValue(instance.getRaw(), out Owner owner) &&
            Math.Abs(observed - owner.AppliedPitch) < .00001f ? owner.OriginalPitch : observed;
    }

    internal static void Apply(Plan plan) {
        EventInstance[] events = { Audio.CurrentMusicEventInstance, Audio.CurrentAmbienceEventInstance, Audio.currentAltMusicEvent };
        var wanted = new HashSet<IntPtr>(events.Where(e => e != null && e.isValid()).Select(e => e.getRaw()));
        foreach (IntPtr key in Owners.Keys.ToArray()) if (Owners[key].Retiring || !wanted.Contains(key)) Remove(key);
        foreach (IntPtr key in YieldedOwners.Keys.ToArray()) if (!wanted.Contains(key)) YieldedOwners.Remove(key);
        ReleasedEvents.RemoveWhere(key => !wanted.Contains(key));
        var visited = new HashSet<IntPtr>();
        var groups = new HashSet<IntPtr>();
        string failure = string.Empty;
        foreach (EventInstance instance in events) {
            if (instance == null || !instance.isValid() || !visited.Add(instance.getRaw())) continue;
            if (ReleasedEvents.Contains(instance.getRaw())) continue;
            if (YieldedOwners.TryGetValue(instance.getRaw(), out Plan yielded)) {
                if (yielded == plan) { failure = OwnershipConflict; continue; }
                YieldedOwners.Remove(instance.getRaw());
            }
            try {
                ApplyOne(instance, plan, groups);
            } catch (Exception exception) {
                Remove(instance.getRaw());
                failure = exception.Message;
            }
        }
        if (failure.Length == 0 && Owners.Values.Any(owner => owner.Retiring))
            failure = "Waiting for audio transform cleanup.";
        if (failure.Length > 0 && failure != LastError)
            Logger.Log(LogLevel.Warn, nameof(AkronAudioTransform), "Audio transform deferred: " + failure);
        LastError = failure;
    }

    private static void ApplyOne(EventInstance instance, Plan plan, HashSet<IntPtr> groups) {
        IntPtr key = instance.getRaw();
        float[] stages = plan.Stages;
        ChannelGroup group = null;
        IntPtr groupHandle = IntPtr.Zero;
        if (stages.Length > 0) {
            Require(instance.getChannelGroup(out group), "get event channel group");
            if (group == null || (groupHandle = group.getRaw()) == IntPtr.Zero)
                throw new InvalidOperationException("Event channel group is not ready.");
            if (!groups.Add(groupHandle)) { Remove(key); return; }
        }

        float? capturedOriginal = null;
        bool carriedPause = false;
        if (Owners.TryGetValue(key, out Owner existing)) {
            if (existing.Retiring) throw new InvalidOperationException("Waiting for audio transform cleanup.");
            Require(instance.getPitch(out float observed, out _), "verify owned event pitch");
            if (existing.Plan == plan && existing.GroupHandle == groupHandle) {
                if (Math.Abs(observed - existing.AppliedPitch) < .00001f) return;
                Remove(key);
                YieldedOwners[key] = plan;
                throw new InvalidOperationException(OwnershipConflict);
            }
            capturedOriginal = Math.Abs(observed - existing.AppliedPitch) < .00001f ? existing.OriginalPitch : observed;
            if (!float.IsFinite(capturedOriginal.Value) || capturedOriginal.Value <= 0)
                throw new InvalidOperationException("Invalid original event pitch.");
            if (!Remove(key, resume: false)) throw new InvalidOperationException("Waiting for audio transform cleanup.");
            carriedPause = existing.PauseOwned;
        }
        if (Owners.Count >= 3) throw new InvalidOperationException("Audio transform cleanup capacity reached.");
        float original;
        if (capturedOriginal.HasValue) original = capturedOriginal.Value;
        else Require(instance.getPitch(out original, out _), "read original event pitch");
        if (!float.IsFinite(original) || original <= 0) throw new InvalidOperationException("Invalid original event pitch.");
        var owner = new Owner { Event = instance, System = Audio.System, PauseOwned = carriedPause, Group = group, GroupHandle = groupHandle,
            OriginalPitch = original, AppliedPitch = original * plan.Speed, Plan = plan };
        Owners.Add(key, owner);
        try {
            PauseForGraphChange(owner);
            if (stages.Length > 0) {
                Require(Audio.System.getLowLevelSystem(out FMOD.System core), "get audio system");
                foreach (float factor in stages) {
                    RESULT created = core.createDSPByType(DSP_TYPE.PITCHSHIFT, out DSP dsp);
                    if (dsp != null && dsp.getRaw() != IntPtr.Zero) owner.Dsps.Add(dsp);
                    Require(created, "create pitch compensation");
                    Require(dsp.setParameterFloat((int)DSP_PITCHSHIFT.PITCH, factor), "set pitch compensation");
                    Require(dsp.setParameterFloat((int)DSP_PITCHSHIFT.FFTSIZE, 4096f), "set pitch analysis window");
                }
                // FMOD pitch shifters can accumulate DC. A final high-pass
                // removes it without transposing intermediate filter cutoffs.
                RESULT filterCreated = core.createDSPByType(DSP_TYPE.HIGHPASS, out DSP dcFilter);
                if (dcFilter != null && dcFilter.getRaw() != IntPtr.Zero) owner.Dsps.Add(dcFilter);
                Require(filterCreated, "create pitch DC filter");
                Require(dcFilter.setParameterFloat((int)DSP_HIGHPASS.CUTOFF, 10f), "set pitch DC cutoff");
                // Downward spectral compression accumulates energy in fewer
                // bins. Normalize its gain before returning to the event mix.
                Require(dcFilter.setWetDryMix(1f, plan.CompensationGain, 0f), "normalize pitch compensation gain");
                Require(core.lockDSP(), "lock audio graph");
                try {
                    foreach (DSP dsp in owner.Dsps) {
                        Require(group.addDSP(CHANNELCONTROL_DSP_INDEX.HEAD, dsp), "attach pitch compensation");
                        owner.TrackAttached(dsp);
                    }
                } finally {
                    Require(core.unlockDSP(), "unlock audio graph");
                }
            }
            Require(instance.setPitch(owner.AppliedPitch), "set event playback speed");
            Require(owner.System.flushCommands(), "commit event playback speed");
            ResumeOwnedPause(owner);
        } catch {
            Remove(key);
            throw;
        }
    }

    internal static void Reset() {
        foreach (IntPtr key in Owners.Keys.ToArray()) Remove(key);
        YieldedOwners.Clear();
        LastError = Owners.Count == 0 ? string.Empty : "Waiting for audio transform cleanup.";
    }

    private static bool Remove(IntPtr key, bool resume = true) {
        if (!Owners.TryGetValue(key, out Owner owner)) return true;
        owner.Retiring = true;
        if (!Dispose(owner, resume)) return false;
        Owners.Remove(key);
        return true;
    }

    private static void PauseForGraphChange(Owner owner) {
        if (!owner.Event.isValid()) return;
        Require(owner.Event.getPaused(out bool paused), "read event pause state");
        if (!paused) {
            Require(owner.Event.setPaused(true), "pause audio transform transition");
            owner.PauseOwned = true;
        }
        // Studio waits must never run while the Core mixer lock is held.
        Require(owner.System.flushCommands(), "commit audio transition pause");
    }

    private static void ResumeOwnedPause(Owner owner) {
        if (!owner.PauseOwned) return;
        if (!owner.Event.isValid()) { owner.PauseOwned = false; return; }
        Require(owner.Event.setPaused(false), "resume audio transform transition");
        Require(owner.System.flushCommands(), "commit audio transition resume");
        owner.PauseOwned = false;
    }

    private static bool Dispose(Owner owner, bool resume) {
        if (owner.System == null || !owner.System.isValid()) return true;
        try {
            PauseForGraphChange(owner);
            bool valid = owner.Event != null && owner.Event.isValid();
            if (!valid) owner.PitchRestored = true;
            if (!owner.PitchRestored && owner.Event.getPitch(out float current, out _) == RESULT.OK) {
                owner.PitchRestored = Math.Abs(current - owner.AppliedPitch) >= .00001f ||
                    owner.Event.setPitch(owner.OriginalPitch) == RESULT.OK;
            }
            if (owner.Dsps.Count > 0) {
                Require(owner.System.getLowLevelSystem(out FMOD.System core), "get cleanup audio system");
                Require(core.lockDSP(), "lock cleanup audio graph");
                try {
                    foreach (DSP dsp in owner.Dsps.ToArray()) {
                        // If a detach fails, silence this effect rather than leave
                        // compensation active after restoring playback speed.
                        dsp.setBypass(true);
                        if (owner.Attached.Contains(dsp.getRaw())) {
                            RESULT membership = owner.Group.getDSPIndex(dsp, out _);
                            if (membership == RESULT.OK) {
                                RESULT detached = owner.Group.removeDSP(dsp);
                                if (detached != RESULT.OK && detached != RESULT.ERR_INVALID_HANDLE) continue;
                            } else if (membership != RESULT.ERR_INVALID_HANDLE && membership != RESULT.ERR_DSP_NOTFOUND) continue;
                            owner.Attached.Remove(dsp.getRaw());
                        }
                        RESULT released = dsp.release();
                        if (released == RESULT.OK || released == RESULT.ERR_INVALID_HANDLE) owner.Dsps.Remove(dsp);
                    }
                } finally {
                    Require(core.unlockDSP(), "unlock cleanup audio graph");
                }
            }
            Require(owner.System.flushCommands(), "commit restored event playback speed");
            if (resume && owner.PitchRestored && owner.Dsps.Count == 0) ResumeOwnedPause(owner);
            return owner.PitchRestored && owner.Dsps.Count == 0;
        } catch (Exception exception) {
            LastError = "Waiting for audio transform cleanup: " + exception.Message;
            // Keep ownership, including a temporary pause, for the next retry.
            return false;
        }
    }

    private static void Require(RESULT result, string operation) {
        if (result != RESULT.OK) throw new InvalidOperationException(operation + ": " + result);
    }
}

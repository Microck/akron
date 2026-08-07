using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FMOD;
using FMOD.Studio;
using Microsoft.Xna.Framework;

namespace Celeste.Mod.Akron;

internal sealed class AkronPersistentEventInstanceState {
    public string Path { get; set; } = string.Empty;
    public float Volume { get; set; } = 1f;
    public float Pitch { get; set; } = 1f;
    public bool Has3DAttributes { get; set; }
    public float PositionX { get; set; }
    public float PositionY { get; set; }
    public float PositionZ { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public float VelocityZ { get; set; }
    public float ForwardX { get; set; }
    public float ForwardY { get; set; }
    public float ForwardZ { get; set; }
    public float UpX { get; set; }
    public float UpY { get; set; }
    public float UpZ { get; set; }
    public bool HasListenerMask { get; set; }
    public uint ListenerMask { get; set; }
    public Dictionary<string, float> Parameters { get; set; } = new Dictionary<string, float>();
    public int TimelinePosition { get; set; }
    public bool ShouldPlay { get; set; }
    public bool Paused { get; set; }
    public bool ManualClone { get; set; }
}

internal static class AkronEventInstanceUtils {
    private sealed class EventPathState {
        public string Path { get; init; } = string.Empty;
    }

    private sealed class DormantPlaybackState {
        public bool ShouldPlay { get; init; }
        public bool Paused { get; init; }
    }

    private sealed class PersistentEventState {
        public AkronPersistentEventInstanceState State { get; init; }
    }

    private static bool initialized;
    private static readonly ConditionalWeakTable<EventInstance, ConcurrentDictionary<string, float>> CachedParameters = new ConditionalWeakTable<EventInstance, ConcurrentDictionary<string, float>>();
    private static readonly ConditionalWeakTable<EventInstance, object> ManualCloneEventInstances = new ConditionalWeakTable<EventInstance, object>();
    private static readonly ConditionalWeakTable<EventInstance, object> CachedTimelinePositions = new ConditionalWeakTable<EventInstance, object>();
    private static readonly ConditionalWeakTable<EventInstance, DormantPlaybackState> DormantPlaybackStates = new ConditionalWeakTable<EventInstance, DormantPlaybackState>();
    private static readonly ConditionalWeakTable<EventInstance, EventPathState> KnownEventPaths = new ConditionalWeakTable<EventInstance, EventPathState>();
    private static readonly ConditionalWeakTable<EventInstance, PersistentEventState> CapturedCloneStates =
        new ConditionalWeakTable<EventInstance, PersistentEventState>();

    public static void Initialize() {
        if (initialized) {
            return;
        }

        initialized = true;
        On.Celeste.Audio.CreateInstance += OnCreateInstance;
        On.FMOD.Studio.EventInstance.setParameterValue += OnSetParameterValue;
    }

    public static void Reset() {
        if (!initialized) {
            return;
        }

        initialized = false;
        On.Celeste.Audio.CreateInstance -= OnCreateInstance;
        On.FMOD.Studio.EventInstance.setParameterValue -= OnSetParameterValue;
    }

    public static ConcurrentDictionary<string, float> GetSavedParameterValues(this EventInstance eventInstance) {
        return eventInstance == null ? null : CachedParameters.GetOrCreateValue(eventInstance);
    }

    public static EventInstance NeedManualClone(this EventInstance eventInstance) {
        if (eventInstance != null) {
            ManualCloneEventInstances.GetOrCreateValue(eventInstance);
        }

        return eventInstance;
    }

    public static bool IsManualCloneNeeded(EventInstance eventInstance) {
        return eventInstance != null && ManualCloneEventInstances.TryGetValue(eventInstance, out _);
    }

    public static EventInstance Clone(EventInstance eventInstance, bool dormant) {
        string path = GetEventPath(eventInstance);
        if (string.IsNullOrEmpty(path)) {
            return null;
        }
        // Only dormant clones need a frozen Set-frame description. A live clone
        // must report its current FMOD values if a later StartPos captures it.
        AkronPersistentEventInstanceState setFrameState = dormant
            ? CapturePersistentState(eventInstance, path)
            : null;

        EventInstance clone = Audio.CreateInstance(path);
        if (clone == null || !clone.isValid()) {
            return null;
        }
        RememberEventPath(clone, path);

        if (IsManualCloneNeeded(eventInstance)) {
            clone.NeedManualClone();
        }

        if (eventInstance.getVolume(out float volume, out _) == RESULT.OK) {
            clone.setVolume(volume);
        }
        if (eventInstance.getPitch(out float pitch, out _) == RESULT.OK) {
            clone.setPitch(pitch);
        }
        if (eventInstance.get3DAttributes(out FMOD.Studio._3D_ATTRIBUTES attributes) == RESULT.OK) {
            clone.set3DAttributes(attributes);
        }
        if (eventInstance.getListenerMask(out uint listenerMask) == RESULT.OK) {
            clone.setListenerMask(listenerMask);
        }

        ConcurrentDictionary<string, float> parameters = eventInstance.GetSavedParameterValues();
        if (parameters != null) {
            foreach (KeyValuePair<string, float> pair in parameters) {
                clone.setParameterValue(pair.Key, pair.Value);
            }
        }

        int timelinePosition = LoadTimelinePosition(eventInstance);
        if (timelinePosition > 0 && clone.setTimelinePosition(timelinePosition) == RESULT.OK) {
            SaveTimelinePosition(clone, timelinePosition);
        }

        bool shouldPlay;
        bool paused;
        if (DormantPlaybackStates.TryGetValue(eventInstance, out DormantPlaybackState savedPlayback)) {
            shouldPlay = savedPlayback.ShouldPlay;
            paused = savedPlayback.Paused;
        } else {
            shouldPlay = eventInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == RESULT.OK &&
                         playbackState != PLAYBACK_STATE.STOPPED &&
                         playbackState != PLAYBACK_STATE.STOPPING;
            paused = shouldPlay && eventInstance.getPaused(out bool sourcePaused) == RESULT.OK && sourcePaused;
        }

        if (dormant) {
            DormantPlaybackStates.Add(clone, new DormantPlaybackState {
                ShouldPlay = shouldPlay,
                Paused = paused
            });
            if (setFrameState != null) {
                CapturedCloneStates.Add(clone, new PersistentEventState {
                    State = ClonePersistentState(setFrameState)
                });
            }
        } else if (shouldPlay) {
            clone.start();
            if (paused) {
                clone.setPaused(paused);
            }
        }

        return clone;
    }

    public static AkronPersistentEventInstanceState CapturePersistentState(
        EventInstance eventInstance,
        string knownPath = null
    ) {
        if (eventInstance == null) {
            return null;
        }
        if (CapturedCloneStates.TryGetValue(eventInstance, out PersistentEventState captured)) {
            return ClonePersistentState(captured.State);
        }
        string path = GetEventPath(eventInstance);
        if (string.IsNullOrWhiteSpace(path)) {
            path = knownPath;
        }
        if (string.IsNullOrWhiteSpace(path)) {
            return null;
        }
        RememberEventPath(eventInstance, path);

        AkronPersistentEventInstanceState state = new AkronPersistentEventInstanceState {
            Path = path,
            TimelinePosition = LoadTimelinePosition(eventInstance),
            ManualClone = IsManualCloneNeeded(eventInstance)
        };
        if (eventInstance.getVolume(out float volume, out _) == RESULT.OK) {
            state.Volume = volume;
        }
        if (eventInstance.getPitch(out float pitch, out _) == RESULT.OK) {
            state.Pitch = pitch;
        }
        if (eventInstance.getListenerMask(out uint listenerMask) == RESULT.OK) {
            state.HasListenerMask = true;
            state.ListenerMask = listenerMask;
        }
        if (eventInstance.get3DAttributes(out FMOD.Studio._3D_ATTRIBUTES attributes) == RESULT.OK) {
            state.Has3DAttributes = true;
            state.PositionX = attributes.position.x;
            state.PositionY = attributes.position.y;
            state.PositionZ = attributes.position.z;
            state.VelocityX = attributes.velocity.x;
            state.VelocityY = attributes.velocity.y;
            state.VelocityZ = attributes.velocity.z;
            state.ForwardX = attributes.forward.x;
            state.ForwardY = attributes.forward.y;
            state.ForwardZ = attributes.forward.z;
            state.UpX = attributes.up.x;
            state.UpY = attributes.up.y;
            state.UpZ = attributes.up.z;
        }
        ConcurrentDictionary<string, float> parameters = eventInstance.GetSavedParameterValues();
        if (parameters != null) {
            state.Parameters = new Dictionary<string, float>(parameters);
        }

        if (DormantPlaybackStates.TryGetValue(eventInstance, out DormantPlaybackState dormant)) {
            state.ShouldPlay = dormant.ShouldPlay;
            state.Paused = dormant.Paused;
        } else {
            state.ShouldPlay = eventInstance.getPlaybackState(out PLAYBACK_STATE playbackState) == RESULT.OK &&
                               playbackState != PLAYBACK_STATE.STOPPED &&
                               playbackState != PLAYBACK_STATE.STOPPING;
            state.Paused = state.ShouldPlay &&
                           eventInstance.getPaused(out bool paused) == RESULT.OK &&
                           paused;
        }
        return state;
    }

    private static AkronPersistentEventInstanceState ClonePersistentState(AkronPersistentEventInstanceState state) {
        if (state == null) {
            return null;
        }
        return new AkronPersistentEventInstanceState {
            Path = state.Path,
            Volume = state.Volume,
            Pitch = state.Pitch,
            Has3DAttributes = state.Has3DAttributes,
            PositionX = state.PositionX,
            PositionY = state.PositionY,
            PositionZ = state.PositionZ,
            VelocityX = state.VelocityX,
            VelocityY = state.VelocityY,
            VelocityZ = state.VelocityZ,
            ForwardX = state.ForwardX,
            ForwardY = state.ForwardY,
            ForwardZ = state.ForwardZ,
            UpX = state.UpX,
            UpY = state.UpY,
            UpZ = state.UpZ,
            HasListenerMask = state.HasListenerMask,
            ListenerMask = state.ListenerMask,
            Parameters = new Dictionary<string, float>(state.Parameters ?? new Dictionary<string, float>()),
            TimelinePosition = state.TimelinePosition,
            ShouldPlay = state.ShouldPlay,
            Paused = state.Paused,
            ManualClone = state.ManualClone
        };
    }

    public static EventInstance RestorePersistentState(AkronPersistentEventInstanceState state) {
        if (state == null || string.IsNullOrWhiteSpace(state.Path)) {
            return null;
        }

        EventInstance eventInstance = Audio.CreateInstance(state.Path);
        if (eventInstance == null || !eventInstance.isValid()) {
            return null;
        }
        RememberEventPath(eventInstance, state.Path);
        if (state.ManualClone) {
            eventInstance.NeedManualClone();
        }
        eventInstance.setVolume(state.Volume);
        eventInstance.setPitch(state.Pitch);
        if (state.HasListenerMask) {
            eventInstance.setListenerMask(state.ListenerMask);
        }
        if (state.Has3DAttributes) {
            eventInstance.set3DAttributes(new FMOD.Studio._3D_ATTRIBUTES {
                position = new FMOD.VECTOR { x = state.PositionX, y = state.PositionY, z = state.PositionZ },
                velocity = new FMOD.VECTOR { x = state.VelocityX, y = state.VelocityY, z = state.VelocityZ },
                forward = new FMOD.VECTOR { x = state.ForwardX, y = state.ForwardY, z = state.ForwardZ },
                up = new FMOD.VECTOR { x = state.UpX, y = state.UpY, z = state.UpZ }
            });
        }
        foreach (KeyValuePair<string, float> parameter in state.Parameters ?? new Dictionary<string, float>()) {
            eventInstance.setParameterValue(parameter.Key, parameter.Value);
        }
        if (state.TimelinePosition > 0 && eventInstance.setTimelinePosition(state.TimelinePosition) == RESULT.OK) {
            SaveTimelinePosition(eventInstance, state.TimelinePosition);
        }
        DormantPlaybackStates.Add(eventInstance, new DormantPlaybackState {
            ShouldPlay = state.ShouldPlay,
            Paused = state.Paused
        });
        // A cold-created handle can expose FMOD defaults that the saved handle could not
        // read, such as 3D attributes on a stopped one-shot sound. Keep the Set-frame
        // description authoritative while the handle is dormant so reconstruction can
        // verify the persisted state before anything starts or updates it.
        CapturedCloneStates.Add(eventInstance, new PersistentEventState {
            State = ClonePersistentState(state)
        });
        return eventInstance;
    }

    // FMOD can stop returning an event description for a dormant instance.
    // Record the path when Akron creates the clone so disk persistence still
    // has the exact event identity after the live room has been unloaded.
    internal static string GetEventPath(EventInstance eventInstance) {
        if (eventInstance == null) {
            return string.Empty;
        }
        string path = Audio.GetEventName(eventInstance);
        if (!string.IsNullOrWhiteSpace(path)) {
            RememberEventPath(eventInstance, path);
            return path;
        }
        return KnownEventPaths.TryGetValue(eventInstance, out EventPathState known)
            ? known.Path
            : string.Empty;
    }

    internal static string GetOwnerEventPath(object owner, string fieldName) {
        return owner is SoundSource soundSource && fieldName == "instance"
            ? soundSource.EventName ?? string.Empty
            : string.Empty;
    }

    private static void RememberEventPath(EventInstance eventInstance, string path) {
        if (eventInstance == null || string.IsNullOrWhiteSpace(path)) {
            return;
        }
        KnownEventPaths.Remove(eventInstance);
        KnownEventPaths.Add(eventInstance, new EventPathState { Path = path });
    }

    private static EventInstance OnCreateInstance(
        On.Celeste.Audio.orig_CreateInstance orig,
        string path,
        Vector2? position
    ) {
        EventInstance eventInstance = orig(path, position);
        RememberEventPath(eventInstance, path);
        return eventInstance;
    }

    public static void ActivateDormantEventInstances(IEnumerable<EventInstance> eventInstances) {
        if (eventInstances == null) {
            return;
        }

        foreach (EventInstance eventInstance in new HashSet<EventInstance>(eventInstances)) {
            if (eventInstance == null ||
                !DormantPlaybackStates.TryGetValue(eventInstance, out DormantPlaybackState playback)) {
                continue;
            }

            DormantPlaybackStates.Remove(eventInstance);
            // From this point the room owns the live handle again. Future StartPos captures
            // must query its current values instead of reusing the prior Set-frame description.
            CapturedCloneStates.Remove(eventInstance);
            if (!playback.ShouldPlay) {
                continue;
            }

            eventInstance.start();
            if (playback.Paused) {
                eventInstance.setPaused(true);
            }
        }
    }

    public static void ReleaseDormantEventInstances(IEnumerable<EventInstance> eventInstances) {
        if (eventInstances == null) {
            return;
        }

        foreach (EventInstance eventInstance in new HashSet<EventInstance>(eventInstances)) {
            if (eventInstance == null || !DormantPlaybackStates.Remove(eventInstance)) {
                continue;
            }

            CapturedCloneStates.Remove(eventInstance);
            eventInstance.stop(STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
    }

    public static void ReleaseEventInstances(IEnumerable<EventInstance> eventInstances) {
        if (eventInstances == null) {
            return;
        }

        foreach (EventInstance eventInstance in new HashSet<EventInstance>(eventInstances)) {
            if (eventInstance == null) {
                continue;
            }

            CachedParameters.Remove(eventInstance);
            ManualCloneEventInstances.Remove(eventInstance);
            CachedTimelinePositions.Remove(eventInstance);
            DormantPlaybackStates.Remove(eventInstance);
            KnownEventPaths.Remove(eventInstance);
            CapturedCloneStates.Remove(eventInstance);
            if (!eventInstance.isValid()) {
                continue;
            }

            eventInstance.stop(STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
    }

    public static int LoadTimelinePosition(EventInstance eventInstance) {
        if (eventInstance == null) {
            return 0;
        }

        if (CachedTimelinePositions.TryGetValue(eventInstance, out object cached) && cached is int cachedPosition && cachedPosition > 0) {
            return cachedPosition;
        }

        eventInstance.getTimelinePosition(out int position);
        return position;
    }

    public static void SaveTimelinePosition(EventInstance eventInstance, int timelinePosition) {
        if (eventInstance != null) {
            CachedTimelinePositions.Remove(eventInstance);
            CachedTimelinePositions.Add(eventInstance, timelinePosition);
        }
    }

    public static void CopyParametersFrom(this EventInstance eventInstance, ConcurrentDictionary<string, float> parameters) {
        if (eventInstance == null || parameters == null) {
            return;
        }

        ConcurrentDictionary<string, float> existingParameters = new ConcurrentDictionary<string, float>(eventInstance.GetSavedParameterValues());
        foreach (KeyValuePair<string, float> pair in parameters) {
            eventInstance.setParameterValue(pair.Key, pair.Value);
        }

        foreach (KeyValuePair<string, float> pair in existingParameters) {
            if (parameters.ContainsKey(pair.Key)) {
                continue;
            }

            if (eventInstance.getDescription(out EventDescription description) != RESULT.OK) {
                continue;
            }

            if (description.getParameter(pair.Key, out PARAMETER_DESCRIPTION parameterDescription) != RESULT.OK) {
                continue;
            }

            eventInstance.setParameterValue(pair.Key, parameterDescription.defaultvalue);
        }
    }

    private static RESULT OnSetParameterValue(On.FMOD.Studio.EventInstance.orig_setParameterValue orig, EventInstance self, string name, float value) {
        RESULT result = orig(self, name, value);
        if (result == RESULT.OK && !string.IsNullOrEmpty(name) && self != null) {
            self.GetSavedParameterValues()[name] = value;
        }

        return result;
    }
}

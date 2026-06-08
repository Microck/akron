using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FMOD;
using FMOD.Studio;

namespace Celeste.Mod.Akron;

internal static class AkronEventInstanceUtils {
    private static bool initialized;
    private static readonly ConditionalWeakTable<EventInstance, ConcurrentDictionary<string, float>> CachedParameters = new ConditionalWeakTable<EventInstance, ConcurrentDictionary<string, float>>();
    private static readonly ConditionalWeakTable<EventInstance, object> ManualCloneEventInstances = new ConditionalWeakTable<EventInstance, object>();
    private static readonly ConditionalWeakTable<EventInstance, object> CachedTimelinePositions = new ConditionalWeakTable<EventInstance, object>();

    public static void Initialize() {
        if (initialized) {
            return;
        }

        initialized = true;
        On.FMOD.Studio.EventInstance.setParameterValue += OnSetParameterValue;
    }

    public static void Reset() {
        if (!initialized) {
            return;
        }

        initialized = false;
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

    public static EventInstance Clone(EventInstance eventInstance) {
        string path = Audio.GetEventName(eventInstance);
        if (string.IsNullOrEmpty(path)) {
            return null;
        }

        EventInstance clone = Audio.CreateInstance(path);
        if (clone == null) {
            return null;
        }

        if (IsManualCloneNeeded(eventInstance)) {
            clone.NeedManualClone();
        }

        ConcurrentDictionary<string, float> parameters = eventInstance.GetSavedParameterValues();
        if (parameters != null) {
            foreach (KeyValuePair<string, float> pair in parameters) {
                clone.setParameterValue(pair.Key, pair.Value);
            }
        }

        int timelinePosition = LoadTimelinePosition(eventInstance);
        if (timelinePosition > 0) {
            clone.setTimelinePosition(timelinePosition);
            SaveTimelinePosition(clone, timelinePosition);
        }

        return clone;
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

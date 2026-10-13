using System;
using System.Reflection;

namespace Celeste.Mod.Akron;

// Own only properties actually overridden. Tint does not own any of these.
internal sealed class AkronVisualTuningOverrides
{
    private readonly FloatOverride light = new("Alpha");
    private readonly FloatOverride bloomBase = new("Base");
    private readonly FloatOverride bloomStrength = new("Strength");
    private object owner;
    private string room;

    internal void Apply(object level, string roomName, object lighting, object bloom,
        bool lightEnabled, float alpha, bool bloomEnabled, float baseValue, float strength)
    {
        if (level == null)
        {
            Reset();
            return;
        }

        if (!ReferenceEquals(owner, level))
        {
            Reset();
            owner = level;
            room = roomName;
        }
        else if (!string.Equals(room, roomName, StringComparison.Ordinal))
        {
            // The normal active LoadLevel hook releases before native room writes.
            // If a custom path bypasses it, preserve incoming Alpha/Base on reused
            // renderers; native LoadLevel does not reset Bloom.Strength.
            light.ReleaseForRoomChange(lighting);
            bloomBase.ReleaseForRoomChange(bloom);
            bloomStrength.Release();
            room = roomName;
        }

        light.Apply(lighting, lightEnabled, alpha);
        bloomBase.Apply(bloom, bloomEnabled, baseValue);
        bloomStrength.Apply(bloom, bloomEnabled, strength);
    }

    internal void CopyBaselinesToSnapshot(object level, object sourceLighting, object sourceBloom,
        object capturedLighting, object capturedBloom)
    {
        if (!ReferenceEquals(owner, level))
        {
            return;
        }

        // Snapshots carry the underlying renderer values, not transient Akron
        // overrides. Never release or write the live renderer during capture.
        light.CopyBaselineToSnapshot(sourceLighting, capturedLighting);
        bloomBase.CopyBaselineToSnapshot(sourceBloom, capturedBloom);
        bloomStrength.CopyBaselineToSnapshot(sourceBloom, capturedBloom);
    }

    internal void BeforeLevelLoad(object level, object activeScene)
    {
        // Detached snapshot loaders must never release the active scene's state.
        if (ReferenceEquals(owner, level) && ReferenceEquals(level, activeScene))
        {
            Reset();
        }
    }

    internal void Reset()
    {
        light.Release();
        bloomBase.Release();
        bloomStrength.Release();
        owner = null;
        room = null;
    }

    private sealed class FloatOverride
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly string memberName;
        private object target;
        private PropertyInfo property;
        private FieldInfo field;
        private float baseline;
        private float lastWritten;

        internal FloatOverride(string memberName)
        {
            this.memberName = memberName;
        }

        internal void Apply(object nextTarget, bool enabled, float value)
        {
            if (!enabled || !ReferenceEquals(target, nextTarget))
            {
                Release();
            }
            if (!enabled || nextTarget == null)
            {
                return;
            }
            if (target == null && !Acquire(nextTarget))
            {
                return;
            }

            Write(value);
            lastWritten = value;
        }

        private bool Acquire(object nextTarget)
        {
            Type type = nextTarget.GetType();
            PropertyInfo candidateProperty = type.GetProperty(memberName, Flags);
            FieldInfo candidateField = type.GetField(memberName, Flags);
            if (candidateProperty?.PropertyType == typeof(float) && candidateProperty.CanRead &&
                candidateProperty.CanWrite && candidateProperty.GetIndexParameters().Length == 0)
            {
                property = candidateProperty;
            }
            else if (candidateField?.FieldType == typeof(float) && !candidateField.IsInitOnly)
            {
                field = candidateField;
            }
            else
            {
                return false;
            }

            target = nextTarget;
            baseline = Read();
            lastWritten = baseline;
            return true;
        }

        internal void CopyBaselineToSnapshot(object source, object captured)
        {
            if (target == null || !ReferenceEquals(target, source) || captured == null ||
                ReferenceEquals(source, captured) || !Read().Equals(lastWritten))
            {
                return;
            }

            var snapshot = new FloatOverride(memberName);
            if (snapshot.Acquire(captured) && snapshot.Read().Equals(lastWritten))
            {
                snapshot.Write(baseline);
            }
            // This temporary accessor never owns an applied override, so do not
            // Release it (which would undo the baseline copied into the snapshot).
            snapshot.Clear();
        }

        internal void ReleaseForRoomChange(object incomingRenderer)
        {
            if (ReferenceEquals(target, incomingRenderer))
            {
                Clear();
            }
            else
            {
                Release();
            }
        }

        internal void Release()
        {
            if (target != null)
            {
                // Preserve a later different engine/provider value. Equality is
                // a conservative coexistence guard, not proof of writer identity.
                if (Read().Equals(lastWritten))
                {
                    Write(baseline);
                }
                Clear();
            }
        }

        private float Read() => (float)(property != null ? property.GetValue(target) : field.GetValue(target));

        private void Write(float value)
        {
            if (property != null)
            {
                property.SetValue(target, value);
            }
            else
            {
                field.SetValue(target, value);
            }
        }

        private void Clear()
        {
            target = null;
            property = null;
            field = null;
        }
    }
}

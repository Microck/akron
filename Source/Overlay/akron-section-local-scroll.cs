using System;

namespace Celeste.Mod.Akron;

// Retain user intent separately from ImGui's geometry-clamped scroll value.
internal sealed class AkronSectionLocalScroll
{
    internal float Desired { get; private set; }
    private float observed;
    private float lastHeight;
    private float lastMaximum;
    private bool initialized;
    private bool pending;

    internal float? Prepare(float height, float? reveal, bool deferRestore)
    {
        if (reveal.HasValue)
        {
            Desired = Math.Max(0f, reveal.Value);
            pending = true;
        }
        if (initialized && Math.Abs(height - lastHeight) > 0.01f)
            pending = true;
        return pending && !deferRestore ? Desired : null;
    }

    internal void Observe(float actual, float maximum, float height, float? applied, bool userScrollIntent)
    {
        float expected = Math.Clamp(applied ?? observed, 0f, Math.Max(0f, maximum));
        if (userScrollIntent || !initialized && !applied.HasValue ||
            initialized && !applied.HasValue && Math.Abs(actual - expected) > 0.5f)
            Desired = actual;
        bool maximumChanged = !initialized || Math.Abs(maximum - lastMaximum) > 0.01f;
        float attainable = Math.Clamp(Desired, 0f, Math.Max(0f, maximum));
        pending = !userScrollIntent && (pending || maximumChanged) && Math.Abs(actual - attainable) > 0.5f;
        observed = actual;
        lastHeight = height;
        lastMaximum = maximum;
        initialized = true;
    }
}

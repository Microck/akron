using System;

namespace Celeste.Mod.Akron;

// One instance per encoder launch. A new sink cannot reset another sink's deadline.
internal sealed class AkronCaptureCadence {
    private readonly double interval;
    private double nextFrameSeconds;

    internal AkronCaptureCadence(int framerate, double startedAtSeconds) {
        interval = 1d / Math.Max(1, framerate);
        nextFrameSeconds = startedAtSeconds;
    }

    internal bool TrySchedule(double now, bool hasCapturedFrames, out long missedFrames) {
        missedFrames = 0;
        if (now + interval * 0.25d < nextFrameSeconds) return false;
        if (hasCapturedFrames && now > nextFrameSeconds + interval * 1.5d)
            missedFrames = Math.Max(1, (long) Math.Floor((now - nextFrameSeconds) / interval));
        nextFrameSeconds = now + interval;
        return true;
    }
}

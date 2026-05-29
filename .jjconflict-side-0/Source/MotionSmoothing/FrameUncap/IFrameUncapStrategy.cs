using System;

namespace Celeste.Mod.Akron.MotionSmoothing.FrameUncap;

public interface IFrameUncapStrategy
{
    public TimeSpan TargetUpdateElapsedTime { get; protected set; }
    public TimeSpan TargetDrawElapsedTime { get; protected set; }

    public void SetTargetFramerate(double updateFramerate, double drawFramerate);
}
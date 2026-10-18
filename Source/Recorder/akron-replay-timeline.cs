using System;
using System.IO;

namespace Celeste.Mod.Akron;

// Replay owns a wall-time CFR timeline. Missed samples hold the previous image;
// they must never silently shorten event windows or move future imagery backward.
internal sealed class AkronReplayTimeline
{
    private readonly int fps;
    private readonly long frequency;
    private long originTick;
    private long lastTick;
    private byte[] previousFrame;
    internal DateTime OriginUtc { get; private set; }
    internal long FramesWritten { get; private set; }
    internal bool Started => previousFrame != null;

    internal AkronReplayTimeline(int fps, long frequency)
    {
        this.fps = Math.Max(1, fps);
        this.frequency = Math.Max(1, frequency);
    }

    internal long WriteSample(Stream output, byte[] frame, long capturedTick, DateTime capturedUtc)
    {
        if (frame == null || frame.Length == 0) throw new InvalidDataException("Replay frame is empty.");
        if (Started && frame.Length != previousFrame.Length)
            throw new InvalidDataException("Replay frame geometry changed during capture.");
        if (!Started)
        {
            output.Write(frame, 0, frame.Length);
            previousFrame = (byte[])frame.Clone();
            originTick = capturedTick;
            lastTick = capturedTick;
            OriginUtc = capturedUtc;
            FramesWritten = 1;
            return 1;
        }
        double slots = SlotsAt(capturedTick);
        if (Math.Abs((capturedUtc - OriginUtc).TotalSeconds - slots / fps) > 1d)
            throw new IOException("System clock changed during replay capture; restart the replay buffer.");
        long slot = (long)Math.Floor(slots);
        if (slot < FramesWritten) return 0;
        long held = slot - FramesWritten;
        CheckCatchup(held, previousFrame.Length);
        WriteHeld(output, held);
        output.Write(frame, 0, frame.Length);
        FramesWritten++;
        Buffer.BlockCopy(frame, 0, previousFrame, 0, frame.Length);
        return held + 1;
    }

    internal long CompleteThrough(Stream output, long tick)
    {
        if (!Started) return 0;
        long held = Math.Max(0, (long)Math.Ceiling(SlotsAt(tick) - 0.000000001d) - FramesWritten);
        CheckCatchup(held, previousFrame.Length);
        WriteHeld(output, held);
        return held;
    }

    internal DateTime ProjectUtc(long tick) => OriginUtc.AddSeconds(ElapsedSlots(tick) / fps);
    private double ElapsedSlots(long tick)
    {
        long delta = checked(tick - originTick);
        double slots = delta * (double)fps / frequency;
        if (delta < 0 || !double.IsFinite(slots) || slots >= long.MaxValue - 1d)
            throw new InvalidDataException("Replay clock is outside the supported timeline.");
        return slots;
    }
    private double SlotsAt(long tick)
    {
        if (tick < lastTick) throw new InvalidDataException("Replay monotonic clock moved backwards.");
        double slots = ElapsedSlots(tick);
        lastTick = tick;
        return slots;
    }
    private void WriteHeld(Stream output, long count)
    {
        for (long i = 0; i < count; i++)
        {
            output.Write(previousFrame, 0, previousFrame.Length);
            FramesWritten++;
        }
    }
    private static void CheckCatchup(long count, int frameBytes)
    {
        // Bound synchronous catch-up work. Refuse an extreme stall rather than
        // fabricate a shortened, apparently successful replay timeline.
        if (count > 120 || count * (long)frameBytes > 128L * 1024 * 1024)
            throw new IOException("Replay capture stalled beyond the safe catch-up budget; restart the replay buffer.");
    }
}

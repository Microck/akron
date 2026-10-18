using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Celeste.Mod.Akron;

// Open handles and flushed byte watermarks, never a later lookup of a growing file.
internal sealed class AkronReplayAudioLease : IDisposable
{
    internal sealed record Track(FileStream Input, string Name, int Rate, int Channels, long Bytes, DateTime Origin, long StartFrame);
    internal readonly List<Track> Tracks = new();

    internal AkronRecordedAudio CopyWindow(DateTime start, DateTime end)
    {
        var outputs = new List<AkronRecordedAudioTrack>();
        try
        {
            foreach (var group in Tracks.GroupBy(t => (t.Name, t.Rate, t.Channels, t.Origin)))
            {
                var format = group.Key;
                int frameBytes = checked(format.Channels * 2);
                long frames = checked((long)Math.Ceiling(Math.Max(0, (end - start).TotalSeconds) * format.Rate));
                if (frames == 0) continue;
                long first = checked((long)Math.Floor((start - format.Origin).TotalSeconds * format.Rate));
                string path = Path.Combine(Path.GetTempPath(), "akron-replay-snapshot-" + Guid.NewGuid().ToString("N") + ".s16le");
                try
                {
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    WriteZeros(output, checked(frames * frameBytes));
                    foreach (var track in group.OrderBy(t => t.StartFrame))
                    {
                        long begin = Math.Max(first, track.StartFrame);
                        long until = Math.Min(checked(first + frames), checked(track.StartFrame + track.Bytes / frameBytes));
                        if (until <= begin) continue;
                        track.Input.Position = checked((begin - track.StartFrame) * frameBytes);
                        output.Position = checked((begin - first) * frameBytes);
                        byte[] buffer = new byte[81920];
                        long remaining = checked((until - begin) * frameBytes);
                        while (remaining > 0)
                        {
                            int read = track.Input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0) throw new EndOfStreamException("Leased replay PCM ended before its captured watermark.");
                            output.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                    outputs.Add(new(path, format.Name, format.Rate, format.Channels, checked(frames * frameBytes), start, end));
                }
                catch { if (File.Exists(path)) File.Delete(path); throw; }
            }
            return new(outputs);
        }
        catch { foreach (var track in outputs) File.Delete(track.Path); throw; }
    }

    private static void WriteZeros(Stream output, long bytes)
    {
        byte[] zero = new byte[81920];
        while (bytes > 0) { int n = (int)Math.Min(bytes, zero.Length); output.Write(zero, 0, n); bytes -= n; }
    }

    public void Dispose()
    {
        // One failed close must not retain other handles or the export's busy lease.
        foreach (var track in Tracks) { try { track.Input.Dispose(); } catch (Exception) { } }
        Tracks.Clear();
    }
}

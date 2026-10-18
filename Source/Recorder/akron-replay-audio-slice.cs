using System;
using System.Collections.Generic;
using System.IO;

namespace Celeste.Mod.Akron;

internal static class AkronReplayAudioSlice
{
    internal static AkronRecordedAudio Slice(AkronRecordedAudio audio, DateTime start, DateTime end)
    {
        var result = new List<AkronRecordedAudioTrack>();
        try
        {
            foreach (var track in audio.Tracks)
            {
                if (!track.HasSamples || track.StartedUtc == DateTime.MinValue || !File.Exists(track.Path)) continue;
                int bytesPerFrame = checked(Math.Max(1, track.Channels) * 2);
                int rate = Math.Max(1, track.SampleRate);
                long requestedFrames = checked((long)Math.Ceiling(Math.Max(0, (end - start).TotalSeconds) * rate));
                if (requestedFrames == 0) continue;
                long sourceFrames = new FileInfo(track.Path).Length / bytesPerFrame;
                long sourceStart = checked((long)Math.Floor((start - track.StartedUtc).TotalSeconds * rate));
                long leadingFrames = Math.Min(requestedFrames, Math.Max(0, -sourceStart));
                long readStart = Math.Max(0, sourceStart);
                long copyFrames = Math.Min(requestedFrames - leadingFrames, Math.Max(0, sourceFrames - readStart));
                string path = Path.Combine(Path.GetTempPath(), "akron-replay-audio-" + Guid.NewGuid().ToString("N") + ".s16le");
                try
                {
                    using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                    WriteZeros(output, checked(leadingFrames * bytesPerFrame));
                    if (copyFrames > 0)
                    {
                        using var input = new FileStream(track.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        input.Position = checked(readStart * bytesPerFrame);
                        byte[] buffer = new byte[81920];
                        long remaining = checked(copyFrames * bytesPerFrame);
                        while (remaining > 0)
                        {
                            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read == 0) throw new EndOfStreamException("Replay PCM changed during export.");
                            output.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                    WriteZeros(output, checked((requestedFrames - leadingFrames - copyFrames) * bytesPerFrame));
                    result.Add(new AkronRecordedAudioTrack(path, track.DisplayName, rate, track.Channels,
                        checked(requestedFrames * bytesPerFrame), start, end));
                }
                catch { if (File.Exists(path)) File.Delete(path); throw; }
            }
            return new AkronRecordedAudio(result);
        }
        catch
        {
            foreach (var track in result) if (File.Exists(track.Path)) File.Delete(track.Path);
            throw;
        }
    }

    private static void WriteZeros(Stream output, long count)
    {
        byte[] zero = new byte[81920];
        while (count > 0)
        {
            int length = (int)Math.Min(zero.Length, count);
            output.Write(zero, 0, length);
            count -= length;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Celeste.Mod.Akron;

internal sealed record AkronReplaySegment(string Path, double Start, double End);
internal sealed record AkronReplayWindow(IReadOnlyList<AkronReplaySegment> Segments,
    long FirstFrame, long EndFrame, int Framerate, DateTime OriginUtc, bool IsPartial)
{
    internal long FrameCount => EndFrame - FirstFrame;
    internal double Duration => FrameCount / (double)Framerate;
    internal long InputFirstFrame => FirstFrame - AkronReplayWindows.SnapMediaFrame(Segments[0].Start, Framerate);
    internal DateTime StartUtc => OriginUtc.AddSeconds(FirstFrame / (double)Framerate);
    internal DateTime EndUtc => OriginUtc.AddSeconds(EndFrame / (double)Framerate);
}

internal static class AkronReplayWindows
{
    internal static List<AkronReplaySegment> ReadFinalizedSegments(string directory)
    {
        string path = Path.Combine(directory, "segments.csv");
        if (!File.Exists(path)) return new();
        // FFmpeg rewrites the bounded live manifest. Only complete rows are admissible.
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(input);
        string text = reader.ReadToEnd();
        int lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0) return new();
        return ParseSegments(directory, text.Substring(0, lastNewline).Split('\n'));
    }

    internal static List<AkronReplaySegment> ReadSegments(string directory)
    {
        return ParseSegments(directory, File.ReadLines(Path.Combine(directory, "segments.csv")));
    }

    private static List<AkronReplaySegment> ParseSegments(string directory, IEnumerable<string> lines)
    {
        var result = new List<AkronReplaySegment>();
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            string[] fields = SplitCsv(line);
            if (fields.Length != 3 || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double start) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double end) ||
                !double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start)
                throw new InvalidDataException("Replay segment timing metadata is invalid.");
            string name = Path.GetFileName(fields[0]);
            if (name.Length <= 12 || !name.StartsWith("segment-", StringComparison.Ordinal) || !name.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                !name.Substring(8, name.Length - 12).All(char.IsDigit))
                throw new InvalidDataException("Unexpected replay segment filename.");
            string path = Path.Combine(directory, name);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                result.Add(new AkronReplaySegment(path, start, end));
        }
        return result.OrderBy(segment => segment.Start).ToList();
    }

    internal static long RequestedFrame(DateTime endpoint, DateTime origin, int fps) =>
        checked((long)Math.Ceiling((endpoint - origin).TotalSeconds * fps - 0.000001));

    internal static DateTime RequestedEndOnFrameGrid(DateTime endpoint, DateTime origin, int fps) =>
        origin.AddSeconds(RequestedFrame(endpoint, origin, fps) / (double)fps);

    internal static AkronReplayWindow Plan(IReadOnlyList<AkronReplaySegment> segments,
        DateTime originUtc, int fps, long capturedFrames, DateTime requestedStart, DateTime requestedEnd)
    {
        if (segments.Count == 0 || originUtc == DateTime.MinValue || fps <= 0 || capturedFrames <= 0 || requestedEnd <= requestedStart)
            throw new InvalidDataException("Replay buffer has no timed history for this window.");
        segments = segments.Select(segment => new AkronReplaySegment(segment.Path,
            SnapMediaFrame(segment.Start, fps) / (double)fps, SnapMediaFrame(segment.End, fps) / (double)fps)).ToList();
        // Half-open frame grid: each endpoint rounds upward by less than one frame.
        long requestedFirst = RequestedFrame(requestedStart, originUtc, fps);
        long requestedLast = RequestedFrame(requestedEnd, originUtc, fps);
        long first = Math.Max(0, requestedFirst);
        long end = Math.Min(capturedFrames, requestedLast);
        first = Math.Max(first, SnapMediaFrame(segments[0].Start, fps));
        end = Math.Min(end, SnapMediaFrame(segments[^1].End, fps));
        if (end <= first) throw new InvalidDataException("Requested replay history is no longer available.");
        double from = first / (double)fps;
        double until = end / (double)fps;
        var selected = segments.Where(segment => segment.End > from + 0.000001 && segment.Start < until - 0.000001).ToList();
        if (selected.Count == 0) throw new InvalidDataException("Replay window has no finalized media.");
        double cursor = from;
        foreach (var segment in selected)
        {
            if (segment.Start > cursor + 0.000001)
                throw new InvalidDataException("Replay history contains a missing segment; refusing a shortened clip.");
            cursor = Math.Max(cursor, segment.End);
        }
        if (cursor + 0.001 < until) throw new InvalidDataException("Replay window has incomplete media.");
        return new AkronReplayWindow(selected, first, end, fps, originUtc, first > requestedFirst || end < requestedLast);
    }

    internal static long SnapMediaFrame(double seconds, int fps)
    {
        double value = seconds * fps;
        if (fps <= 0 || !double.IsFinite(value) || value < 0 || value >= long.MaxValue - 1d)
            throw new InvalidDataException("Replay media timestamp is outside the frame grid.");
        long frame = (long)Math.Round(value, MidpointRounding.AwayFromZero);
        // Matroska segment timestamps are quantized to a millisecond. Normalize
        // that muxer rounding before applying requested-window frame boundaries.
        if (Math.Abs(seconds - frame / (double)fps) > 0.0011d)
            throw new InvalidDataException("Replay media timing does not match its captured frame rate.");
        return frame;
    }

    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        if (quoted) throw new InvalidDataException("Incomplete replay timing metadata.");
        fields.Add(field.ToString());
        return fields.ToArray();
    }
}

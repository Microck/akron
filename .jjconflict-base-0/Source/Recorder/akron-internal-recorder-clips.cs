using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Celeste;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronInternalRecorder {
    private const string ClipSidecarExtension = ".akrclip";

    public static IEnumerable<AkronRecordingClipInfo> ListClips(AkronModuleSettings settings) {
        string folder = ResolveOutputFolder(settings);
        if (!Directory.Exists(folder)) {
            return Enumerable.Empty<AkronRecordingClipInfo>();
        }

        IEnumerable<string> clips = Directory.EnumerateFiles(folder)
            .Where(path => IsRecordingFile(path))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + ".replay-", StringComparison.Ordinal));

        IEnumerable<AkronRecordingClipInfo> infos = ApplyClipFilter(clips.Select(ReadClipInfo), settings.RecordingClipBrowserFilter);
        return settings.RecordingClipBrowserSort switch {
            AkronRecordingClipSort.Chapter => infos.OrderBy(info => info.Chapter).ThenByDescending(info => info.CreatedUtc),
            AkronRecordingClipSort.Room => infos.OrderBy(info => info.Room).ThenByDescending(info => info.CreatedUtc),
            AkronRecordingClipSort.Death => infos.OrderByDescending(info => info.Kind.Contains("death", StringComparison.OrdinalIgnoreCase)).ThenByDescending(info => info.CreatedUtc),
            AkronRecordingClipSort.Clear => infos.OrderByDescending(info => info.Kind.Contains("clear", StringComparison.OrdinalIgnoreCase)).ThenByDescending(info => info.CreatedUtc),
            AkronRecordingClipSort.Favorite => infos.OrderByDescending(info => info.Favorite).ThenByDescending(info => info.CreatedUtc),
            _ => infos.OrderByDescending(info => info.CreatedUtc)
        };
    }

    private static IEnumerable<AkronRecordingClipInfo> ApplyClipFilter(IEnumerable<AkronRecordingClipInfo> clips, AkronRecordingClipFilter filter) {
        string currentChapter = string.Empty;
        string currentRoom = string.Empty;
        if (filter == AkronRecordingClipFilter.Chapter || filter == AkronRecordingClipFilter.Room) {
            try {
                Level currentLevel = Engine.Scene as Level;
                currentChapter = currentLevel?.Session?.Area.GetSID() ?? string.Empty;
                currentRoom = currentLevel?.Session?.Level ?? string.Empty;
            } catch {
                currentChapter = string.Empty;
                currentRoom = string.Empty;
            }
        }

        return filter switch {
            AkronRecordingClipFilter.Chapter when !string.IsNullOrWhiteSpace(currentChapter) =>
                clips.Where(info => string.Equals(info.Chapter, SanitizeToken(currentChapter), StringComparison.OrdinalIgnoreCase)),
            AkronRecordingClipFilter.Room when !string.IsNullOrWhiteSpace(currentRoom) =>
                clips.Where(info => string.Equals(info.Room, SanitizeToken(currentRoom), StringComparison.OrdinalIgnoreCase)),
            AkronRecordingClipFilter.Death =>
                clips.Where(info => info.Kind.Contains("death", StringComparison.OrdinalIgnoreCase)),
            AkronRecordingClipFilter.Clear =>
                clips.Where(info => info.Kind.Contains("clear", StringComparison.OrdinalIgnoreCase)),
            AkronRecordingClipFilter.Pb =>
                clips.Where(info => info.Kind.Contains("pb", StringComparison.OrdinalIgnoreCase)),
            AkronRecordingClipFilter.Favorite =>
                clips.Where(info => info.Favorite),
            _ => clips
        };
    }

    public static IEnumerable<AkronRecordingClipInfo> SelectCompletionClipsForTesting(AkronModuleSettings settings, string chapter) {
        return SelectCompletionClips(settings, chapter);
    }

    private static IEnumerable<AkronRecordingClipInfo> SelectCompletionClips(AkronModuleSettings settings, Scene scene) {
        Level level = scene as Level;
        string chapter = SanitizeToken(level?.Session?.Area.GetSID() ?? scene?.GetType().Name ?? string.Empty);
        return SelectCompletionClips(settings, chapter);
    }

    private static IEnumerable<AkronRecordingClipInfo> SelectCompletionClips(AkronModuleSettings settings, string chapter) {
        return ListClips(settings)
            .Where(clip => string.IsNullOrWhiteSpace(chapter) || string.Equals(clip.Chapter, chapter, StringComparison.OrdinalIgnoreCase))
            .Where(IsCompletionClip)
            .OrderBy(clip => clip.StartUtc == DateTime.MinValue ? clip.CreatedUtc : clip.StartUtc)
            .ThenBy(clip => clip.CreatedUtc);
    }

    private static bool IsCompletionClip(AkronRecordingClipInfo clip) {
        return string.Equals(clip.Kind, "completion-flag", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clip.Kind, "room-clear", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clip.Kind, "area-clear", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(clip.Kind, "checkpoint-clear", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteClipSidecar(string clipPath, string kind, AkronRecordingContainerFormat format, DateTime startUtc, DateTime endUtc, Scene scene = null) {
        if (string.IsNullOrWhiteSpace(clipPath)) {
            return;
        }

        Level level = scene as Level;
        string sidecar = clipPath + ClipSidecarExtension;
        List<string> lines = new List<string> {
            "kind=" + kind,
            "format=" + AkronModuleSettings.FormatRecordingContainer(format),
            "chapter=" + SanitizeToken(level?.Session?.Area.GetSID() ?? string.Empty),
            "room=" + SanitizeToken(level?.Session?.Level ?? string.Empty),
            "createdUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            "startUtc=" + startUtc.ToString("O", CultureInfo.InvariantCulture),
            "endUtc=" + endUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        try {
            File.WriteAllLines(sidecar, lines);
        } catch (Exception ex) {
            lastError = ex.Message;
        }
    }

    private static AkronRecordingClipInfo ReadClipInfo(string path) {
        FileInfo file = new FileInfo(path);
        Dictionary<string, string> sidecar = ReadSidecar(path + ClipSidecarExtension);
        string fileName = Path.GetFileNameWithoutExtension(path);
        string[] parts = fileName.Split('-');
        string chapter = sidecar.TryGetValue("chapter", out string sidecarChapter) ? sidecarChapter : parts.Length > 0 ? parts[0] : string.Empty;
        string room = sidecar.TryGetValue("room", out string sidecarRoom) ? sidecarRoom : parts.Length > 1 ? parts[1] : string.Empty;
        string kind = sidecar.TryGetValue("kind", out string sidecarKind) ? sidecarKind : "manual";
        DateTime createdUtc = TryReadUtc(sidecar, "createdUtc", file.Exists ? file.CreationTimeUtc : DateTime.MinValue);
        DateTime startUtc = TryReadUtc(sidecar, "startUtc", DateTime.MinValue);
        DateTime endUtc = TryReadUtc(sidecar, "endUtc", DateTime.MinValue);
        bool favorite = sidecar.TryGetValue("favorite", out string favoriteValue) &&
                        string.Equals(favoriteValue, "true", StringComparison.OrdinalIgnoreCase);

        return new AkronRecordingClipInfo(path, fileName, chapter, room, kind, createdUtc, startUtc, endUtc, favorite);
    }

    private static DateTime TryReadUtc(Dictionary<string, string> sidecar, string key, DateTime fallback) {
        if (sidecar.TryGetValue(key, out string value) &&
            DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)) {
            return parsed.Kind == DateTimeKind.Utc ? parsed : parsed.ToUniversalTime();
        }

        return fallback;
    }

    private static Dictionary<string, string> ReadSidecar(string path) {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) {
            return values;
        }

        foreach (string line in File.ReadLines(path)) {
            int equals = line.IndexOf('=');
            if (equals <= 0) {
                continue;
            }

            values[line.Substring(0, equals)] = line.Substring(equals + 1);
        }

        return values;
    }

    private static bool IsRecordingFile(string path) {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".mkv", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".mov", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".webm", StringComparison.OrdinalIgnoreCase);
    }
}

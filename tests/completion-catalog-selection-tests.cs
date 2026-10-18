using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CompletionCatalogSelectionTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "akron-completion-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly DateTime time = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
    public CompletionCatalogSelectionTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, true);

    private string Clip(string name, string kind, string chapter, int offset) {
        string path = Path.Combine(directory, name + ".mkv");
        File.WriteAllText(path, string.Empty);
        File.WriteAllLines(path + ".akrclip", new[] {
            "kind=" + kind, "chapter=" + chapter, "room=0",
            "createdUtc=" + time.AddSeconds(offset).ToString("O"),
            "startUtc=" + time.AddSeconds(offset).ToString("O"),
            "endUtc=" + time.AddSeconds(offset + 5).ToString("O")
        });
        return path;
    }

    [Fact]
    public void CompletionInputsIgnoreEveryBrowserFilterAndSortWithoutChangingPreferences() {
        string later = Clip("later", "completion-flag", "chapter-a", 20);
        string earlier = Clip("earlier", "room-clear", "chapter-a", 0);
        Clip("death", "last-death", "chapter-a", 30);
        Clip("other-chapter", "area-clear", "chapter-b", 10);
        Clip("compilation", "clear-video", "chapter-a", 40);
        Clip(".replay-hidden", "area-clear", "chapter-a", 50);
        var settings = new AkronModuleSettings { RecordingOutputFolder = directory };
        foreach (AkronRecordingClipFilter filter in Enum.GetValues<AkronRecordingClipFilter>()) {
            foreach (AkronRecordingClipSort sort in Enum.GetValues<AkronRecordingClipSort>()) {
                settings.RecordingClipBrowserFilter = filter;
                settings.RecordingClipBrowserSort = sort;
                Assert.Equal(new[] { earlier, later }, AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "chapter-a").Select(clip => clip.Path));
                Assert.Equal(filter, settings.RecordingClipBrowserFilter);
                Assert.Equal(sort, settings.RecordingClipBrowserSort);
            }
        }
    }

    [Fact]
    public void BrowserStillHonorsItsOwnFilterAfterCompilationSelection() {
        string completion = Clip("flag", "completion-flag", "chapter-a", 0);
        string death = Clip("death", "last-death", "chapter-a", 10);
        var settings = new AkronModuleSettings { RecordingOutputFolder = directory, RecordingClipBrowserFilter = AkronRecordingClipFilter.Death };
        Assert.Equal(completion, Assert.Single(AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "chapter-a")).Path);
        Assert.Equal(death, Assert.Single(AkronInternalRecorder.ListClips(settings)).Path);
    }

    [Fact]
    public void MissingCatalogIsEmptyForBrowserAndCompletionSelection() {
        var settings = new AkronModuleSettings { RecordingOutputFolder = Path.Combine(directory, "missing") };
        Assert.Empty(AkronInternalRecorder.ListClips(settings));
        Assert.Empty(AkronInternalRecorder.SelectCompletionClipsForTesting(settings, "chapter-a"));
    }
}

using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class RecorderOutputPathTests {
    private static string Root => Path.Combine(Path.GetTempPath(), "akron-output-path-tests");

    [Theory]
    [InlineData("relative")]
    [InlineData("relative folder")]
    [InlineData(" ./nested/../relative ")]
    public void RelativeOutputResolvesOnceAgainstGameProcessDirectory(string configured) {
        string cwd = Path.Combine(Root, "cwd");
        string folder = AkronInternalRecorder.ResolveAbsoluteOutputFolder(configured, Path.Combine(Root, "game"), cwd);
        Assert.Equal(Path.GetFullPath(configured.Trim(), cwd), folder);
        Assert.True(Path.IsPathFullyQualified(folder));
        string output = Path.Combine(folder, "clip.mkv");
        // Resolving the encoder argument from its own working directory must not prefix it again.
        Assert.Equal(output, Path.GetFullPath(output, folder));
    }

    [Fact]
    public void AbsoluteOutputAndDefaultGameFolderStayCorrect() {
        string game = Path.Combine(Root, "game");
        string cwd = Path.Combine(Root, "different-cwd");
        string absolute = Path.Combine(Root, "absolute folder");
        Assert.Equal(absolute, AkronInternalRecorder.ResolveAbsoluteOutputFolder(absolute, game, cwd));
        foreach (string? configured in new string?[] { null, "", "  " }) {
            Assert.Equal(Path.Combine(game, "Saves", AkronInternalRecorder.DefaultOutputFolderName),
                AkronInternalRecorder.ResolveAbsoluteOutputFolder(configured, game, cwd));
        }
    }

    [Fact]
    public void CustomFolderDoesNotRequireGameDirectory() {
        string cwd = Path.Combine(Root, "cwd");
        Assert.Equal(Path.Combine(cwd, "custom"), AkronInternalRecorder.ResolveAbsoluteOutputFolder("custom", null!, cwd));
    }

    [Fact]
    public void RecorderOutputBuilderUsesAbsoluteResolutionBeforeFfmpegSetup() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder/akron-internal-recorder.cs"));
        Assert.Contains("string.IsNullOrWhiteSpace(settings.RecordingOutputFolder) ? Everest.PathGame : null", source);
        Assert.Contains("ResolveAbsoluteOutputFolder(settings.RecordingOutputFolder, gameFolder, Environment.CurrentDirectory)", source);
        Assert.Contains("string folder = ResolveOutputFolder(AkronModule.Settings);", source);
        Assert.Contains("WorkingDirectory = ResolveFfmpegWorkingDirectory(outputDirectory)", source);
    }
}

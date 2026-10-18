using System;
using System.IO;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PublishedClipContainerTests {
    [Theory]
    [InlineData("remuxed.mp4", AkronRecordingContainerFormat.Mkv, AkronRecordingContainerFormat.Mp4)]
    [InlineData("audio.MP4", AkronRecordingContainerFormat.Mkv, AkronRecordingContainerFormat.Mp4)]
    [InlineData("capture.mkv", AkronRecordingContainerFormat.Mp4, AkronRecordingContainerFormat.Mkv)]
    [InlineData("capture.MOV", AkronRecordingContainerFormat.Mkv, AkronRecordingContainerFormat.Mov)]
    [InlineData("capture.webm", AkronRecordingContainerFormat.Mp4, AkronRecordingContainerFormat.WebM)]
    [InlineData("capture.unknown", AkronRecordingContainerFormat.Mkv, AkronRecordingContainerFormat.Mkv)]
    [InlineData("capture", AkronRecordingContainerFormat.Mov, AkronRecordingContainerFormat.Mov)]
    [InlineData(null, AkronRecordingContainerFormat.Mp4, AkronRecordingContainerFormat.Mp4)]
    public void MetadataFollowsFinalPathInsteadOfMutableConfiguredContainer(string path, AkronRecordingContainerFormat configured, AkronRecordingContainerFormat expected) {
        Assert.Equal(expected, AkronInternalRecorder.PublishedClipContainer(path, configured));
    }

    [Fact]
    public void SidecarUsesFinalPathAfterManualRemuxAndAudioPublication() {
        string root = Path.Combine(AppContext.BaseDirectory, "../../../../Source/Recorder");
        string sidecar = File.ReadAllText(Path.Combine(root, "akron-internal-recorder-clips.cs"));
        Assert.Contains("FormatRecordingContainer(PublishedClipContainer(clipPath, format))", sidecar);
        string stop = File.ReadAllText(Path.Combine(root, "akron-internal-recorder.cs"));
        int sidecarCall = stop.IndexOf("WriteClipSidecar(completedPath, \"manual-recording\"", StringComparison.Ordinal);
        Assert.True(sidecarCall > stop.IndexOf("completedPath = remuxedPath;", StringComparison.Ordinal));
        Assert.True(sidecarCall > stop.IndexOf("completedPath = audioMuxedPath;", StringComparison.Ordinal));
    }
}

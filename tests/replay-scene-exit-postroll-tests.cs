using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ReplaySceneExitPostrollTests {
    [Fact]
    public void InLevelsRetainsOnlyExistingPendingHistoryOutsideLevel() {
        var settings = new AkronModuleSettings {
            RecordingReplayBufferSeconds = 30,
            RecordingReplayAutoStart = AkronRecordingReplayAutoStart.InLevels
        };
        Assert.True(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, false, true, 2));
        Assert.False(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, true, true, 2));
        Assert.False(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, false, false, 2));
        Assert.False(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, false, true, 0));
        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.Off;
        Assert.False(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, false, true, 2));
        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.InLevels;
        settings.RecordingReplayBufferSeconds = 0;
        Assert.False(AkronInternalRecorder.ShouldKeepPendingReplayBuffer(settings, false, true, 2));
    }

    [Fact]
    public void EventAdmissionHonorsOffAndManualArming() {
        var settings = new AkronModuleSettings { RecordingReplayBufferSeconds = 30 };
        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.Off;
        Assert.False(AkronInternalRecorder.ShouldMaintainReplayBufferForTesting(settings, true, false));
        Assert.True(AkronInternalRecorder.ShouldMaintainReplayBufferForTesting(settings, true, true));
        settings.RecordingReplayAutoStart = AkronRecordingReplayAutoStart.InLevels;
        Assert.True(AkronInternalRecorder.ShouldMaintainReplayBufferForTesting(settings, true, false));
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronInternalRecorder).Assembly.Location);
        var queue = module.GetType(typeof(AkronInternalRecorder).FullName).Methods.Single(m => m.Name == "QueueClipSave");
        Assert.Contains(queue.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "ShouldMaintainReplayBuffer");
    }

    [Fact]
    public void RenderStopChecksPendingHistoryAndCompletionUsesOneBoundary() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronInternalRecorder).Assembly.Location);
        var type = module.GetType(typeof(AkronInternalRecorder).FullName);
        var capture = type.Methods.Single(m => m.Name == "CaptureFrame" && m.Parameters.Count == 2);
        var calls = capture.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToList();
        Assert.True(calls.IndexOf("ShouldKeepPendingReplayBuffer") >= 0);
        Assert.True(calls.IndexOf("ShouldKeepPendingReplayBuffer") < calls.IndexOf("StopReplayBuffer"));
        var complete = type.Methods.Single(m => m.Name == "NotifyAreaComplete");
        Assert.Single(complete.Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name == "ReplayNowUtc"));
    }
}

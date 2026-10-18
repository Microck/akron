using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class ReplaySceneExitPostrollTests {
    [Fact]
    public void EncoderRotationRunsOnlyAfterNativeRenderCapture() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronInternalRecorder).Assembly.Location);
        var recorder = module.GetType(typeof(AkronInternalRecorder).FullName);
        foreach (string name in new[] { "Update", "SaveReplayWindow" }) {
            var method = recorder.Methods.Single(m => m.Name == name);
            Assert.DoesNotContain(method.Body.Instructions, i => i.Operand is MethodReference called && called.Name == "SaveReplayWindows");
        }
        var render = module.GetType(typeof(AkronModule).FullName).Methods.Single(m => m.Name == "EngineOnRenderCore");
        var instructions = render.Body.Instructions.ToList();
        int finish = instructions.FindIndex(i => i.Operand is MethodReference called && called.Name == "FinishFrame");
        var captures = instructions.Select((i, index) => (i, index)).Where(p => p.i.Operand is MethodReference called && called.Name == "CaptureFrame").ToList();
        Assert.Equal(2, captures.Count);
        Assert.All(captures, p => Assert.True(p.index < finish));
        Assert.Contains(recorder.Methods.Single(m => m.Name == "FinishFrame").Body.Instructions,
            i => i.Operand is MethodReference called && called.Name == "SaveReplayWindows");
    }

    [Fact]
    public void NoProcessStartupAndLevelBeginPreserveQueuedReplacementEvent() {
        var type = typeof(AkronInternalRecorder);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        Assert.Null(type.GetField("replayProcess", flags)!.GetValue(null));
        var pending = (System.Collections.IList)type.GetField("PendingClipSaves", flags)!.GetValue(null)!;
        object[] saved = pending.Cast<object>().ToArray();
        string[] names = { "roomEntryUtc", "respawnUtc", "roomEntryName" };
        object?[] values = names.Select(n => type.GetField(n, flags)!.GetValue(null)).ToArray();
        try {
            pending.Clear();
            pending.Add(System.Activator.CreateInstance(type.GetNestedType("PendingClipSave", System.Reflection.BindingFlags.NonPublic)!)!);
            AkronInternalRecorder.NotifyLevelBegin(null!);
            Assert.Single(pending.Cast<object>());
            var stop = type.GetMethods(flags).Single(m => m.Name == "StopReplayBuffer" && m.GetParameters().Length == 2);
            stop.Invoke(null, new object?[] { false, null });
            Assert.Single(pending.Cast<object>());
        } finally {
            pending.Clear();
            foreach (object request in saved) pending.Add(request);
            for (int i = 0; i < names.Length; i++) type.GetField(names[i], flags)!.SetValue(null, values[i]);
        }
    }

    [Fact]
    public void EncoderBatchWaitsForEveryAssociatedPostRoll() {
        var now = new System.DateTime(2026, 10, 18, 14, 0, 0, System.DateTimeKind.Utc);
        Assert.False(AkronInternalRecorder.AreAllReplayWindowsDue(System.Array.Empty<System.DateTime>(), now));
        Assert.False(AkronInternalRecorder.AreAllReplayWindowsDue(new[] { now.AddSeconds(-1), now.AddSeconds(2) }, now));
        Assert.False(AkronInternalRecorder.AreAllReplayWindowsDue(new[] { now.AddSeconds(2), now }, now));
        Assert.True(AkronInternalRecorder.AreAllReplayWindowsDue(new[] { now.AddSeconds(-1), now }, now));
        Assert.True(AkronInternalRecorder.AreAllReplayWindowsDue(new[] { now, now.AddSeconds(2) }, now.AddSeconds(2)));
    }
    [Fact]
    public void InLevelsPreservesOnlyExistingPendingHistoryOutsideLevel() {
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
    public void RenderPathConsultsPendingHistoryBeforeStoppingBuffer() {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronInternalRecorder).Assembly.Location);
        MethodDefinition capture = module.GetType(typeof(AkronInternalRecorder).FullName).Methods
            .Single(m => m.Name == "CaptureFrame" && m.Parameters.Count == 2);
        var calls = capture.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToList();
        Assert.True(calls.IndexOf("ShouldKeepPendingReplayBuffer") >= 0);
        Assert.True(calls.IndexOf("ShouldKeepPendingReplayBuffer") < calls.IndexOf("StopReplayBuffer"));
    }
}

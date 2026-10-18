using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[CollectionDefinition("Replay static ownership", DisableParallelization = true)]
public sealed class ReplayStaticOwnershipCollection { }

[Collection("Replay static ownership")]
public sealed class ReplayLifecycleOwnershipTests
{
    private static FieldInfo Field(string name) => typeof(AkronInternalRecorder).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    [Fact]
    public void LevelBeginRetainsAlreadyAcceptedSourceIdentity()
    {
        var pending = (IList)Field("PendingClipSaves").GetValue(null)!;
        object[] saved = pending.Cast<object>().ToArray();
        string[] names = { "replayTimeline", "roomEntryUtc", "respawnUtc", "roomEntryName" };
        object?[] state = names.Select(n => Field(n).GetValue(null)).ToArray();
        try
        {
            pending.Clear();
            Field("replayTimeline").SetValue(null, null);
            var type = typeof(AkronInternalRecorder).GetNestedType("PendingClipSave", BindingFlags.NonPublic)!;
            var identity = new AkronInternalRecorder.RecordingIdentity("old-chapter", "old-room", 2, 3);
            var request = Activator.CreateInstance(type, "area-clear", DateTime.UnixEpoch,
                DateTime.UnixEpoch.AddSeconds(5), DateTime.UnixEpoch.AddSeconds(5), identity)!;
            pending.Add(request);
            AkronInternalRecorder.NotifyLevelBegin(null);
            Assert.Equal(request, pending[0]);
            Assert.Equal(identity, type.GetProperty("Identity")!.GetValue(pending[0]));
        }
        finally
        {
            pending.Clear(); foreach (var entry in saved) pending.Add(entry);
            for (int i = 0; i < names.Length; i++) Field(names[i]).SetValue(null, state[i]);
        }
    }

    [Fact]
    public void CancelInvalidatesPublicationWithoutReleasingBusyOwnershipEarly()
    {
        string[] names = { "replayExportGeneration", "replayExportBusy", "replayExportProcess" };
        object?[] state = names.Select(n => Field(n).GetValue(null)).ToArray();
        try
        {
            Field("replayExportGeneration").SetValue(null, 7);
            Field("replayExportBusy").SetValue(null, 1);
            Field("replayExportProcess").SetValue(null, null);
            AkronInternalRecorder.CancelReplayExport();
            Assert.Equal(8, Field("replayExportGeneration").GetValue(null));
            Assert.Equal(1, Field("replayExportBusy").GetValue(null));
        }
        finally { for (int i = 0; i < names.Length; i++) Field(names[i]).SetValue(null, state[i]); }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

[Collection(AkronSharedStateCollection.Name)]
public sealed class RoomStatsExportDurationTests {
    [Theory]
    [InlineData(long.MinValue, "00:00.000")]
    [InlineData(-1L, "00:00.000")]
    [InlineData(9999L, "00:00.000")]
    [InlineData(10000L, "00:00.001")]
    [InlineData(35999999999L, "59:59.999")]
    [InlineData(36000000000L, "60:00.000")]
    [InlineData(36623456789L, "61:02.345")]
    [InlineData(864000000000L, "1440:00.000")]
    [InlineData(long.MaxValue, "15372286728:05.477")]
    public void ExportFormatterPreservesDurationAndNegativeClamp(long ticks, string expected) {
        var method = typeof(AkronPracticeStats).GetMethod("FormatTicks", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(expected, method.Invoke(null, new object[] { ticks }));
    }

    // The public writer reads EverestModule._SaveData, whose getter has no body
    // in the stripped CI references. Formatter boundaries above remain ordinary tests.
    [NativeEverestFact]
    public void ProductionWriterChangesOnlyDurationPresentation() {
        var instance = typeof(AkronModule).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = instance.GetValue(null);
        var module = (AkronModule)RuntimeHelpers.GetUninitializedObject(typeof(AkronModule));
        var save = new AkronModuleSaveData();
        var record = new AkronRoomStatRecord {
            AreaSid = "Celeste/9-Core", Room = "space", Visits = 3, Deaths = 2, Strawberries = 1,
            LastInGameTime = 72000000000L, BestInGameTime = 36623450000L, LastAliveTime = 36000000000L
        };
        save.RoomStats["Celeste/9-Core|space"] = record;
        save.RoomStats["Other|hidden"] = new AkronRoomStatRecord { AreaSid = "Other", Room = "hidden" };
        string path = Path.Combine(Path.GetTempPath(), "akron-tsv-" + Guid.NewGuid().ToString("N") + ".tsv");
        try {
            typeof(EverestModule).GetField("<_SaveData>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(module, save);
            instance.SetValue(null, module);
            AkronPracticeStats.WriteRoomStatsTsv("Celeste/9-Core", path);
            Assert.Equal(new[] {
                "area\troom\tvisits\tdeaths\tstrawberries\tlast_igt\tbest_igt\tlast_alive",
                "Celeste/9-Core\tspace\t3\t2\t1\t120:00.000\t61:02.345\t60:00.000"
            }, File.ReadAllLines(path));
            Assert.Equal(72000000000L, record.LastInGameTime);
            Assert.Equal(36623450000L, record.BestInGameTime);
            Assert.Equal(36000000000L, record.LastAliveTime);
            Assert.Equal(3, record.Visits);
            Assert.Equal(2, record.Deaths);
            Assert.Equal(1, record.Strawberries);
            Assert.Equal(2, save.RoomStats.Count);
        } finally {
            instance.SetValue(null, previous);
            File.Delete(path);
        }
    }
}

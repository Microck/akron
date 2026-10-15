using System;
using Celeste.Mod.Akron;
using Xunit;

namespace Akron.Tests;

public sealed class SafeModeStatFreezeTests : IDisposable {
    private sealed class FileStats {
        public int TotalDeaths = 10;
        public long TotalJumps { get; set; } = 20;
    }
    private sealed class ModeStats {
        public int Deaths = 3;
        public long BestTime = 100;
        public long BestFullClearTime = 200;
    }

    public SafeModeStatFreezeTests() => Clear();
    public void Dispose() => Clear();
    private static void Clear() => AkronRuntimeOptions.ApplySafeModeStatFreezeState(null!, null!, "", false, false, false);
    private static void Apply(object file, object? mode, bool deaths = true, bool jumps = true,
        bool best = false, string area = "Celeste/0:Normal") =>
        AkronRuntimeOptions.ApplySafeModeStatFreezeState(file, mode!, area, deaths, jumps, best);

    [Fact]
    public void FreezesFileTotalsAndAreaDeathsAfterOrdinaryIncrements() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode);
        file.TotalDeaths++; file.TotalJumps++; mode.Deaths++;
        Apply(file, mode);
        Assert.Equal(10, file.TotalDeaths); Assert.Equal(20, file.TotalJumps); Assert.Equal(3, mode.Deaths);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void GuardsRemainIndependent(bool deaths, bool jumps) {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode, deaths, jumps);
        file.TotalDeaths++; file.TotalJumps++; mode.Deaths++;
        Apply(file, mode, deaths, jumps);
        Assert.Equal(deaths ? 10 : 11, file.TotalDeaths);
        Assert.Equal(jumps ? 20 : 21, file.TotalJumps);
        Assert.Equal(deaths ? 3 : 4, mode.Deaths);
    }

    [Fact]
    public void EnablingSecondGuardCapturesCurrentValue() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode, true, false);
        file.TotalJumps = 25;
        Apply(file, mode, true, true);
        Assert.Equal(25, file.TotalJumps);
        file.TotalJumps++;
        Apply(file, mode);
        Assert.Equal(25, file.TotalJumps);
    }

    [Fact]
    public void ReenablingGuardKeepsChangesMadeWhileDisabled() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode);
        Apply(file, mode, false, true);
        file.TotalDeaths = 15; mode.Deaths = 8;
        Apply(file, mode);
        Assert.Equal(15, file.TotalDeaths); Assert.Equal(8, mode.Deaths);
        file.TotalDeaths++; mode.Deaths++; file.TotalJumps++;
        Apply(file, mode);
        Assert.Equal(15, file.TotalDeaths); Assert.Equal(8, mode.Deaths); Assert.Equal(20, file.TotalJumps);
    }

    [Fact]
    public void ReplacingSaveAtSameSidRecapturesInsteadOfWritingOldTotals() {
        var oldFile = new FileStats(); var mode = new ModeStats();
        Apply(oldFile, mode);
        var newFile = new FileStats { TotalDeaths = 50, TotalJumps = 60 };
        Apply(newFile, mode);
        newFile.TotalDeaths++; newFile.TotalJumps++;
        Apply(newFile, mode);
        Assert.Equal(50, newFile.TotalDeaths); Assert.Equal(60, newFile.TotalJumps);
        Assert.Equal(10, oldFile.TotalDeaths);
    }

    [Fact]
    public void ReplacingAreaStatsAtSameSidRecaptures() {
        var file = new FileStats(); var oldMode = new ModeStats();
        Apply(file, oldMode);
        var newMode = new ModeStats { Deaths = 9 };
        Apply(file, newMode);
        newMode.Deaths++;
        Apply(file, newMode);
        Assert.Equal(9, newMode.Deaths); Assert.Equal(3, oldMode.Deaths);
    }

    [Fact]
    public void MissingAreaStatsDoesNotDisableFileProtection() {
        var file = new FileStats();
        Apply(file, null);
        file.TotalDeaths++; file.TotalJumps++;
        Apply(file, null);
        Assert.Equal(10, file.TotalDeaths); Assert.Equal(20, file.TotalJumps);
    }

    [Fact]
    public void DisablingAllGuardsReleasesSnapshot() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode);
        Apply(file, mode, false, false);
        file.TotalDeaths = 40; file.TotalJumps = 50;
        Apply(file, mode);
        Assert.Equal(40, file.TotalDeaths); Assert.Equal(50, file.TotalJumps);
    }

    [Fact]
    public void BestRunFieldsAreProtectedOnlyWhenEnabled() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode, false, false, true);
        mode.BestTime = 80; mode.BestFullClearTime = 150; file.TotalDeaths++;
        Apply(file, mode, false, false, true);
        Assert.Equal(100, mode.BestTime); Assert.Equal(200, mode.BestFullClearTime); Assert.Equal(11, file.TotalDeaths);
        Apply(file, mode, true, false, false);
        mode.BestTime = 70;
        Apply(file, mode, true, false, true);
        Assert.Equal(70, mode.BestTime);
    }

    [Fact]
    public void ChangingAreaRecapturesCurrentTotals() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode);
        file.TotalDeaths = 30;
        Apply(file, mode, area: "Other:Normal");
        Assert.Equal(30, file.TotalDeaths);
    }

    [Fact]
    public void MissingSaveClearsPreviousSnapshot() {
        var file = new FileStats(); var mode = new ModeStats();
        Apply(file, mode);
        AkronRuntimeOptions.ApplySafeModeStatFreezeState(null!, mode, "Celeste/0:Normal", true, true, false);
        file.TotalJumps = 90;
        Apply(file, mode);
        Assert.Equal(90, file.TotalJumps);
    }
}

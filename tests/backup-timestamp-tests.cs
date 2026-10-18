using System;
using System.Linq;
using Mono.Cecil;
using Xunit;
using YamlDotNet.Serialization;

namespace Celeste.Mod.Akron.Tests;

public sealed class BackupTimestampTests {
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(3155378976000000000L)]
    [InlineData(long.MaxValue)]
    public void InvalidPersistedTicksAreTreatedAsNever(long ticks) {
        Assert.Equal(DateTime.MinValue, AkronBackupActions.ParseBackupUtcTicks(ticks));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(639279000000000000L)]
    [InlineData(3155378975999999999L)]
    public void ValidTicksKeepExactUtcIdentity(long ticks) {
        DateTime value = AkronBackupActions.ParseBackupUtcTicks(ticks);
        Assert.Equal(ticks, value.Ticks);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Theory]
    [InlineData(3155378976000000000L)]
    [InlineData(long.MaxValue)]
    public void ReadingCorruptYamlDoesNotRewritePersistedSettings(long ticks) {
        var settings = new DeserializerBuilder().Build().Deserialize<AkronModuleSettings>(
            "BackupsLastBackupUtcTicks: " + ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(DateTime.MinValue, AkronBackupActions.ParseBackupUtcTicks(settings.BackupsLastBackupUtcTicks));
        Assert.Equal(ticks, settings.BackupsLastBackupUtcTicks);
    }

    [Fact]
    public void ProductionGetterUsesTheGuardBeforeConstructingADate() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronBackupActions).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronBackupActions));
        var getter = type.Methods.Single(m => m.Name == "GetLastBackupUtc");
        Assert.Contains(getter.Body.Instructions, i => i.Operand is MethodReference m && m.Name == "ParseBackupUtcTicks");
        Assert.DoesNotContain(getter.Body.Instructions, i => i.Operand is MethodReference m && m.DeclaringType.FullName == typeof(DateTime).FullName && m.Name == ".ctor");
    }
}

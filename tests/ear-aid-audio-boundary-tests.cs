using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class EarAidAudioBoundaryTests {
    [Fact]
    public void AudioUpdateReconcilesAfterNativeUpdateWithoutLevelDependency() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronEarAid).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronEarAid));
        var method = type.Methods.Single(m => m.Name == "AudioOnUpdate");
        var calls = method.Body.Instructions.Where(i => i.Operand is MethodReference)
            .Select(i => ((MethodReference)i.Operand).Name).ToArray();
        int native = System.Array.IndexOf(calls, "Invoke");
        int reconcile = System.Array.IndexOf(calls, "ReconcileVolumeOverrides");
        Assert.True(native >= 0 && reconcile > native);
        Assert.Single(calls.Where(name => name == "ReconcileVolumeOverrides"));
        Assert.DoesNotContain(method.Body.Instructions, i => i.Operand is TypeReference t && t.Name == "Level");
        var options = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronRuntimeOptions));
        Assert.DoesNotContain(options.Methods.Single(m => m.Name == "Apply").Body.Instructions,
            i => i.Operand is MethodReference call && call.Name == "ReconcileVolumeOverrides");
    }

    [Fact]
    public void ModuleLifecycleInstallsAndRemovesTheIndependentAudioHook() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronEarAid).Assembly.Location);
        var type = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronEarAid));
        Assert.Contains(type.Methods.Single(m => m.Name == "Load").Body.Instructions,
            i => i.Operand is MethodReference call && call.Name == "add_Update");
        Assert.Contains(type.Methods.Single(m => m.Name == "Unload").Body.Instructions,
            i => i.Operand is MethodReference call && call.Name == "remove_Update");
        Assert.Contains(type.Methods.Single(m => m.Name == "Unload").Body.Instructions,
            i => i.Operand is MethodReference call && call.Name == "ReconcileVolumeOverrides");
        var module = assembly.MainModule.Types.Single(t => t.Name == nameof(AkronModule));
        foreach (string name in new[] { "Load", "Unload" }) {
            Assert.Contains(module.Methods.Single(m => m.Name == name).Body.Instructions,
                i => i.Operand is MethodReference call && call.Name == name && call.DeclaringType.Name == nameof(AkronEarAid));
        }
    }
}

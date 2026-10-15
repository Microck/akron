using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DreamStateRefreshTests {
    [Fact]
    public void ToggleRefreshesExistingBlocksThroughBothNativeMethods() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronActions).Assembly.Location);
        var action = assembly.MainModule.GetType(typeof(AkronActions).FullName).Methods
            .Single(method => method.Name == nameof(AkronActions.ToggleDreamState));
        var calls = action.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Contains(calls, m => m.DeclaringType.FullName == "Celeste.DreamBlock" && m.Name == "ActivateNoRoutine");
        Assert.Contains(calls, m => m.DeclaringType.FullName == "Celeste.DreamBlock" && m.Name == "DeactivateNoRoutine");
        Assert.Contains(calls, m => m.Name == "ToArray");
        Assert.DoesNotContain(calls, m => m.DeclaringType.FullName == "Celeste.Level" && m.Name == "Reload");
    }
}

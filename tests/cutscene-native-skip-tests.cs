using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CutsceneNativeSkipTests {
    private static string Body() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
        "../../../../Source/Actions/akron-actions.cs"))
        .Split("public static void SkipCutscene(Level level) {")[1]
        .Split("private static void WarpToRoom")[0];

    [Fact]
    public void NativeRoutineOwnsCallbackAndFlagCleanup() {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var method = assembly.MainModule.GetType(typeof(AkronActions).FullName).Methods.Single(m => m.Name == "SkipCutscene");
        var calls = method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Assert.Single(calls, m => m.DeclaringType.FullName == "Celeste.Level" && m.Name == "SkipCutscene");
        Assert.DoesNotContain(calls, m => m.Name == "Invoke" && m.DeclaringType.FullName.StartsWith("System.Action", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, m => m.Name == "SetValue" && m.DeclaringType.FullName == "System.Reflection.FieldInfo");
        Assert.DoesNotContain("level.SkippingCutscene =", Body());
    }

    [Fact]
    public void InProgressSkipReturnsBeforeSchedulingAnotherRoutine() {
        string body = Body();
        int guard = body.IndexOf("if (level.SkippingCutscene)", StringComparison.Ordinal);
        int returned = body.IndexOf("return;", guard, StringComparison.Ordinal);
        int native = body.IndexOf("level.SkipCutscene();", StringComparison.Ordinal);
        Assert.True(guard >= 0 && returned > guard && native > returned);
        Assert.Contains("Cutscene skip already in progress.", body);
    }

    [Fact]
    public void MissingCallbackAndNoActiveCutsceneRemainGuarded() {
        string body = Body();
        Assert.Contains("if (!level.InCutscene && !level.SkippingCutscene)", body);
        Assert.Contains("onCutsceneSkipValue is not Action<Level> && onCutsceneSkipValue is not Action", body);
        int noCallback = body.IndexOf("This cutscene has no skip to run.", StringComparison.Ordinal);
        Assert.True(noCallback >= 0 && body.IndexOf("return;", noCallback, StringComparison.Ordinal) < body.IndexOf("level.SkipCutscene();", StringComparison.Ordinal));
        Assert.Contains("AkronModule.TryUse(AkronFeatureKind.CutsceneSkip)", body);
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class PublicationGateTests {
    [Theory]
    [InlineData("Celeste.Mod.Akron.AkronActions", "JournalSnapshotPublicationGate", 0)]
    [InlineData("Celeste.Mod.Akron.AkronArchive", "UniqueArchivePublicationGate", 1)]
    public void CompiledGateCoversSelectionAndPublicationAfterStaging(string typeName, string gateName, int outsideMoves) {
        using var assembly = AssemblyDefinition.ReadAssembly(typeof(AkronModule).Assembly.Location);
        var type = assembly.MainModule.GetType(typeName);
        var field = type.Fields.Single(f => f.Name == gateName);
        Assert.True(field.IsStatic && field.IsInitOnly);
        var method = type.Methods.Single(m => m.HasBody && m.Body.Instructions.Any(i => i.OpCode == OpCodes.Ldsfld && i.Operand is FieldReference f && f.Name == gateName));
        var instructions = method.Body.Instructions;
        var enter = instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.Threading.Monitor" && m.Name == "Enter");
        var release = instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.Threading.Monitor" && m.Name == "Exit");
        var handler = method.Body.ExceptionHandlers.Single(h => h.HandlerType == ExceptionHandlerType.Finally && release.Offset >= h.HandlerStart.Offset && (h.HandlerEnd == null || release.Offset < h.HandlerEnd.Offset));
        bool Protected(int offset) => offset >= handler.TryStart.Offset && offset < handler.TryEnd.Offset;
        var enumerate = instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.IO.Directory" && m.Name == "EnumerateFiles");
        Assert.True(Protected(enumerate.Offset));
        var moves = instructions.Where(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.IO.File" && m.Name == "Move").ToArray();
        Assert.Single(moves.Where(i => Protected(i.Offset)));
        Assert.Equal(outsideMoves, moves.Count(i => !Protected(i.Offset)));
        Assert.Contains(instructions, i => i.Offset < enter.Offset && i.Operand is MethodReference m && m.Name == "Dispose");
        if (outsideMoves == 0) {
            var write = instructions.Single(i => i.Operand is MethodReference m && m.Name == "Invoke" && m.DeclaringType.FullName.StartsWith("System.Action`1", StringComparison.Ordinal));
            Assert.True(write.Offset < enter.Offset);
        }
    }

    [Fact]
    public void JournalCallbackRunsOutsideGateAndPublicationFailureReleasesIt() {
        string dir = Path.Combine(Path.GetTempPath(), "akron-gate-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        object gate = typeof(AkronActions).GetField("JournalSnapshotPublicationGate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var stamp = new DateTime(2026, 10, 16, 21, 30, 0, DateTimeKind.Utc);
        string blocked = Path.Combine(dir, "akron-journal-20261016-213000.json");
        try {
            Directory.CreateDirectory(blocked);
            Assert.ThrowsAny<IOException>(() => AkronActions.WriteUniqueJournalSnapshot(dir, writer => {
                Assert.False(Monitor.IsEntered(gate));
                writer.Write("complete");
            }, stamp));
            Assert.False(Monitor.IsEntered(gate));
            Assert.Empty(Directory.GetFiles(dir));
            Directory.Delete(blocked);
            string path = AkronActions.WriteUniqueJournalSnapshot(dir, "retry", stamp);
            Assert.Equal("retry", File.ReadAllText(path));
        } finally { Directory.Delete(dir, true); }
    }
}

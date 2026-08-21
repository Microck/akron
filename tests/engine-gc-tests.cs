using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Celeste;
using Mono.Cecil;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// Celeste forces a blocking GC.Collect plus GC.WaitForPendingFinalizers inside
// Level.Reload, which runs on every death. AkronEngineGarbageCollection rewrites
// that method so the collection is owed rather than run, and pays the debt at the
// next StartPos load. CI's Celeste assembly is reference-only, so these tests
// verify the real target's metadata and drive the real manipulator against a
// test-owned copy of Celeste's adjacent collection pair.
[Collection(AkronSharedStateCollection.Name)]
public sealed class EngineGarbageCollectionTests {
    private const BindingFlags Internals = BindingFlags.Static | BindingFlags.NonPublic;

    private static readonly FieldInfo CollectionOwedField =
        typeof(AkronEngineGarbageCollection).GetField("collectionOwed", Internals)
        ?? throw new InvalidOperationException("AkronEngineGarbageCollection.collectionOwed is unavailable.");

    [Fact]
    public void TheGuardTargetsLevelReloadAndWrapsCelestesCollectionPair() {
        using ILContext target = OpenCelesteMethod("Celeste.Level", nameof(Level.Reload));
        Assert.False(target.Method.IsStatic);
        Assert.Empty(target.Method.Parameters);

        using ILContext context = OpenCollectionPairFixture();
        context.Invoke(AkronEngineGarbageCollection.DeferForcedCollection);

        List<Instruction> instructions = context.Instrs.ToList();
        int collect = IndexOfCall(instructions, nameof(GC.Collect));
        int wait = IndexOfCall(instructions, nameof(GC.WaitForPendingFinalizers));

        // The pair Celeste ships is still there, once, and still adjacent. A
        // second mod that matches the same two instructions - CelesteTAS does -
        // can still find it after Akron has run.
        Assert.Equal(1, CountCalls(instructions, nameof(GC.Collect)));
        Assert.Equal(1, CountCalls(instructions, nameof(GC.WaitForPendingFinalizers)));
        Assert.Equal(collect + 1, wait);

        // The guard sits immediately in front of the pair: call the decision,
        // branch past both calls when it says yes.
        MethodReference guard = (MethodReference) instructions[collect - 2].Operand;
        Assert.Equal(nameof(AkronEngineGarbageCollection.TryDeferForcedCollection), guard.Name);
        // The parameterless overload, called directly rather than through a
        // cached delegate, so the guard costs one static call per death.
        Assert.Empty(guard.Parameters);
        Assert.Equal(OpCodes.Call, instructions[collect - 2].OpCode);
        AssertBranchesIfTrue(instructions[collect - 1], instructions[wait + 1]);
    }

    [Fact]
    public void TwoModsPatchingTheSamePairEachKeepTheirOwnGuard() {
        // Akron leaves the call pair in place precisely so a second manipulator
        // over the same method still matches. Running Akron's twice is the same
        // shape as Akron plus CelesteTAS, and proves the second one still finds
        // its pattern and still branches past the same two calls.
        using ILContext context = OpenCollectionPairFixture();
        context.Invoke(AkronEngineGarbageCollection.DeferForcedCollection);
        context.Invoke(AkronEngineGarbageCollection.DeferForcedCollection);

        List<Instruction> instructions = context.Instrs.ToList();
        int collect = IndexOfCall(instructions, nameof(GC.Collect));

        Assert.Equal(1, CountCalls(instructions, nameof(GC.Collect)));
        Assert.Equal(1, CountCalls(instructions, nameof(GC.WaitForPendingFinalizers)));
        Assert.Equal(2, CountCalls(instructions, nameof(AkronEngineGarbageCollection.TryDeferForcedCollection)));
        Assert.Equal(0, CountCalls(instructions, nameof(AkronEngineGarbageCollection.MarkDeferredCollectionPaid)));

        Instruction afterPair = instructions[collect + 2];
        AssertBranchesIfTrue(instructions[collect - 1], afterPair);
        AssertBranchesIfTrue(instructions[collect - 3], afterPair);
    }

    [Fact]
    public void RetainedFullCollectionsReconcileDebtOnlyAfterTheyRun() {
        string source = ReadSource("Source/Runtime/akron-engine-gc.cs");
        Assert.Contains("instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt", source);

        using ILContext sceneTransition = OpenCollectionPairFixture();
        sceneTransition.Invoke(AkronEngineGarbageCollection.ReconcileSceneTransitionCollection);
        List<Instruction> sceneInstructions = sceneTransition.Instrs.ToList();
        int sceneWait = IndexOfCall(sceneInstructions, nameof(GC.WaitForPendingFinalizers));
        int sceneReconcile = IndexOfCall(
            sceneInstructions,
            nameof(AkronEngineGarbageCollection.MarkDeferredCollectionPaid));
        Assert.Equal(sceneWait + 1, sceneReconcile);

        using ILContext roomTransition = OpenSingleCollectionFixture();
        roomTransition.Invoke(AkronEngineGarbageCollection.ReconcileRoomTransitionCollection);
        List<Instruction> roomInstructions = roomTransition.Instrs.ToList();
        int roomCollect = IndexOfCall(roomInstructions, nameof(GC.Collect));
        int roomReconcile = IndexOfCall(
            roomInstructions,
            nameof(AkronEngineGarbageCollection.MarkDeferredCollectionPaid));
        Assert.Equal(roomCollect + 1, roomReconcile);
    }

    [Fact]
    public void TheSettingDecidesWhetherCelestesCollectionIsTakenOver() {
        // Default on: the whole point is that a player who installs Akron stops
        // paying a quarter second per death without having to find a toggle.
        Assert.True(AkronEngineGarbageCollection.ShouldDeferForcedCollection(new AkronModuleSettings()));
        Assert.False(AkronEngineGarbageCollection.ShouldDeferForcedCollection(
            new AkronModuleSettings { DeferEngineGarbageCollection = false }));
        // No settings at all - Everest has not built them yet, or the module is
        // gone - leaves Celeste's own behaviour alone.
        Assert.False(AkronEngineGarbageCollection.ShouldDeferForcedCollection(null));
    }

    [Fact]
    public void DeferringADeathCollectionNeverCollects() {
        int owedBefore = ClearDebt();
        try {
            long paidBefore = AkronEngineGarbageCollection.PaidCollections;
            AkronModuleSettings settings = new AkronModuleSettings();
            System.Diagnostics.Stopwatch timer = System.Diagnostics.Stopwatch.StartNew();
            for (int index = 0; index < 10000; index++) {
                Assert.True(AkronEngineGarbageCollection.TryDeferForcedCollection(settings));
            }
            timer.Stop();

            // Nothing was collected: the counter that only CollectDeferred moves
            // did not move, and ten thousand blocking collections could not have
            // finished in this budget even on the fastest heap.
            Assert.Equal(paidBefore, AkronEngineGarbageCollection.PaidCollections);
            Assert.True(timer.Elapsed.TotalSeconds < 5,
                "10000 deferrals took " + timer.Elapsed.TotalSeconds + " s");
            Assert.True(AkronEngineGarbageCollection.CollectionOwed);
        } finally {
            RestoreDebt(owedBefore);
        }
    }

    [Fact]
    public void WithNoModuleLoadedNothingIsDeferred() {
        // Headless callers and an unloaded module must get Celeste's behaviour,
        // not a debt nobody will ever pay.
        Assert.Null(AkronModule.Instance);
        int owedBefore = ClearDebt();
        try {
            Assert.False(AkronEngineGarbageCollection.TryDeferForcedCollection());
            Assert.False(AkronEngineGarbageCollection.CollectionOwed);
        } finally {
            RestoreDebt(owedBefore);
        }
    }

    [Fact]
    public void ManyDeathsCoalesceIntoOneCollectionAndThenTheDebtIsGone() {
        int owedBefore = ClearDebt();
        try {
            AkronModuleSettings settings = new AkronModuleSettings();
            for (int index = 0; index < 40; index++) {
                AkronEngineGarbageCollection.TryDeferForcedCollection(settings);
            }

            long paidBefore = AkronEngineGarbageCollection.PaidCollections;
            int gen2Before = GC.CollectionCount(2);

            Assert.True(AkronEngineGarbageCollection.CollectDeferred());
            Assert.Equal(paidBefore + 1, AkronEngineGarbageCollection.PaidCollections);
            // Forty deaths, one real full collection.
            Assert.True(GC.CollectionCount(2) > gen2Before);

            // Nothing owed now, so a second load in a row costs the player nothing.
            Assert.False(AkronEngineGarbageCollection.CollectDeferred());
            Assert.Equal(paidBefore + 1, AkronEngineGarbageCollection.PaidCollections);
            Assert.False(AkronEngineGarbageCollection.CollectionOwed);
        } finally {
            RestoreDebt(owedBefore);
        }
    }

    [Fact]
    public void AnotherFullCollectionPaysTheDebtWithoutASecondCollection() {
        int owedBefore = ClearDebt();
        try {
            Assert.True(AkronEngineGarbageCollection.TryDeferForcedCollection(new AkronModuleSettings()));
            long paidBefore = AkronEngineGarbageCollection.PaidCollections;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            AkronEngineGarbageCollection.MarkDeferredCollectionPaid();

            Assert.False(AkronEngineGarbageCollection.CollectionOwed);
            Assert.Equal(paidBefore + 1, AkronEngineGarbageCollection.PaidCollections);
            Assert.False(AkronEngineGarbageCollection.CollectDeferred());
            Assert.Equal(paidBefore + 1, AkronEngineGarbageCollection.PaidCollections);
        } finally {
            RestoreDebt(owedBefore);
        }
    }

    [Fact]
    public void TheStartPosLoadPathIsWhereTheDebtIsPaid() {
        // The debt is only worth taking on because there is a beat that settles
        // it. That beat is the StartPos restore, and it must not run while the
        // prewarm worker is allocating on another thread.
        //
        // The worker is stopped by the pacing gate for the whole time the player is
        // in control, and a load holds that gate open for as long as it freezes the
        // game thread. So the ordering that matters is that the collection is paid
        // outside the hold: by then the worker has parked at its next buffer fill and
        // the collection is not marking a heap another thread is still growing.
        string source = ReadSource("Source/Actions/akron-startpos-actions.cs");
        int payment = source.IndexOf("AkronEngineGarbageCollection.CollectDeferred();", StringComparison.Ordinal);
        int gateHeldLoad = source.IndexOf("RestoreStartPosUnderPacingGate(level, startPos", StringComparison.Ordinal);
        Assert.True(payment > 0, "the StartPos restore never pays the deferred collection");
        Assert.True(gateHeldLoad > 0, "the load no longer runs under a pacing gate hold");
        Assert.True(payment > gateHeldLoad, "the deferred collection is paid before the gate hold is released");

        // Everything that runs with the gate open lives inside that one method, and
        // nothing speculative is queued in there at all: the prewarm queue is handed
        // over after the collection, by which point the gate has closed again and the
        // worker parks instead of allocating against it.
        string gateHeld = SliceMethod(source, "private static bool RestoreStartPosUnderPacingGate(");
        Assert.Contains("AkronStartPosPersistence.HoldPacingGateOpen();", gateHeld);
        Assert.DoesNotContain("PrewarmOtherStartPosSnapshots", gateHeld);
        Assert.DoesNotContain("AkronEngineGarbageCollection.CollectDeferred();", gateHeld);
        int queued = source.IndexOf("PrewarmOtherStartPosSnapshots(Engine.Scene as Level ?? level", StringComparison.Ordinal);
        Assert.True(queued > payment, "the prewarm queue is filled before the deferred collection is paid");

        // Akron installs the guard and takes it back out with the module.
        string module = ReadSource("Source/Module/AkronModule.cs");
        Assert.Contains("AkronEngineGarbageCollection.Load();", module, StringComparison.Ordinal);
        Assert.Contains("AkronEngineGarbageCollection.Unload();", module, StringComparison.Ordinal);

        string engineGc = ReadSource("Source/Runtime/akron-engine-gc.cs");
        Assert.Contains("IL.Celeste.Level.Reload += DeferForcedCollection;", engineGc, StringComparison.Ordinal);
        Assert.Contains("IL.Celeste.Level.Reload -= DeferForcedCollection;", engineGc, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.CollectionCount(2)", engineGc, StringComparison.Ordinal);

        // Unloading the module is the last chance to keep the promise a deferral
        // makes, so it settles the debt instead of discarding it.
        int unload = engineGc.IndexOf("internal static void Unload()", StringComparison.Ordinal);
        int settled = engineGc.IndexOf("CollectDeferred();", unload, StringComparison.Ordinal);
        int nextMember = engineGc.IndexOf("    // Level.Reload contains", unload, StringComparison.Ordinal);
        Assert.True(unload > 0);
        Assert.True(settled > unload && settled < nextMember, "Unload does not settle the deferred collection");
    }

    // MonoMod shortens the branch when it finishes a manipulation, so the guard
    // arrives as brtrue or brtrue.s depending on how far it has to jump.
    private static void AssertBranchesIfTrue(Instruction branch, Instruction target) {
        Assert.True(branch.OpCode == OpCodes.Brtrue || branch.OpCode == OpCodes.Brtrue_S,
            "expected a branch-if-true guard, got " + branch.OpCode);
        Assert.Same(target, branch.Operand);
    }

    private static ILContext OpenCelesteMethod(string typeName, string methodName) {
        // The Celeste.dll the tests load is the one the mod is compiled against,
        // so the IL under test is the IL that ships.
        return OpenMethod(
            typeof(Level).Assembly.Location,
            module => module.GetType(typeName).Methods
                .Single(candidate => candidate.Name == methodName && candidate.Parameters.Count == 0));
    }

    private static ILContext OpenCollectionPairFixture() {
        return OpenFixture(nameof(CollectionPairFixture));
    }

    private static ILContext OpenSingleCollectionFixture() {
        return OpenFixture(nameof(SingleCollectionFixture));
    }

    private static ILContext OpenFixture(string methodName) {
        return OpenMethod(
            typeof(EngineGarbageCollectionTests).Assembly.Location,
            module => {
                MethodDefinition method = module.GetType(typeof(EngineGarbageCollectionTests).FullName).Methods
                    .Single(candidate => candidate.Name == methodName);
                // Debug builds put sequence-point nops between source statements.
                // Celeste's target release IL has the calls adjacent, so normalize
                // the test-owned fixture to the shape the manipulator receives.
                ILProcessor processor = method.Body.GetILProcessor();
                foreach (Instruction instruction in method.Body.Instructions
                             .Where(instruction => instruction.OpCode == OpCodes.Nop)
                             .ToArray()) {
                    processor.Remove(instruction);
                }
                return method;
            });
    }

    // ILContext owns its instruction processor but not the Cecil module that owns
    // the method. Tie both lifetimes together so every using declaration above also
    // releases the assembly reader, including when a test fails inside manipulation.
    private static ILContext OpenMethod(
        string assemblyPath,
        Func<ModuleDefinition, MethodDefinition> selectMethod
    ) {
        ModuleDefinition module = ModuleDefinition.ReadModule(assemblyPath);
        try {
            ILContext context = new ILContext(selectMethod(module));
            context.OnDispose += module.Dispose;
            return context;
        } catch {
            module.Dispose();
            throw;
        }
    }

    private static void CollectionPairFixture() {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    private static void SingleCollectionFixture() {
        GC.Collect();
    }

    private static int IndexOfCall(List<Instruction> instructions, string name) {
        int index = instructions.FindIndex(instruction =>
            instruction.Operand is MethodReference method && method.Name == name);
        Assert.True(index >= 0, "no call to " + name + " in the rewritten body");
        return index;
    }

    private static int CountCalls(List<Instruction> instructions, string name) {
        return instructions.Count(instruction =>
            instruction.Operand is MethodReference method && method.Name == name);
    }

    private static int ClearDebt() {
        int previous = (int) (CollectionOwedField.GetValue(null) ?? 0);
        CollectionOwedField.SetValue(null, 0);
        return previous;
    }

    private static void RestoreDebt(int previous) {
        CollectionOwedField.SetValue(null, previous);
    }

    private static string ReadSource(string relativePath) {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../..", relativePath));
    }

    // From a method signature to the start of the next method at the same
    // indentation, which is what makes "this line is not in that method" assertable.
    private static string SliceMethod(string source, string signature) {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, "method not found: " + signature);
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start, "method end not found: " + signature);
        return source.Substring(start, end - start);
    }
}

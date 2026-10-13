using System;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DiagnosticPathAllocationTests
{
    [Fact]
    public void CachedParentPathAddsNoTraversalAllocation()
    {
        var parent = new AkronReconstructionNode { Path = "root" };
        const int count = 1024;
        var nodes = new AkronReconstructionNode[count];
        var direct = new string[count];
        var actual = new string[count];
        for (int i = 0; i < count; i++)
        {
            nodes[i] = new AkronReconstructionNode { ParentKind = "field", ParentFieldName = "child" };
            nodes[i].SetLazyDiagnosticPath(parent, 10);
        }
        // Warm both paths before measuring only their managed allocations.
        for (int i = 0; i < count; i++)
        {
            _ = AkronReconstructionGraph.MaterializeDiagnosticPath(nodes[i]);
            _ = nodes[i].Path;
            nodes[i].SetLazyDiagnosticPath(parent, 10);
        }
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
            direct[i] = AkronReconstructionGraph.MaterializeDiagnosticPath(nodes[i]);
        long directBytes = GC.GetAllocatedBytesForCurrentThread() - start;
        start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++)
            actual[i] = nodes[i].Path;
        long actualBytes = GC.GetAllocatedBytesForCurrentThread() - start;

        Assert.Equal(direct, actual);
        Assert.All(actual, value => Assert.Equal("root.child", value));
        Assert.True(actualBytes <= directBytes,
            $"Cached-parent traversal allocated {actualBytes} bytes versus direct materialization {directBytes}.");
    }
}

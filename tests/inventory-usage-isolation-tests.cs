using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class InventoryUsageIsolationTests
{
    [Theory]
    [InlineData("ApplySetInventory")]
    [InlineData("RestoreSetInventoryOnDeath")]
    [InlineData("CaptureSetInventorySnapshot")]
    public void InventoryOperationsNeverReadOrWriteNativeUsage(string methodName)
    {
        // Inspect compiled code without executing reference-only Celeste methods in CI.
        // Player-visible counter behavior and death lifecycle require native QA.
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronActions).Assembly.Location);
        MethodDefinition method = module.GetType(typeof(AkronActions).FullName).Methods.Single(m => m.Name == methodName);
        Assert.DoesNotContain(method.Body.Instructions, instruction =>
            instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Celeste.Session" && field.Name == "Dashes");
        Assert.DoesNotContain(method.Body.Instructions, instruction =>
            instruction.Operand is MethodReference call &&
            ((call.DeclaringType.FullName == "Celeste.Session" && (call.Name == "get_Dashes" || call.Name == "set_Dashes")) ||
             (call.DeclaringType.FullName == typeof(AkronSetInventorySnapshot).FullName &&
              (call.Name == "get_SessionDashes" || call.Name == "set_SessionDashes"))));
    }

    [Theory]
    [InlineData("ApplySetInventory")]
    [InlineData("RestoreSetInventoryOnDeath")]
    public void InventoryOperationsStillWriteCapacityAndPlayerCharges(string methodName)
    {
        using ModuleDefinition module = ModuleDefinition.ReadModule(typeof(AkronActions).Assembly.Location);
        MethodDefinition method = module.GetType(typeof(AkronActions).FullName).Methods.Single(m => m.Name == methodName);
        Assert.Contains(method.Body.Instructions, instruction => instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Celeste.PlayerInventory" && field.Name == "Dashes");
        Assert.Contains(method.Body.Instructions, instruction => instruction.OpCode == OpCodes.Stfld &&
            instruction.Operand is FieldReference field && field.DeclaringType.FullName == "Celeste.Player" && field.Name == "Dashes");
    }
}

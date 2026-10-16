using System.Reflection;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class DashDirectionPresetTests {
    private static bool[][] Build(string preset) => (bool[][]) typeof(AkronExtendedVariants)
        .GetMethod("BuildDashDirectionPreset", BindingFlags.NonPublic | BindingFlags.Static)!
        .Invoke(null, new object[] { preset })!;

    [Theory]
    [InlineData("all", "111101111")]
    [InlineData("cardinal", "010101010")]
    [InlineData("diagonal", "101000101")]
    [InlineData("no-up", "000101111")]
    [InlineData("no-down", "111101000")]
    [InlineData("horizontal", "000101000")]
    [InlineData("vertical", "010000010")]
    [InlineData("none", "000000000")]
    public void PresetsMatchProviderRowThenColumnConvention(string preset, string expected) {
        bool[][] matrix = Build(preset);
        // Provider reads [Sign(dash.Y)+1][Sign(dash.X)+1].
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++)
                Assert.Equal(expected[(y + 1) * 3 + x + 1] == '1', matrix[y + 1][x + 1]);
    }

    [Theory]
    [InlineData("matrix:010000000")]
    [InlineData("matrix:000001000")]
    [InlineData("matrix:100000010")]
    public void AsymmetricCustomMatrixRoundTripKeepsRowMajorOrder(string serialized) {
        bool[][] matrix = Build(serialized);
        string actual = (string) typeof(AkronExtendedVariants)
            .GetMethod("SerializeValue", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { matrix })!;
        Assert.Equal(serialized, actual);
    }
}

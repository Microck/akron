using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ShiftedDigitInputTests {
    [Theory]
    [InlineData(Keys.D0, ')')]
    [InlineData(Keys.D1, '!')]
    [InlineData(Keys.D2, '@')]
    [InlineData(Keys.D3, '#')]
    [InlineData(Keys.D4, '$')]
    [InlineData(Keys.D5, '%')]
    [InlineData(Keys.D6, '^')]
    [InlineData(Keys.D7, '&')]
    [InlineData(Keys.D8, '*')]
    [InlineData(Keys.D9, '(')]
    public void NumberRowHonorsShiftAndPreservesUnshiftedDigits(Keys key, char expected) {
        Assert.True(AkronImGuiRenderer.TryGetInputCharacter(key, true, out char actual));
        Assert.Equal(expected, actual);
        Assert.True(AkronImGuiRenderer.TryGetInputCharacter(key, false, out char digit));
        Assert.Equal((char)('0' + key - Keys.D0), digit);
    }

    [Fact]
    public void ExistingLetterCaseAndPunctuationArePreserved() {
        Assert.True(AkronImGuiRenderer.TryGetInputCharacter(Keys.A, true, out char upper));
        Assert.Equal('A', upper);
        Assert.True(AkronImGuiRenderer.TryGetInputCharacter(Keys.A, false, out char lower));
        Assert.Equal('a', lower);
        Assert.True(AkronImGuiRenderer.TryGetInputCharacter(Keys.OemMinus, true, out char underscore));
        Assert.Equal('_', underscore);
    }
}

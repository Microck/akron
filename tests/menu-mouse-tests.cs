using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste;
using Monocle;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

// Menu Mouse maps the cursor onto Celeste's own menus and drives them through
// one-frame presses of their VirtualButtons. CI's Celeste assembly is reference-only,
// so these tests drive the plain-value geometry and read VirtualButton's buffer
// counter directly instead of calling Celeste members.
[Collection(AkronSharedStateCollection.Name)]
public sealed class MenuMouseTests {
    private const float LineHeight = 60f;

    private static readonly FieldInfo BufferCounterField =
        typeof(VirtualButton).GetField("bufferCounter", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private class PlainRow : TextMenu.Item {
    }

    // Overrides LeftPressed like TextMenu.Option and Everest's sliders do.
    private sealed class ChoiceRow : PlainRow {
        public override void LeftPressed() {
        }
    }

    [Fact]
    public void RowsStackLikeTheMenuDrawsThem() {
        // Header 100 (not hoverable), A 60, hidden, B 60, spacing 4, top at 426:
        // header [426, 526), A [530, 590), B [594, 654); the hidden row takes no space.
        List<AkronMenuMouse.MenuRow> rows = new List<AkronMenuMouse.MenuRow> {
            new AkronMenuMouse.MenuRow(visible: true, height: 100f, hoverable: false),
            new AkronMenuMouse.MenuRow(visible: true, height: 60f, hoverable: true),
            new AkronMenuMouse.MenuRow(visible: false, height: 0f, hoverable: true),
            new AkronMenuMouse.MenuRow(visible: true, height: 60f, hoverable: true)
        };

        Assert.Equal(-1, Row(rows, 470f));
        Assert.Equal(1, Row(rows, 530f));
        Assert.Equal(-1, Row(rows, 592f));
        Assert.Equal(3, Row(rows, 594f));
        Assert.Equal(3, Row(rows, 653f));
        Assert.Equal(-1, Row(rows, 654f));
        Assert.Equal(-1, Row(rows, 400f));

        Assert.Equal(3, AkronMenuMouse.FindRowAt(rows, 426f, 4f, 600f, out float rowTop));
        Assert.Equal(594f, rowTop);
    }

    [Fact]
    public void DisabledRowsAreNotHit() {
        List<AkronMenuMouse.MenuRow> rows = new List<AkronMenuMouse.MenuRow> {
            new AkronMenuMouse.MenuRow(visible: true, height: 60f, hoverable: false)
        };

        Assert.Equal(-1, AkronMenuMouse.FindRowAt(rows, 510f, 4f, 540f, out _));
    }

    [Theory]
    // A row at y 510 in a menu whose right edge is 1260, with a 200px value column.
    // Zones: 0 = body (confirm), 1 = decrease, 2 = increase.
    [InlineData(800f, 540f, 0)]
    [InlineData(1100f, 540f, 1)]
    [InlineData(1159f, 540f, 1)]
    [InlineData(1160f, 540f, 2)]
    [InlineData(1200f, 540f, 2)]
    // Below the first line: an expanded option submenu's children confirm.
    [InlineData(1200f, 570f, 0)]
    public void ValueColumnHalvesStepAChoiceDownAndUp(float x, float y, int expected) {
        Assert.Equal((AkronMenuMouse.ItemZone) expected, AkronMenuMouse.ResolveValueZone(x, y, 510f, LineHeight, 1260f, 200f, adjustable: true));
    }

    [Fact]
    public void RowsThatCannotStepConfirmAnywhere() {
        // Key binding rows draw their keys in the right column but have no value to step.
        Assert.Equal(AkronMenuMouse.ItemZone.Body, AkronMenuMouse.ResolveValueZone(1200f, 540f, 510f, LineHeight, 1260f, 200f, adjustable: false));
        Assert.Equal(AkronMenuMouse.ItemZone.Body, AkronMenuMouse.ResolveValueZone(1200f, 540f, 510f, LineHeight, 1260f, 0f, adjustable: true));
    }

    [Fact]
    public void OnlyRowsThatOverrideLeftPressedStep() {
        Assert.True(AkronMenuMouse.IsAdjustable((ChoiceRow) RuntimeHelpers.GetUninitializedObject(typeof(ChoiceRow))));
        Assert.False(AkronMenuMouse.IsAdjustable((PlainRow) RuntimeHelpers.GetUninitializedObject(typeof(PlainRow))));
    }

    [Theory]
    // A menu that fits the screen never scrolls.
    [InlineData(540f, 150f, 700f, 0.5f, false, 540f)]
    // A 2000px menu centered on screen moves between 930 - 1000 and 150 + 1000.
    [InlineData(540f, 150f, 2000f, 0.5f, true, 390f)]
    [InlineData(540f, -150f, 2000f, 0.5f, true, 690f)]
    [InlineData(540f, 5000f, 2000f, 0.5f, true, -70f)]
    [InlineData(540f, -5000f, 2000f, 0.5f, true, 1150f)]
    [InlineData(-70f, 150f, 2000f, 0.5f, false, -70f)]
    [InlineData(1150f, -150f, 2000f, 0.5f, false, 1150f)]
    // Top-justified, the bounds cross (930 > 150); like Calc.Clamp, the upper one wins.
    [InlineData(540f, 150f, 2000f, 0f, true, 150f)]
    public void WheelScrollStaysWithinAutoScrollBounds(float currentY, float distance, float menuHeight, float justifyY, bool expectedMoved, float expectedY) {
        bool moved = AkronMenuMouse.TryScrollMenuY(currentY, distance, menuHeight, justifyY, 1080f, out float y);

        Assert.Equal(expectedMoved, moved);
        Assert.Equal(expectedY, y);
    }

    [Fact]
    public void SyntheticPressLastsUntilReleased() {
        VirtualButton button = Button();
        try {
            AkronMenuMouse.Press(button);
            // VirtualButton.Pressed is true while the buffer counter is positive, for
            // every reader in the frame.
            Assert.True(IsPressed(button));

            AkronMenuMouse.ReleaseSyntheticPresses();
            Assert.False(IsPressed(button));
        } finally {
            AkronMenuMouse.ReleaseSyntheticPresses();
        }
    }

    [Fact]
    public void StepWalkerPressesOncePerAcceptedStepUntilTheTarget() {
        WithMenuArrows((left, right) => {
            Oui owner = Owner();

            // Chapter 1 -> 3: the first step goes out at once.
            AkronMenuMouse.StartStep(owner, target: 3, current: 1, ready: true);
            Assert.True(IsPressed(right));
            Assert.False(IsPressed(left));
            AkronMenuMouse.ReleaseSyntheticPresses();

            // Chapter select's input delay is running: wait without pressing.
            Assert.True(AkronMenuMouse.ContinueStep(owner, current: 2, ready: false));
            Assert.False(IsPressed(right));

            Assert.True(AkronMenuMouse.ContinueStep(owner, current: 2, ready: true));
            Assert.True(IsPressed(right));
            AkronMenuMouse.ReleaseSyntheticPresses();

            // Arrived: the walk ends and nothing more is pressed.
            Assert.False(AkronMenuMouse.ContinueStep(owner, current: 3, ready: true));
            Assert.False(IsPressed(right));
            Assert.False(AkronMenuMouse.ContinueStep(owner, current: 3, ready: true));
        });
    }

    [Fact]
    public void StepWalkerStopsWhenAStepIsRefused() {
        WithMenuArrows((left, right) => {
            Oui owner = Owner();

            AkronMenuMouse.StartStep(owner, target: 0, current: 2, ready: true);
            Assert.True(IsPressed(left));
            AkronMenuMouse.ReleaseSyntheticPresses();

            // The selection did not move (a locked or hidden chapter): stop, do not retry.
            Assert.False(AkronMenuMouse.ContinueStep(owner, current: 2, ready: true));
            Assert.False(IsPressed(left));
            Assert.False(AkronMenuMouse.ContinueStep(owner, current: 1, ready: true));
            Assert.False(IsPressed(left));
        });
    }

    [Fact]
    public void StepWalkerIgnoresOtherScreens() {
        WithMenuArrows((left, right) => {
            AkronMenuMouse.StartStep(Owner(), target: 3, current: 1, ready: true);
            AkronMenuMouse.ReleaseSyntheticPresses();

            Assert.False(AkronMenuMouse.ContinueStep(Owner(), current: 2, ready: true));
            Assert.False(IsPressed(right));
        });
    }

    private static int Row(List<AkronMenuMouse.MenuRow> rows, float y) {
        return AkronMenuMouse.FindRowAt(rows, 426f, 4f, y, out _);
    }

    private static Oui Owner() {
        return (Oui) RuntimeHelpers.GetUninitializedObject(typeof(OuiJournal));
    }

    private static VirtualButton Button() {
        return (VirtualButton) RuntimeHelpers.GetUninitializedObject(typeof(VirtualButton));
    }

    private static bool IsPressed(VirtualButton button) {
        return (float) BufferCounterField.GetValue(button)! > 0f;
    }

    private static void WithMenuArrows(System.Action<VirtualButton, VirtualButton> test) {
        VirtualButton previousLeft = Input.MenuLeft;
        VirtualButton previousRight = Input.MenuRight;
        VirtualButton left = Button();
        VirtualButton right = Button();
        Input.MenuLeft = left;
        Input.MenuRight = right;
        try {
            test(left, right);
        } finally {
            AkronMenuMouse.ReleaseSyntheticPresses();
            AkronMenuMouse.ClearStep();
            Input.MenuLeft = previousLeft;
            Input.MenuRight = previousRight;
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class CustomLabelMoveLayoutTests {
    [Theory]
    [InlineData(AkronHudAnchor.TopLeft)]
    [InlineData(AkronHudAnchor.TopCenter)]
    [InlineData(AkronHudAnchor.TopRight)]
    [InlineData(AkronHudAnchor.MiddleLeft)]
    [InlineData(AkronHudAnchor.Center)]
    [InlineData(AkronHudAnchor.MiddleRight)]
    [InlineData(AkronHudAnchor.BottomLeft)]
    [InlineData(AkronHudAnchor.BottomCenter)]
    [InlineData(AkronHudAnchor.BottomRight)]
    public void DestinationReservationsSeparateDifferentSizedLabels(AkronHudAnchor anchor) {
        Type type = typeof(AkronCustomHudLabels).GetNestedType("LabelLayout", BindingFlags.NonPublic)!;
        object layout = Activator.CreateInstance(type, new object[] { 10, 6, 1280f, 720f, 50f })!;
        MethodInfo next = type.GetMethod("NextObstructed")!;
        Vector2 Place(float width, float height) => (Vector2)next.Invoke(layout, new object[] { anchor, new Vector2(width, height) })!;
        Vector2 first = Place(150, 20);
        Vector2 second = Place(230, 35);
        Vector2 third = Place(90, 12);
        bool bottom = anchor == AkronHudAnchor.BottomLeft || anchor == AkronHudAnchor.BottomCenter || anchor == AkronHudAnchor.BottomRight;
        if (bottom) {
            Assert.Equal(6f, first.Y - (second.Y + 35));
            Assert.Equal(6f, second.Y - (third.Y + 12));
        } else {
            Assert.True(second.Y >= first.Y + 20 + 6);
            Assert.True(third.Y >= second.Y + 35 + 6);
        }
    }

    [Theory]
    [InlineData(AkronHudAnchor.MiddleLeft)]
    [InlineData(AkronHudAnchor.Center)]
    [InlineData(AkronHudAnchor.MiddleRight)]
    public void MovedLabelsClearExistingMiddleDestinationOccupants(AkronHudAnchor anchor) {
        Type type = typeof(AkronCustomHudLabels).GetNestedType("LabelLayout", BindingFlags.NonPublic)!;
        object layout = Activator.CreateInstance(type, new object[] { 10, 6, 1280f, 720f, 50f })!;
        Vector2 resident = (Vector2)type.GetMethod("Next")!.Invoke(layout, new object[] { anchor, new Vector2(150, 20) })!;
        Vector2 moved = (Vector2)type.GetMethod("NextObstructed")!.Invoke(layout, new object[] { anchor, new Vector2(230, 35) })!;
        Assert.True(moved.Y >= resident.Y + 20 + 6);
        Vector2 third = (Vector2)type.GetMethod("NextObstructed")!.Invoke(layout, new object[] { anchor, new Vector2(90, 50) })!;
        Assert.True(third.Y >= moved.Y + 35 + 6);
    }

    [Fact]
    public void MoveUsesTheAdvancingDestinationWhileFadeRetainsItsOpacityPath() {
        string source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "../../../../Source/Hud/akron-custom-hud-labels.cs"));
        int start = source.IndexOf("private static void ApplyObstructionBehavior", StringComparison.Ordinal);
        int end = source.IndexOf("private sealed class LabelRenderPlan", start, StringComparison.Ordinal);
        string body = source.Substring(start, end - start);
        Assert.Contains("position = layout.NextObstructed(", body);
        Assert.DoesNotContain("layout.PositionForAnchor(", body);
        Assert.Contains("style = style.WithOpacity(opacity)", body);
        Assert.Contains("!settings.CustomHudLabelObstructionEnabled || !obstructed", body);
    }
}

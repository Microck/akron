using System;
using System.Linq;
using System.Reflection;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class ValueObjectTests {
    [Fact]
    public void DeepCloneReadonlyFieldSetterDoesNotThrowWhenRuntimeRejectsWrite() {
        Type setterType = typeof(AkronModule).Assembly.GetType("Force.DeepCloner.Helpers.DeepClonerExprGenerator")!;
        MethodInfo forceSetField = setterType.GetMethod("ForceSetField", BindingFlags.Static | BindingFlags.NonPublic)!;
        FieldInfo readonlyField = typeof(ReadonlyFieldProbe).GetField(nameof(ReadonlyFieldProbe.Value), BindingFlags.Instance | BindingFlags.Public)!;
        ReadonlyFieldProbe probe = new ReadonlyFieldProbe("original");

        Exception exception = Record.Exception(() => forceSetField.Invoke(null, new object[] { readonlyField, probe, "updated" }));

        Assert.Null(exception);
    }

    [Fact]
    public void RectangleDataRoundTripsThroughXnaRectangle() {
        Rectangle source = new Rectangle(-12, 34, 56, 78);

        AkronRectangleData data = new AkronRectangleData(source);
        Rectangle roundTrip = data.ToRectangle();

        Assert.Equal(source.X, roundTrip.X);
        Assert.Equal(source.Y, roundTrip.Y);
        Assert.Equal(source.Width, roundTrip.Width);
        Assert.Equal(source.Height, roundTrip.Height);
    }

    [Fact]
    public void RenderPassLatchesAttemptStateOnce() {
        AkronModuleSettings settings = new AkronModuleSettings {
            Noclip = true,
            HudCheatIndicator = false,
            RecordingEndscreenDurationSeconds = 1f
        };
        AkronModuleSession session = new AkronModuleSession {
            AttemptStatus = AkronStatus.Cheat,
            AttemptReason = "Noclip was enabled.",
            TimescaleEnabled = true,
            TimescaleMultiplier = 0.5f,
            PauseTrackerPauseCount = 7
        };

        AkronRenderPass record = AkronScreenProjection.CaptureForTesting(
            settings,
            session,
            "Celeste/1-ForsakenCity",
            areaMode: 0,
            frameCounter: 1234);
        uint contributorDigest = record.ContributorDigest;
        uint firstLayoutWord = record.GetLayoutWordForTesting(0);

        settings.Noclip = false;
        settings.HudCheatIndicator = true;
        session.AttemptStatus = AkronStatus.GoldberryHardlistClean;
        session.AttemptReason = "Post-run mutation.";
        session.TimescaleEnabled = false;

        Assert.Equal(AkronStatus.Cheat, record.AttemptStatus);
        Assert.Equal("Noclip was enabled.", record.AttemptReason);
        Assert.Equal(2, record.StatusSlot);
        Assert.Equal(contributorDigest, record.ContributorDigest);
        Assert.Equal(firstLayoutWord, record.GetLayoutWordForTesting(0));
        Assert.Equal(7, session.PauseTrackerPauseCount);
    }

    [Theory]
    [InlineData(AkronStatus.GoldberryHardlistClean, 0)]
    [InlineData(AkronStatus.RegularClean, 1)]
    [InlineData(AkronStatus.Cheat, 2)]
    public void RenderPassUsesDistinctStatusValues(AkronStatus status, int expectedStatusSlot) {
        AkronRenderPass record = AkronScreenProjection.CreateForTesting(
            status,
            Array.Empty<AkronFeatureKind>(),
            "Celeste/1-ForsakenCity",
            areaMode: 0,
            frameCounter: 99,
            displaySeconds: AkronScreenProjection.MinimumDisplaySeconds);

        Assert.Equal(expectedStatusSlot, record.StatusSlot);
        Assert.Equal((uint) expectedStatusSlot, record.GetLayoutWordForTesting(0) & 0x03u);
    }

    [Fact]
    public void RenderPassDataDoesNotMutateSettingsOrSession() {
        AkronModuleSettings settings = new AkronModuleSettings {
            RecordingEndscreenDurationSeconds = 12f,
            HudCheatIndicator = false,
            HudCheatIndicatorOnlyFlagged = true
        };
        AkronModuleSession session = new AkronModuleSession {
            AttemptStatus = AkronStatus.RegularClean,
            AttemptReason = "Room label overlay was enabled.",
            PauseTrackerPausedSeconds = 4.5f,
            UsedBrokeredSavestate = false
        };

        AkronRenderPass record = AkronScreenProjection.CaptureForTesting(
            settings,
            session,
            "Celeste/2-OldSite",
            areaMode: 1,
            frameCounter: 88);

        Assert.Equal(AkronStatus.RegularClean, session.AttemptStatus);
        Assert.Equal("Room label overlay was enabled.", session.AttemptReason);
        Assert.Equal(4.5f, session.PauseTrackerPausedSeconds);
        Assert.False(session.UsedBrokeredSavestate);
        Assert.False(settings.HudCheatIndicator);
        Assert.True(settings.HudCheatIndicatorOnlyFlagged);
        Assert.Equal(12f, record.DisplaySeconds);
    }

    [Fact]
    public void RenderPassHasNoSettingsOrProfileSurface() {
        string[] surfaceNames = typeof(AkronModuleSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Concat(typeof(AkronProfileState).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property.Name)
            .ToArray();

        Assert.DoesNotContain(surfaceNames, name => name.Contains("Presentation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surfaceNames, name => name.Contains("FrameCue", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(surfaceNames, name => name.Contains("Raster", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RenderPassCellsExistWhenVisibleHudIndicatorIsDisabled() {
        AkronModuleSettings settings = new AkronModuleSettings {
            HudCheatIndicator = false,
            HudCheatIndicatorOnlyFlagged = true,
            RecordingEndscreenDurationSeconds = 0f
        };
        AkronModuleSession session = new AkronModuleSession {
            AttemptStatus = AkronStatus.GoldberryHardlistClean
        };

        AkronRenderPass record = AkronScreenProjection.CaptureForTesting(
            settings,
            session,
            "Celeste/7-Summit",
            areaMode: 0,
            frameCounter: 456);
        AkronRenderCell[] cells = AkronScreenProjection
            .BuildRenderCellsForTesting(record, 1920, 1080, frameIndex: 0)
            .ToArray();

        Assert.True(record.DisplaySeconds >= AkronScreenProjection.MinimumDisplaySeconds);
        Assert.NotEmpty(cells);
        Assert.Contains(cells, cell => cell.Layer == 0);
        Assert.Contains(cells, cell => cell.Layer == 1);
        Assert.Contains(cells, cell => cell.Layer == 2);
        Assert.Contains(cells, cell => cell.Increase);
        Assert.Contains(cells, cell => !cell.Increase);
        Assert.All(cells, cell => {
            Assert.InRange(cell.X, 0, 1919);
            Assert.InRange(cell.Y, 0, 1079);
            Assert.True(cell.Width > 0);
            Assert.True(cell.Height > 0);
        });
        AssertBalancedPairs(cells);
    }

    [Fact]
    public void RenderPassBuildsStableLayoutWords() {
        AkronRenderPass record = AkronScreenProjection.CreateForTesting(
            AkronStatus.Cheat,
            new[] { AkronFeatureKind.Timescale, AkronFeatureKind.Noclip },
            "Celeste/1-ForsakenCity",
            areaMode: 0,
            frameCounter: 1234,
            displaySeconds: AkronScreenProjection.MinimumDisplaySeconds);

        uint[] layoutWords = Enumerable.Range(0, record.LayoutWordCount)
            .Select(record.GetLayoutWordForTesting)
            .ToArray();
        uint[] summaryWords = Enumerable.Range(0, record.SummaryWordCount)
            .Select(record.GetSummaryWordForTesting)
            .ToArray();
        AkronRenderCell[] cells = AkronScreenProjection
            .BuildRenderCellsForTesting(record, 1920, 1080, frameIndex: 12)
            .ToArray();

        Assert.Equal(
            new[] {
                0xA6D30102u,
                0xD197F2ADu,
                0xD4D900C4u,
                0xE57F2544u,
                0x000004D2u,
                0xDE6C82BEu,
                0xCAC92368u,
                0x15B8F02Fu
            },
            layoutWords);
        Assert.Equal(
            new[] {
                0x5D7E0102u,
                0x02FE7B82u
            },
            summaryWords);
        Assert.Equal(43008, cells.Length);
        AssertBalancedPairs(cells);
        AssertCell(cells[0], layer: 0, x: 816, y: 593, width: 1, height: 1, increase: false, polarity: 1);
        AssertCell(cells[1], layer: 0, x: 817, y: 593, width: 1, height: 1, increase: true, polarity: 1);
        AssertCell(cells[13824], layer: 1, x: 526, y: 657, width: 1, height: 1, increase: true, polarity: 1);
        AssertCell(cells[18431], layer: 1, x: 423, y: 267, width: 1, height: 1, increase: true, polarity: 1);
        AssertCell(cells[18432], layer: 2, x: 728, y: 604, width: 2, height: 2, increase: true, polarity: -1);
        AssertCell(cells[43007], layer: 2, x: 1374, y: 296, width: 2, height: 2, increase: false, polarity: -1);
    }

    private static void AssertBalancedPairs(AkronRenderCell[] cells) {
        Assert.Equal(0, cells.Length % 2);
        for (int index = 0; index < cells.Length; index += 2) {
            AkronRenderCell first = cells[index];
            AkronRenderCell second = cells[index + 1];
            Assert.Equal(first.Layer, second.Layer);
            Assert.Equal(first.Width, second.Width);
            Assert.Equal(first.Height, second.Height);
            Assert.Equal(first.Polarity, second.Polarity);
            Assert.NotEqual(first.Increase, second.Increase);

            int xDistance = Math.Abs(first.X - second.X);
            int yDistance = Math.Abs(first.Y - second.Y);
            Assert.True(
                (xDistance == first.Width && yDistance == 0) ||
                (xDistance == 0 && yDistance == first.Height),
                "Pair " + index / 2 + " should be adjacent without leaving an unbalanced isolated pixel.");
        }
    }

    private static void AssertCell(
        AkronRenderCell cell,
        int layer,
        int x,
        int y,
        int width,
        int height,
        bool increase,
        int polarity) {
        Assert.Equal(layer, cell.Layer);
        Assert.Equal(x, cell.X);
        Assert.Equal(y, cell.Y);
        Assert.Equal(width, cell.Width);
        Assert.Equal(height, cell.Height);
        Assert.Equal(increase, cell.Increase);
        Assert.Equal(polarity, cell.Polarity);
    }

    private sealed class ReadonlyFieldProbe {
        public readonly string Value;

        public ReadonlyFieldProbe(string value) {
            Value = value;
        }
    }
}

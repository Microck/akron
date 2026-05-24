using System;
using System.Collections.Generic;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Akron.Tests")]

namespace Celeste.Mod.Akron;

internal readonly struct AkronHudRect {
    public readonly float X;
    public readonly float Y;
    public readonly float Width;
    public readonly float Height;

    public AkronHudRect(float x, float y, float width, float height) {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}

internal static partial class AkronScreenProjection {
    private const float GameWidth = 320f;
    private const float GameHeight = 180f;

    public static AkronHudRect WorldToHudRect(Level level, Rectangle worldBounds) {
        Vector2 topLeft = WorldToHud(level, new Vector2(worldBounds.X, worldBounds.Y));
        float scale = CurrentViewportScale();
        return new AkronHudRect(
            topLeft.X,
            topLeft.Y,
            worldBounds.Width * scale,
            worldBounds.Height * scale);
    }

    public static Vector2 WorldToHud(Level level, Vector2 worldPosition) {
        return level.Camera.CameraToScreen(worldPosition) * CurrentViewportScale() + CurrentViewportOffset();
    }

    public static Vector2 MouseScreenToWorld(Level level, Vector2 mouseScreenPosition) {
        Vector2 gamePosition = RemoveLevelZoom(level, MouseScreenToGame(mouseScreenPosition));
        return new Vector2(level.Camera.X + gamePosition.X, level.Camera.Y + gamePosition.Y);
    }

    public static Vector2 MouseScreenToGame(Vector2 mouseScreenPosition) {
        float scale = CurrentViewportScale();
        Vector2 viewportPosition = mouseScreenPosition - CurrentViewportOffset();
        return new Vector2(
            Calc.Clamp(viewportPosition.X / scale, 0f, GameWidth),
            Calc.Clamp(viewportPosition.Y / scale, 0f, GameHeight));
    }

    public static float CurrentViewportScale() {
        Viewport viewport = Engine.Viewport;
        float backBufferWidth = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferWidth ?? GameWidth;
        float backBufferHeight = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferHeight ?? GameHeight;
        float viewportWidth = viewport.Width > 0 ? viewport.Width : backBufferWidth;
        float viewportHeight = viewport.Height > 0 ? viewport.Height : backBufferHeight;
        return MathHelper.Min(viewportWidth / GameWidth, viewportHeight / GameHeight);
    }

    public static Vector2 CurrentViewportOffset() {
        Viewport viewport = Engine.Viewport;
        return new Vector2(viewport.X, viewport.Y);
    }

    private static Vector2 RemoveLevelZoom(Level level, Vector2 screenGamePosition) {
        float zoom = CurrentLevelZoom(level);
        if (System.Math.Abs(zoom - 1f) < 0.001f) {
            return screenGamePosition;
        }

        Vector2 focus = CurrentLevelZoomFocus(level);
        return focus + (screenGamePosition - focus) / zoom;
    }

    private static float CurrentLevelZoom(Level level) {
        if (level == null || level.Zoom <= 0f) {
            return 1f;
        }

        return level.Zoom;
    }

    private static Vector2 CurrentLevelZoomFocus(Level level) {
        if (level == null) {
            return new Vector2(GameWidth / 2f, GameHeight / 2f);
        }

        return level.ZoomFocusPoint;
    }
}

internal sealed class AkronRenderPass {
    public const int CurrentLayoutVersion = 1;

    private readonly uint[] layoutWords;
    private readonly uint[] summaryWords;
    private readonly byte[] shadePattern;
    private readonly byte[] summaryPattern;

    public AkronRenderPass(
        AkronStatus attemptStatus,
        string attemptReason,
        uint contributorDigest,
        uint mapDigest,
        int areaMode,
        uint frameBucket,
        float displaySeconds) {
        LayoutVersion = CurrentLayoutVersion;
        AttemptStatus = attemptStatus;
        AttemptReason = string.IsNullOrWhiteSpace(attemptReason)
            ? "No modifying Akron feature has been used in this attempt."
            : attemptReason;
        ContributorDigest = contributorDigest;
        MapDigest = mapDigest;
        AreaMode = areaMode;
        ViewDigest = AkronScreenProjection.ViewDigest;
        FrameBucket = frameBucket;
        DisplaySeconds = Math.Max(AkronScreenProjection.MinimumDisplaySeconds, displaySeconds);
        StatusSlot = AkronScreenProjection.GetStatusSlot(attemptStatus);
        layoutWords = AkronScreenProjection.BuildLayoutWords(this);
        summaryWords = AkronScreenProjection.BuildSummaryWords(this);
        shadePattern = AkronScreenProjection.BuildShadePattern(layoutWords);
        summaryPattern = AkronScreenProjection.BuildSingleBitPattern(summaryWords);
    }

    public int LayoutVersion { get; }
    public AkronStatus AttemptStatus { get; }
    public string AttemptReason { get; }
    public uint ContributorDigest { get; }
    public uint MapDigest { get; }
    public int AreaMode { get; }
    public uint ViewDigest { get; }
    public uint FrameBucket { get; }
    public float DisplaySeconds { get; }
    public int StatusSlot { get; }
    public int LayoutWordCount => layoutWords.Length;
    public int ShadeStepCount => shadePattern.Length;
    public int SummaryWordCount => summaryWords.Length;
    public int SummaryStepCount => summaryPattern.Length;

    public uint GetLayoutWordForTesting(int index) {
        return layoutWords[index];
    }

    public uint GetSummaryWordForTesting(int index) {
        return summaryWords[index];
    }

    internal byte GetShadeStep(int index) {
        return shadePattern[index % shadePattern.Length];
    }

    internal byte GetSummaryStep(int index) {
        return summaryPattern[index % summaryPattern.Length];
    }
}

internal readonly struct AkronRenderCell {
    public AkronRenderCell(int layer, int x, int y, int width, int height, bool increase, int polarity) {
        Layer = layer;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Increase = increase;
        Polarity = polarity;
    }

    public int Layer { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public bool Increase { get; }
    public int Polarity { get; }
}

internal static partial class AkronScreenProjection {
    public const float MinimumDisplaySeconds = 12f;
    public static readonly uint ViewDigest = HashText("akron.viewport.composite.v1");

    private const ushort LayoutPrefix = 0xA6D3;
    private const int ShadeRepeatCount = 3;
    private const ulong CompletionArtWarmupFrames = 180ul;
    private const int InteriorCellCount = 13824;
    private const int EdgeCellCount = 4608;
    private const int SummaryCellCount = 24576;
    private const ushort SummaryPrefix = 0x5D7E;
    private const float InteriorAlpha = 0.010f;
    private const float EdgeAlpha = 0.008f;
    private const float SummaryAlpha = 0.012f;
    private const float SummarySafeLeft = 0.26f;
    private const float SummarySafeTop = 0.26f;
    private const float SummarySafeWidth = 0.48f;
    private const float SummarySafeHeight = 0.48f;
    private static readonly Color HighShelfTint = new Color(255, 0, 0);
    private static readonly Color LowShelfTint = new Color(0, 50, 255);
    private static AkronRenderPass activeRecord;
    private static ulong activeVisibleFromFrame;
    private static ulong renderedFrame = ulong.MaxValue;

    public static void Attach(Level level) {
        if (level == null || AkronModule.Session == null || AkronModule.Settings == null) {
            return;
        }

        AkronRenderPass record = Capture(level);
        activeRecord = record;
        activeVisibleFromFrame = Engine.FrameCounter + CompletionArtWarmupFrames;
        renderedFrame = ulong.MaxValue;
    }

    public static AkronRenderPass Capture(Level level) {
        string mapSid = level?.Session?.Area.GetSID() ?? "unknown";
        int areaMode = level?.Session == null ? 0 : (int) level.Session.Area.Mode;
        return CaptureForTesting(
            AkronModule.Settings,
            AkronModule.Session,
            mapSid,
            areaMode,
            Engine.FrameCounter);
    }

    public static AkronRenderPass CaptureForTesting(
        AkronModuleSettings settings,
        AkronModuleSession session,
        string mapSid,
        int areaMode,
        ulong frameCounter) {
        AkronStatus status = session?.AttemptStatus ?? AkronStatus.GoldberryHardlistClean;
        string reason = session?.AttemptReason ?? string.Empty;
        uint contributorDigest = BuildContributorDigest(AkronPolicy.GetActiveCheatContributors(settings, session));
        uint mapDigest = BuildMapDigest(mapSid, areaMode);
        float displaySeconds = Math.Max(
            MinimumDisplaySeconds,
            AkronModuleSettings.ClampRecordingEndscreenDurationSeconds(settings?.RecordingEndscreenDurationSeconds ?? 0f));

        return new AkronRenderPass(
            status,
            reason,
            contributorDigest,
            mapDigest,
            areaMode,
            (uint) (frameCounter & 0xFFFFFFFFu),
            displaySeconds);
    }

    public static AkronRenderPass CreateForTesting(
        AkronStatus status,
        IEnumerable<AkronFeatureKind> contributorKinds,
        string mapSid,
        int areaMode,
        ulong frameCounter,
        float displaySeconds) {
        return new AkronRenderPass(
            status,
            "test render pass",
            BuildContributorDigest(contributorKinds),
            BuildMapDigest(mapSid, areaMode),
            areaMode,
            (uint) (frameCounter & 0xFFFFFFFFu),
            displaySeconds);
    }

    public static int GetStatusSlot(AkronStatus status) {
        return status switch {
            AkronStatus.RegularClean => 1,
            AkronStatus.Cheat => 2,
            _ => 0
        };
    }

    public static uint BuildMapDigest(string mapSid, int areaMode) {
        uint hash = HashText(string.IsNullOrWhiteSpace(mapSid) ? "unknown" : mapSid.Trim());
        return HashInt(hash, areaMode);
    }

    public static uint BuildContributorDigest(IEnumerable<AkronActiveCheatContributor> contributors) {
        List<AkronFeatureKind> kinds = new List<AkronFeatureKind>();
        if (contributors != null) {
            foreach (AkronActiveCheatContributor contributor in contributors) {
                kinds.Add(contributor.Feature);
            }
        }

        return BuildContributorDigest(kinds);
    }

    public static uint BuildContributorDigest(IEnumerable<AkronFeatureKind> contributorKinds) {
        List<int> kinds = new List<int>();
        if (contributorKinds != null) {
            foreach (AkronFeatureKind kind in contributorKinds) {
                kinds.Add((int) kind);
            }
        }

        kinds.Sort();
        uint hash = HashInt(FnvOffset, kinds.Count);
        foreach (int kind in kinds) {
            hash = HashInt(hash, kind);
        }

        return hash;
    }

    public static uint[] BuildLayoutWords(AkronRenderPass record) {
        uint word0 =
            ((uint) LayoutPrefix << 16) |
            ((uint) (record.LayoutVersion & 0xFF) << 8) |
            ((uint) (record.AreaMode & 0x3F) << 2) |
            (uint) (record.StatusSlot & 0x03);
        uint word1 = record.MapDigest;
        uint word2 = record.ContributorDigest;
        uint word3 = record.ViewDigest;
        uint word4 = record.FrameBucket;
        uint checkA = HashWords(new[] { word0, word1, word2, word3, word4 });
        uint checkB = word0 ^ RotateLeft(word2, 7) ^ RotateLeft(word4, 13);
        uint checkC = RotateLeft(word1, 5) ^ RotateLeft(word3, 11) ^ checkA;

        return new[] {
            word0,
            word1,
            word2,
            word3,
            word4,
            checkA,
            checkB,
            checkC
        };
    }

    public static byte[] BuildShadePattern(uint[] words) {
        if (words == null || words.Length == 0) {
            return Array.Empty<byte>();
        }

        byte[] bits = new byte[words.Length * 32 * ShadeRepeatCount];
        int output = 0;
        for (int wordIndex = 0; wordIndex < words.Length; wordIndex++) {
            uint word = words[wordIndex];
            for (int shadeIndex = 31; shadeIndex >= 0; shadeIndex--) {
                byte bit = (byte) ((word >> shadeIndex) & 1u);
                for (int repeat = 0; repeat < ShadeRepeatCount; repeat++) {
                    bits[output++] = bit;
                }
            }
        }

        return bits;
    }

    public static uint[] BuildSummaryWords(AkronRenderPass record) {
        uint word0 =
            ((uint) SummaryPrefix << 16) |
            ((uint) (record.LayoutVersion & 0xFF) << 8) |
            ((uint) (record.AreaMode & 0x3F) << 2) |
            (uint) (record.StatusSlot & 0x03);
        uint word1 = HashWords(new[] { word0, record.ViewDigest });
        return new[] { word0, word1 };
    }

    public static byte[] BuildSingleBitPattern(uint[] words) {
        if (words == null || words.Length == 0) {
            return Array.Empty<byte>();
        }

        byte[] bits = new byte[words.Length * 32];
        int output = 0;
        for (int wordIndex = 0; wordIndex < words.Length; wordIndex++) {
            uint word = words[wordIndex];
            for (int shadeIndex = 31; shadeIndex >= 0; shadeIndex--) {
                bits[output++] = (byte) ((word >> shadeIndex) & 1u);
            }
        }

        return bits;
    }

    public static IReadOnlyList<AkronRenderCell> BuildRenderCellsForTesting(
        AkronRenderPass record,
        int width,
        int height,
        int frameIndex) {
        List<AkronRenderCell> cells = new List<AkronRenderCell>(InteriorCellCount + EdgeCellCount + SummaryCellCount);
        for (int index = 0; index < InteriorCellCount; index++) {
            cells.Add(BuildInteriorCell(record, width, height, frameIndex, index));
        }

        for (int index = 0; index < EdgeCellCount; index++) {
            cells.Add(BuildEdgeCell(record, width, height, frameIndex, index));
        }

        for (int index = 0; index < SummaryCellCount; index++) {
            cells.Add(BuildSummaryCell(record, width, height, frameIndex, index));
        }

        return cells;
    }

    internal static void RenderLayer(Scene scene, int width, int height) {
        if (scene == null ||
            activeRecord == null ||
            !ShouldRenderForScene(scene) ||
            Engine.FrameCounter < activeVisibleFromFrame ||
            width <= 0 ||
            height <= 0) {
            return;
        }

        if (renderedFrame == Engine.FrameCounter) {
            return;
        }

        renderedFrame = Engine.FrameCounter;
        int framePhase = 0;
        RenderInterior(activeRecord, width, height, framePhase);
        RenderEdge(activeRecord, width, height, framePhase);
        RenderSummary(activeRecord, width, height, framePhase);
    }

    private static bool ShouldRenderForScene(Scene scene) {
        return scene is AreaComplete;
    }

    internal static void RenderInterior(AkronRenderPass record, int width, int height, int frameIndex) {
        if (record == null || width <= 0 || height <= 0 || record.ShadeStepCount == 0) {
            return;
        }

        for (int index = 0; index < InteriorCellCount; index++) {
            DrawCell(BuildInteriorCell(record, width, height, frameIndex, index), InteriorAlpha);
        }
    }

    private static void RenderEdge(AkronRenderPass record, int width, int height, int frameIndex) {
        if (record == null || width <= 0 || height <= 0 || record.ShadeStepCount == 0) {
            return;
        }

        for (int index = 0; index < EdgeCellCount; index++) {
            DrawCell(BuildEdgeCell(record, width, height, frameIndex, index), EdgeAlpha);
        }
    }

    private static void RenderSummary(AkronRenderPass record, int width, int height, int frameIndex) {
        if (record == null || width <= 0 || height <= 0 || record.SummaryStepCount == 0) {
            return;
        }

        for (int index = 0; index < SummaryCellCount; index++) {
            DrawCell(BuildSummaryCell(record, width, height, frameIndex, index), SummaryAlpha);
        }
    }

    private static AkronRenderCell BuildInteriorCell(
        AkronRenderPass record,
        int width,
        int height,
        int frameIndex,
        int index) {
        int pairIndex = index / 2;
        uint seed = BuildCellSeed(record, frameIndex, pairIndex, 0);
        int shadeIndex = PositiveModulo(pairIndex * 11 + frameIndex * 17, record.ShadeStepCount);
        int polarity = ((seed >> 8) & 1u) == 0u ? 1 : -1;
        bool firstIncrease = (record.GetShadeStep(shadeIndex) != 0) == (polarity > 0);
        bool increase = (index & 1) == 0 ? firstIncrease : !firstIncrease;
        float xUnit = 0.15f + UnitFloat(seed) * 0.70f;
        float yUnit = 0.18f + UnitFloat(Mix(seed ^ 0x7351A2D5u)) * 0.64f;
        int size = Math.Max(1, Math.Min(width, height) / 1080);

        return new AkronRenderCell(
            0,
            PairX((int) (xUnit * width), width, size, seed, index),
            PairY((int) (yUnit * height), height, size, size, seed, index),
            size,
            size,
            increase,
            polarity);
    }

    private static AkronRenderCell BuildEdgeCell(
        AkronRenderPass record,
        int width,
        int height,
        int frameIndex,
        int index) {
        int pairIndex = index / 2;
        uint seed = BuildCellSeed(record, frameIndex, pairIndex, 1);
        int shadeIndex = PositiveModulo(pairIndex * 19 + frameIndex * 23 + 5, record.ShadeStepCount);
        int polarity = ((seed >> 8) & 1u) == 0u ? 1 : -1;
        bool firstIncrease = (record.GetShadeStep(shadeIndex) != 0) == (polarity > 0);
        bool increase = (index & 1) == 0 ? firstIncrease : !firstIncrease;
        int band = (int) (seed & 3u);
        float along = 0.22f + UnitFloat(Mix(seed ^ 0xB8C4D91Fu)) * 0.56f;
        float across = UnitFloat(Mix(seed ^ 0x4E2F6A91u));
        float xUnit;
        float yUnit;
        int cellWidth;
        int cellHeight;

        if (band == 0 || band == 1) {
            xUnit = along;
            yUnit = (band == 0 ? 0.24f : 0.70f) + across * 0.05f;
            cellWidth = Math.Max(1, width / 1280);
            cellHeight = Math.Max(1, height / 1080);
        } else {
            xUnit = (band == 2 ? 0.24f : 0.72f) + across * 0.05f;
            yUnit = along;
            cellWidth = Math.Max(1, width / 1080);
            cellHeight = Math.Max(1, height / 1280);
        }

        return new AkronRenderCell(
            1,
            PairX((int) (xUnit * width), width, cellWidth, seed, index),
            PairY((int) (yUnit * height), height, cellWidth, cellHeight, seed, index),
            cellWidth,
            cellHeight,
            increase,
            polarity);
    }

    private static AkronRenderCell BuildSummaryCell(
        AkronRenderPass record,
        int width,
        int height,
        int frameIndex,
        int index) {
        int pairIndex = index / 2;
        uint seed = BuildCellSeed(record, frameIndex, pairIndex, 2);
        int shadeIndex = PositiveModulo(pairIndex * 7 + frameIndex * 13, record.SummaryStepCount);
        int polarity = ((seed >> 8) & 1u) == 0u ? 1 : -1;
        bool firstIncrease = (record.GetSummaryStep(shadeIndex) != 0) == (polarity > 0);
        bool increase = (index & 1) == 0 ? firstIncrease : !firstIncrease;
        float xUnit = SummarySafeLeft + UnitFloat(seed) * SummarySafeWidth;
        float yUnit = SummarySafeTop + UnitFloat(Mix(seed ^ 0x5167E3B1u)) * SummarySafeHeight;
        int size = Math.Max(2, Math.Min(width, height) / 1080);
        int pairSpanX = IsHorizontalPair(seed) ? size * 2 : size;
        int pairSpanY = IsHorizontalPair(seed) ? size : size * 2;
        int pairX = AlignEven((int) (xUnit * width), width, pairSpanX);
        int pairY = AlignEven((int) (yUnit * height), height, pairSpanY);

        return new AkronRenderCell(
            2,
            PairX(pairX, width, size, seed, index),
            PairY(pairY, height, size, size, seed, index),
            size,
            size,
            increase,
            polarity);
    }

    private static void DrawCell(AkronRenderCell cell, float alpha) {
        Color color = (cell.Increase ? HighShelfTint : LowShelfTint) * alpha;
        Draw.Rect(cell.X, cell.Y, cell.Width, cell.Height, color);
    }

    private static uint BuildCellSeed(AkronRenderPass record, int frameIndex, int index, int layer) {
        uint seed = record.ViewDigest;
        seed = HashInt(seed, record.LayoutVersion);
        seed = HashInt(seed, frameIndex);
        seed = HashInt(seed, index);
        seed = HashInt(seed, layer);
        return Mix(seed);
    }

    private static int PairX(int value, int maximum, int width, uint seed, int index) {
        int x = ClampCoordinate(value, maximum, IsHorizontalPair(seed) ? width * 2 : width);
        return ((index & 1) != 0 && IsHorizontalPair(seed))
            ? ClampCoordinate(x + width, maximum)
            : x;
    }

    private static int PairY(int value, int maximum, int width, int height, uint seed, int index) {
        int y = ClampCoordinate(value, maximum, IsHorizontalPair(seed) ? height : height * 2);
        return ((index & 1) != 0 && !IsHorizontalPair(seed))
            ? ClampCoordinate(y + height, maximum)
            : y;
    }

    private static bool IsHorizontalPair(uint seed) {
        return ((seed >> 9) & 1u) == 0u;
    }

    private static int ClampCoordinate(int value, int maximum) {
        if (maximum <= 1) {
            return 0;
        }

        if (value < 0) {
            return 0;
        }

        return value >= maximum ? maximum - 1 : value;
    }

    private static int ClampCoordinate(int value, int maximum, int span) {
        if (maximum <= 1) {
            return 0;
        }

        int lastStart = Math.Max(0, maximum - Math.Max(1, span));
        if (value < 0) {
            return 0;
        }

        return value > lastStart ? lastStart : value;
    }

    private static int AlignEven(int value, int maximum, int span) {
        int aligned = value & ~1;
        return ClampCoordinate(aligned, maximum, span);
    }

    private static int PositiveModulo(int value, int modulo) {
        if (modulo <= 0) {
            return 0;
        }

        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private static float UnitFloat(uint value) {
        return (value & 0x00FFFFFFu) / 16777216f;
    }

    private const uint FnvOffset = 2166136261u;
    private const uint FnvPrime = 16777619u;

    private static uint HashText(string value) {
        uint hash = FnvOffset;
        if (value == null) {
            return HashInt(hash, 0);
        }

        for (int index = 0; index < value.Length; index++) {
            char character = value[index];
            hash ^= (byte) character;
            hash *= FnvPrime;
            hash ^= (byte) (character >> 8);
            hash *= FnvPrime;
        }

        return hash;
    }

    private static uint HashWords(IReadOnlyList<uint> words) {
        uint hash = FnvOffset;
        for (int index = 0; index < words.Count; index++) {
            hash = HashUInt(hash, words[index]);
        }

        return hash;
    }

    private static uint HashInt(uint hash, int value) {
        return HashUInt(hash, (uint) value);
    }

    private static uint HashUInt(uint hash, uint value) {
        unchecked {
            hash ^= value & 0xFFu;
            hash *= FnvPrime;
            hash ^= (value >> 8) & 0xFFu;
            hash *= FnvPrime;
            hash ^= (value >> 16) & 0xFFu;
            hash *= FnvPrime;
            hash ^= (value >> 24) & 0xFFu;
            hash *= FnvPrime;
            return hash;
        }
    }

    private static uint Mix(uint value) {
        unchecked {
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return value;
        }
    }

    private static uint RotateLeft(uint value, int bits) {
        return (value << bits) | (value >> (32 - bits));
    }
}

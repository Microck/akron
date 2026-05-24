using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.Akron;
using Microsoft.Xna.Framework;
using Xunit;

namespace Celeste.Mod.Akron.Tests;

public sealed class OverlayTests {
    [Fact]
    public void ExternalToolTabsAreHiddenWhenTheirModsAreMissing() {
        string[] visibleTabs = InvokeBuildVisibleTabs(speedrunToolLoaded: false, celesteTasLoaded: false, extendedVariantModeAvailable: false);

        Assert.DoesNotContain("Speedrun Tool", visibleTabs);
        Assert.DoesNotContain("CelesteTAS", visibleTabs);
        Assert.DoesNotContain("Extended Variant Mode", visibleTabs);
    }

    [Fact]
    public void ExternalToolTabsAppearOnlyForAvailableMods() {
        string[] visibleTabs = InvokeBuildVisibleTabs(speedrunToolLoaded: true, celesteTasLoaded: true, extendedVariantModeAvailable: true);

        Assert.Contains("Speedrun Tool", visibleTabs);
        Assert.Contains("CelesteTAS", visibleTabs);
        Assert.Contains("Extended Variant Mode", visibleTabs);
    }

    [Fact]
    public void ExternalToolSectionsRemainToggleableForCollapseCommands() {
        string[] toggleableSections = InvokeStaticStringArray("GetToggleableSections");

        foreach (string title in new[] { "Speedrun Tool", "CelesteTAS", "Extended Variant Mode" }) {
            Assert.Contains(title, toggleableSections);
        }
    }

    [Fact]
    public void ExternalToolColumnsDoNotChangeVanillaRows() {
        List<string> playerLabels = BuildOverlayEntryLabels("Player");
        List<string> creatorLabels = BuildOverlayEntryLabels("Creator");

        Assert.DoesNotContain("Extended Variants Master", playerLabels);
        Assert.DoesNotContain("Extended Variants Randomizer", playerLabels);
        Assert.DoesNotContain("Reset Extended", playerLabels);
        Assert.DoesNotContain("Reset Vanilla", playerLabels);
        Assert.Contains("Export Room Times", creatorLabels);
    }

    [Fact]
    public void ExternalToolTabsExposeTheirOwnRows() {
        List<string> speedrunToolLabels = BuildOverlayEntryLabels("Speedrun Tool");
        List<string> celesteTasLabels = BuildOverlayEntryLabels("CelesteTAS");
        List<string> extendedVariantModeLabels = BuildOverlayEntryLabels("Extended Variant Mode");

        Assert.Contains("SRT Status", speedrunToolLabels);
        Assert.Contains("SRT Slot", speedrunToolLabels);
        Assert.Contains("TAS Status", celesteTasLabels);
        Assert.Contains("Play Configured TAS", celesteTasLabels);
        Assert.Contains("Extended Variants Master", extendedVariantModeLabels);
        Assert.Contains("Extended Variants Randomizer", extendedVariantModeLabels);
        Assert.Contains("Reset Extended", extendedVariantModeLabels);
        Assert.Contains("Reset Vanilla", extendedVariantModeLabels);
    }

    [Fact]
    public void MotionSmoothingRowsStayInGlobalOnlyWhenInstalled() {
        List<string> missingLabels = BuildGlobalEntryLabels(motionSmoothingLoaded: false);
        List<string> loadedLabels = BuildGlobalEntryLabels(motionSmoothingLoaded: true);

        Assert.DoesNotContain("FPS Bypass", missingLabels);
        Assert.DoesNotContain("TPS Bypass", missingLabels);
        Assert.Equal("FPS Bypass", loadedLabels[0]);
        Assert.Equal("TPS Bypass", loadedLabels[1]);
        Assert.DoesNotContain("Motion Smoothing", InvokeBuildVisibleTabs(speedrunToolLoaded: true, celesteTasLoaded: true, extendedVariantModeAvailable: true));
    }

    [Fact]
    public void MotionSmoothingBypassTargetsUseInlineNumericInputs() {
        Dictionary<string, string> controls = BuildGlobalEntryControls(motionSmoothingLoaded: true);

        Assert.Equal("NumericInput", controls["FPS Bypass"]);
        Assert.Equal("NumericInput", controls["TPS Bypass"]);
        Assert.True(HasOverlayOptionsPopup("FPS Bypass"));
        Assert.False(HasOverlayOptionsPopup("TPS Bypass"));
    }

    [Fact]
    public void TransientResetKeepsCollapsedWindowState() {
        AkronOverlay overlay = CreateOverlayForStateTest();

        Assert.True(overlay.ToggleCollapsedWindow("Global"));

        overlay.ResetTransientUiState(searchAutofocus: false);

        Assert.Contains("Global", GetCollapsedWindowTitles(overlay));
    }

    [Theory]
    [InlineData(true, false, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, true, false, AkronOverlay.OverlayCancelAction.ClearSearch)]
    [InlineData(false, false, true, AkronOverlay.OverlayCancelAction.CloseCommunityPackBrowser)]
    [InlineData(false, false, false, AkronOverlay.OverlayCancelAction.KeepOverlayOpen)]
    public void CancelInputDoesNotCloseBaseOverlay(bool searchInputActive, bool hasSearchQuery, bool communityPackBrowserOpen, AkronOverlay.OverlayCancelAction expected) {
        Assert.Equal(expected, AkronOverlay.ResolveCancelAction(searchInputActive, hasSearchQuery, communityPackBrowserOpen));
    }

    [Theory]
    [InlineData(true, false, false, false, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(true, true, false, false, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, false, false, true, false)]
    public void OverlayRenderSurfaceIgnoresDeathWipeState(bool visible, bool promptMenuOpen, bool autoKillAreaSelectionActive, bool autoDeafenAreaSelectionActive, bool expected) {
        Assert.Equal(expected, AkronOverlay.ShouldRenderOverlaySurface(visible, promptMenuOpen, autoKillAreaSelectionActive, autoDeafenAreaSelectionActive));
    }

    [Fact]
    public void StartPosRowUsesSnapshotSlotPopupKey() {
        Dictionary<string, string> popupKeys = BuildOverlayEntryOptionsPopupKeys("StartPos");

        Assert.Equal("StartPos Snapshot Slot", popupKeys["StartPos"]);
        Assert.True(HasOverlayOptionsPopup("StartPos Snapshot Slot"));
    }

    [Fact]
    public void ToastStackOffsetAddsEveryNewerToastHeightPlusGap() {
        Assert.Equal(0f, AkronToast.CalculateStackOffset(Array.Empty<float>()));
        Assert.Equal(40f, AkronToast.CalculateStackOffset(new[] { 12f, 16f }));
    }

    [Theory]
    [InlineData("Global", 0)]
    [InlineData("Level", 1)]
    [InlineData("Player", 3)]
    [InlineData("Shortcuts", 7)]
    public void BaseTabsKeepTheirEightColumnPositions(string tabName, int expectedColumn) {
        Assert.Equal(expectedColumn, InvokeActionColumnIndex(tabName, 8, new List<float>(), new List<int>()));
    }

    [Fact]
    public void ShortExternalToolTabsUseShortestSingleSectionColumns() {
        List<float> columnBottoms = new List<float> { 100f, 50f, 110f, 70f, 80f, 120f, 90f, 130f };
        List<int> columnSectionCounts = new List<int> { 2, 1, 2, 1, 1, 2, 1, 2 };

        int speedrunToolColumn = InvokeActionColumnIndex("Speedrun Tool", 8, columnBottoms, columnSectionCounts);
        Assert.Equal(1, speedrunToolColumn);

        columnBottoms[speedrunToolColumn] = 160f;
        columnSectionCounts[speedrunToolColumn]++;

        int celesteTasColumn = InvokeActionColumnIndex("CelesteTAS", 8, columnBottoms, columnSectionCounts);
        Assert.Equal(3, celesteTasColumn);
    }

    [Fact]
    public void ExtendedVariantModeUsesShortestCurrentColumn() {
        List<float> columnBottoms = new List<float> { 100f, 160f, 110f, 160f, 80f, 120f, 90f, 130f };
        List<int> columnSectionCounts = new List<int> { 2, 2, 2, 2, 1, 2, 1, 2 };

        Assert.Equal(4, InvokeActionColumnIndex("Extended Variant Mode", 8, columnBottoms, columnSectionCounts));
    }

    [Fact]
    public void HudElementOverlapFadesEveryLabelWhenOnlyCurrentIsOff() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Fade,
            CustomHudLabelObstructedOpacity = 35,
            CustomHudLabelObstructionOnlyOverlappedLabel = false
        };
        Vector2 position = TestVector(20f, 20f);
        float opacity = 0.90f;

        bool applied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            new AkronHudRect(500f, 500f, 16f, 32f),
            true,
            TestVector(80f, 24f),
            ref position,
            ref opacity);

        Assert.True(applied);
        Assert.Equal(20f, position.X);
        Assert.Equal(20f, position.Y);
        Assert.Equal(0.35f, opacity, precision: 3);
    }

    [Fact]
    public void HudElementOverlapOnlyCurrentRequiresElementBoundsToIntersect() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Fade,
            CustomHudLabelObstructedOpacity = 35,
            CustomHudLabelObstructionOnlyOverlappedLabel = true,
            CustomHudLabelObstructionPaddingPixels = 0
        };
        AkronHudRect player = new AkronHudRect(500f, 500f, 16f, 32f);
        Vector2 farPosition = TestVector(20f, 20f);
        float farOpacity = 0.90f;

        bool farApplied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            player,
            true,
            TestVector(80f, 24f),
            ref farPosition,
            ref farOpacity);

        Vector2 overlappingPosition = TestVector(490f, 510f);
        float overlappingOpacity = 0.90f;
        bool overlappingApplied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            player,
            false,
            TestVector(80f, 24f),
            ref overlappingPosition,
            ref overlappingOpacity);

        Assert.False(farApplied);
        Assert.Equal(0.90f, farOpacity, precision: 3);
        Assert.True(overlappingApplied);
        Assert.Equal(0.35f, overlappingOpacity, precision: 3);
    }

    [Fact]
    public void HudElementOverlapMoveUsesConfiguredObstructedAnchor() {
        AkronModuleSettings settings = new AkronModuleSettings {
            CustomHudLabelObstructionEnabled = true,
            CustomHudLabelObstructionMode = AkronLabelObstructionMode.Move,
            CustomHudLabelPadding = 100,
            CustomHudLabelObstructedAnchor = AkronHudAnchor.BottomRight,
            CustomHudLabelObstructedOffsetX = -12,
            CustomHudLabelObstructedOffsetY = 8
        };
        Vector2 position = TestVector(20f, 20f);
        float opacity = 0.90f;

        bool applied = AkronHudRenderer.TryApplyHudElementPlayerOverlap(
            settings,
            new AkronHudRect(500f, 500f, 16f, 32f),
            true,
            TestVector(80f, 24f),
            ref position,
            ref opacity);

        Assert.True(applied);
        Assert.Equal(1728f, position.X);
        Assert.Equal(964f, position.Y);
        Assert.Equal(0.90f, opacity, precision: 3);
    }

    private static Vector2 TestVector(float x, float y) {
        Vector2 vector = default;
        vector.X = x;
        vector.Y = y;
        return vector;
    }

    private static string[] InvokeBuildVisibleTabs(bool speedrunToolLoaded, bool celesteTasLoaded, bool extendedVariantModeAvailable) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildVisibleTabs", BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((IEnumerable<string>) method.Invoke(null, new object[] { speedrunToolLoaded, celesteTasLoaded, extendedVariantModeAvailable })!).ToArray();
    }

    private static string[] InvokeStaticStringArray(string methodName) {
        MethodInfo method = typeof(AkronOverlay).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)!;
        return ((IEnumerable<string>) method.Invoke(null, Array.Empty<object>())!).ToArray();
    }

    private static int InvokeActionColumnIndex(string tabName, int availableColumns, List<float> columnBottoms, List<int> columnSectionCounts) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("GetActionColumnIndex", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (int) method.Invoke(null, new object[] { tabName, availableColumns, columnBottoms, columnSectionCounts, 500f })!;
    }

    private static List<string> BuildOverlayEntryLabels(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        return ExtractOverlayEntryLabels(entries);
    }

    private static List<string> BuildGlobalEntryLabels(bool motionSmoothingLoaded) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildGlobalEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object[] { motionSmoothingLoaded })!;
        return ExtractOverlayEntryLabels(entries);
    }

    private static Dictionary<string, string> BuildGlobalEntryControls(bool motionSmoothingLoaded) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildGlobalEntries", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object[] { motionSmoothingLoaded })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo controlProperty = entryType.GetProperty("Control", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => controlProperty.GetValue(entry)!.ToString()!,
                StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> BuildOverlayEntryOptionsPopupKeys(string tab) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("BuildDisplayEntriesForTab", BindingFlags.NonPublic | BindingFlags.Static)!;
        object entries = method.Invoke(null, new object?[] { tab, null })!;
        Type entryType = entries.GetType().GetGenericArguments()[0];
        PropertyInfo labelProperty = entryType.GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;
        PropertyInfo popupKeyProperty = entryType.GetProperty("OptionsPopupKey", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .ToDictionary(
                entry => (string) labelProperty.GetValue(entry)!,
                entry => (string) (popupKeyProperty.GetValue(entry) ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractOverlayEntryLabels(object entries) {
        PropertyInfo labelProperty = entries.GetType().GetGenericArguments()[0].GetProperty("Label", BindingFlags.Public | BindingFlags.Instance)!;

        return ((System.Collections.IEnumerable) entries)
            .Cast<object>()
            .Select(entry => (string) labelProperty.GetValue(entry)!)
            .ToList();
    }

    private static bool HasOverlayOptionsPopup(string label) {
        MethodInfo method = typeof(AkronOverlay).GetMethod("HasOptionsPopup", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (bool) method.Invoke(null, new object[] { label })!;
    }

    private static HashSet<string> GetCollapsedWindowTitles(AkronOverlay overlay) {
        FieldInfo field = typeof(AkronOverlay).GetField("collapsedWindowTitles", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (HashSet<string>) field.GetValue(overlay)!;
    }

    private static AkronOverlay CreateOverlayForStateTest() {
        AkronOverlay overlay = (AkronOverlay) RuntimeHelpers.GetUninitializedObject(typeof(AkronOverlay));
        SetPrivateField(overlay, "collapsedWindowTitles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetPrivateField(overlay, "pendingImGuiCollapseSync", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        overlay.Visible = true;
        return overlay;
    }

    private static void SetPrivateField(object target, string fieldName, object value) {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }

}

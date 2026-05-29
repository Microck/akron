using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Celeste;

namespace Celeste.Mod.Akron;

public sealed class AkronCommunityRulesetManifest {
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PrimaryRuleset? PrimaryRuleset { get; set; }
    public bool? ProofModeOverlay { get; set; }
    public bool? LowDistractionOverlay { get; set; }
    public bool? SafeMode { get; set; }
    public IndicatorVisibility? IndicatorVisibility { get; set; }
    public IndicatorCorner? IndicatorCorner { get; set; }
    public int? IndicatorOffsetX { get; set; }
    public int? IndicatorOffsetY { get; set; }
    public bool? ConsumeGameplayInputInMenu { get; set; }
    public bool? PauseGameplayInMenu { get; set; }
    public bool? RoomLabels { get; set; }
    public bool? StaminaWidget { get; set; }
    public bool? SpeedWidget { get; set; }
    public bool? DashWidget { get; set; }
    public bool? InputViewer { get; set; }
    public bool? InputHistoryPanel { get; set; }
    public bool? RoomTimerWidget { get; set; }
    public bool? DeathStatsWidget { get; set; }
    public bool? StaminaBar { get; set; }
    public bool? StaminaBarPlayer { get; set; }
    public bool? StaminaBarHud { get; set; }
    public AkronStaminaPlayerBarPosition? StaminaBarPlayerPosition { get; set; }
    public AkronStaminaHudPosition? StaminaBarHudPosition { get; set; }
    public bool? StaminaShowOverflow { get; set; }
    public bool? StaminaHideWhilePaused { get; set; }
    public int? StaminaNormalColor { get; set; }
    public int? StaminaLowColor { get; set; }
    public int? StaminaFillColor { get; set; }
    public int? StaminaLineColor { get; set; }
    public bool? DashBar { get; set; }
    public bool? ReducedVisualNoise { get; set; }
    public bool? NoParticles { get; set; }
    public bool? NoTrails { get; set; }
    public bool? NoGlitch { get; set; }
    public bool? NoAnxiety { get; set; }
    public bool? NoDistortion { get; set; }
    public bool? HitboxViewer { get; set; }
    public bool? EntityInspector { get; set; }
    public bool? InfiniteStamina { get; set; }
    public bool? InfiniteDash { get; set; }
    public bool? Noclip { get; set; }
    public int? NoclipSpeed { get; set; }
    public int? NoclipFloatSpeed { get; set; }
    public bool? NoclipDrawOnTop { get; set; }
    public bool? Invincibility { get; set; }
    public bool? EverestSafeAutoBlock { get; set; }
    public string SourcePath { get; set; } = string.Empty;
}

public static class AkronCommunityRulesets {
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<AkronCommunityRulesetManifest> LoadAvailable() {
        List<AkronCommunityRulesetManifest> manifests = new List<AkronCommunityRulesetManifest>();
        HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AkronCommunityRulesetManifest manifest in BuiltInManifests()) {
            if (seenIds.Add(manifest.Id)) {
                manifests.Add(manifest);
            }
        }

        foreach (string directory in new[] { GetBundledDirectory(), GetLocalDirectory() }.Where(Directory.Exists)) {
            foreach (string path in Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)) {
                try {
                    AkronCommunityRulesetManifest manifest = JsonSerializer.Deserialize<AkronCommunityRulesetManifest>(File.ReadAllText(path), JsonOptions);
                    if (manifest == null) {
                        continue;
                    }

                    manifest.SourcePath = path;
                    if (string.IsNullOrWhiteSpace(manifest.Id)) {
                        manifest.Id = Path.GetFileNameWithoutExtension(path);
                    }
                    if (string.IsNullOrWhiteSpace(manifest.Label)) {
                        manifest.Label = manifest.Id;
                    }
                    if (seenIds.Add(manifest.Id)) {
                        manifests.Add(manifest);
                    }
                } catch {
                }
            }
        }

        return manifests;
    }

    private static IEnumerable<AkronCommunityRulesetManifest> BuiltInManifests() {
        yield return new AkronCommunityRulesetManifest {
            Id = "hard-clears-submit",
            Label = "Hard Clears Submit",
            Description = "Conservative Hard Clears submission preset: input overlay only, no input-history frame counts, no practice/cheat tools, no internal info viewers, no major visual simplification.",
            PrimaryRuleset = PrimaryRuleset.LeaderboardClean,
            ProofModeOverlay = false,
            LowDistractionOverlay = false,
            SafeMode = true,
            IndicatorVisibility = global::Celeste.Mod.Akron.IndicatorVisibility.ShowWhenFlagged,
            IndicatorCorner = global::Celeste.Mod.Akron.IndicatorCorner.TopRight,
            ConsumeGameplayInputInMenu = true,
            PauseGameplayInMenu = false,
            RoomLabels = false,
            StaminaWidget = false,
            SpeedWidget = false,
            DashWidget = false,
            InputViewer = true,
            InputHistoryPanel = false,
            RoomTimerWidget = false,
            DeathStatsWidget = false,
            StaminaBar = false,
            DashBar = false,
            ReducedVisualNoise = false,
            NoParticles = false,
            NoTrails = false,
            NoGlitch = false,
            NoAnxiety = false,
            NoDistortion = false,
            HitboxViewer = false,
            EntityInspector = false,
            InfiniteStamina = false,
            InfiniteDash = false,
            Noclip = false,
            Invincibility = false,
            EverestSafeAutoBlock = true,
            SourcePath = "built-in"
        };
    }

    public static void Apply(AkronCommunityRulesetManifest manifest) {
        if (manifest == null) {
            return;
        }

        AkronModuleSettings settings = AkronModule.Settings;
        if (manifest.PrimaryRuleset.HasValue) {
            settings.ApplyRulesetDefaults(manifest.PrimaryRuleset.Value);
        }

        ApplyIfPresent(manifest.ProofModeOverlay, value => settings.ProofModeOverlay = value);
        ApplyIfPresent(manifest.LowDistractionOverlay, settings.SetLowDistractionChannels);
        ApplyIfPresent(manifest.SafeMode, value => settings.SafeMode = value);
        ApplyIfPresent(manifest.IndicatorVisibility, value => settings.IndicatorVisibility = value);
        ApplyIfPresent(manifest.IndicatorCorner, value => settings.IndicatorCorner = value);
        ApplyIfPresent(manifest.IndicatorOffsetX, value => settings.IndicatorOffsetX = value);
        ApplyIfPresent(manifest.IndicatorOffsetY, value => settings.IndicatorOffsetY = value);
        ApplyIfPresent(manifest.ConsumeGameplayInputInMenu, value => settings.ConsumeGameplayInputInMenu = value);
        ApplyIfPresent(manifest.PauseGameplayInMenu, value => settings.PauseGameplayInMenu = value);
        ApplyIfPresent(manifest.RoomLabels, value => settings.RoomLabels = value);
        ApplyIfPresent(manifest.StaminaWidget, value => settings.StaminaWidget = value);
        ApplyIfPresent(manifest.SpeedWidget, value => settings.SpeedWidget = value);
        ApplyIfPresent(manifest.DashWidget, value => settings.DashWidget = value);
        ApplyIfPresent(manifest.InputViewer, value => settings.InputViewer = value);
        ApplyIfPresent(manifest.InputHistoryPanel, value => settings.InputHistoryPanel = value);
        ApplyIfPresent(manifest.RoomTimerWidget, value => settings.RoomTimerWidget = value);
        ApplyIfPresent(manifest.DeathStatsWidget, value => settings.DeathStatsWidget = value);
        ApplyIfPresent(manifest.StaminaBar, value => settings.StaminaBar = value);
        ApplyIfPresent(manifest.StaminaBarPlayer, value => settings.StaminaBarPlayer = value);
        ApplyIfPresent(manifest.StaminaBarHud, value => settings.StaminaBarHud = value);
        ApplyIfPresent(manifest.StaminaBarPlayerPosition, value => settings.StaminaBarPlayerPosition = value);
        ApplyIfPresent(manifest.StaminaBarHudPosition, value => settings.StaminaBarHudPosition = value);
        ApplyIfPresent(manifest.StaminaShowOverflow, value => settings.StaminaShowOverflow = value);
        ApplyIfPresent(manifest.StaminaHideWhilePaused, value => settings.StaminaHideWhilePaused = value);
        ApplyIfPresent(manifest.StaminaNormalColor, value => settings.StaminaNormalColor = value);
        ApplyIfPresent(manifest.StaminaLowColor, value => settings.StaminaLowColor = value);
        ApplyIfPresent(manifest.StaminaFillColor, value => settings.StaminaFillColor = value);
        ApplyIfPresent(manifest.StaminaLineColor, value => settings.StaminaLineColor = value);
        ApplyIfPresent(manifest.DashBar, value => settings.DashBar = value);
        ApplyIfPresent(manifest.ReducedVisualNoise, value => settings.ReducedVisualNoise = value);
        ApplyIfPresent(manifest.NoParticles, settings.SetNoParticles);
        ApplyIfPresent(manifest.NoTrails, settings.SetNoTrails);
        ApplyIfPresent(manifest.NoGlitch, settings.SetNoGlitch);
        ApplyIfPresent(manifest.NoAnxiety, settings.SetNoAnxiety);
        ApplyIfPresent(manifest.NoDistortion, settings.SetNoDistortion);
        ApplyIfPresent(manifest.HitboxViewer, value => settings.HitboxViewer = value);
        ApplyIfPresent(manifest.EntityInspector, value => settings.EntityInspector = value);
        ApplyIfPresent(manifest.InfiniteStamina, value => settings.InfiniteStamina = value);
        ApplyIfPresent(manifest.InfiniteDash, value => settings.InfiniteDash = value);
        ApplyIfPresent(manifest.Noclip, value => settings.Noclip = value);
        ApplyIfPresent(manifest.NoclipSpeed, value => settings.NoclipSpeed = AkronModuleSettings.ClampNoclipSpeed(value));
        ApplyIfPresent(manifest.NoclipFloatSpeed, value => settings.NoclipFloatSpeed = AkronModuleSettings.ClampNoclipFloatSpeed(value));
        ApplyIfPresent(manifest.NoclipDrawOnTop, value => settings.NoclipDrawOnTop = value);
        ApplyIfPresent(manifest.Invincibility, value => settings.Invincibility = value);
        ApplyIfPresent(manifest.EverestSafeAutoBlock, value => settings.EverestSafeAutoBlock = value);
    }

    public static string GetBundledDirectory() {
        return Path.Combine(Everest.PathGame, "Mods", "Akron", "Rulesets");
    }

    public static string GetLocalDirectory() {
        return Path.Combine(Everest.PathGame, "Saves", "AkronRulesets");
    }

    private static void ApplyIfPresent<T>(T? value, Action<T> apply) where T : struct {
        if (value.HasValue) {
            apply(value.Value);
        }
    }
}

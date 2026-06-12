using Celeste;
using Celeste.Mod;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using Monocle;

namespace Celeste.Mod.Akron;

public partial class AkronModuleSettings {
    public string DescribePresentationOverlays() {
        List<string> overlays = new List<string>();
        if (StreamerMode) {
            overlays.Add("Streamer Mode");
        }

        if (ProofModeOverlay) {
            overlays.Add("Proof-mode");
        }

        if (IsLowDistractionActive()) {
            overlays.Add("Low-distraction");
        }

        return overlays.Count == 0 ? "None" : string.Join(", ", overlays);
    }

    public string DescribeOverlayBehavior() {
        List<string> behaviors = new List<string>();
        if (StreamerMode) {
            behaviors.Add("Streamer Mode hides local filesystem paths by showing only filenames.");
        }

        if (ProofModeOverlay) {
            behaviors.Add("Proof-mode keeps proof surfaces compact instead of turning Akron into a permanent review HUD.");
        }

        if (IsLowDistractionActive()) {
            behaviors.Add("Low-distraction is active because every visual-noise channel is enabled: particles, trails, glitch, anxiety, and distortion.");
        }

        return behaviors.Count == 0
            ? "Presentation overlays change how Akron shows or records information."
            : string.Join(" ", behaviors);
    }

    public void SetLowDistractionChannels(bool enabled) {
        NoParticles = enabled;
        NoTrails = enabled;
        NoGlitch = enabled;
        NoAnxiety = enabled;
        NoDistortion = enabled;
        RefreshLowDistractionStateFlag();
    }

    public string FormatPathForDisplay(string path) {
        return FormatPathForDisplay(path, StreamerMode);
    }

    public static string FormatPathForDisplay(string path, bool streamerMode) {
        if (string.IsNullOrWhiteSpace(path)) {
            return string.Empty;
        }

        if (!streamerMode) {
            return path;
        }

        string trimmed = path.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fileName = Path.GetFileName(trimmed);
        return string.IsNullOrWhiteSpace(fileName) ? "Hidden by Streamer Mode" : fileName;
    }

    public void SetNoParticles(bool enabled) {
        NoParticles = enabled;
        RefreshLowDistractionStateFlag();
    }

    public void SetNoTrails(bool enabled) {
        NoTrails = enabled;
        RefreshLowDistractionStateFlag();
    }

    public void SetNoGlitch(bool enabled) {
        NoGlitch = enabled;
        RefreshLowDistractionStateFlag();
    }

    public void SetNoAnxiety(bool enabled) {
        NoAnxiety = enabled;
        RefreshLowDistractionStateFlag();
    }

    public void SetNoDistortion(bool enabled) {
        NoDistortion = enabled;
        RefreshLowDistractionStateFlag();
    }

    private void RefreshLowDistractionStateFlag() {
        LowDistractionOverlay = NoParticles && NoTrails && NoGlitch && NoAnxiety && NoDistortion;
    }

    public bool IsLowDistractionActive() {
        return NoParticles && NoTrails && NoGlitch && NoAnxiety && NoDistortion;
    }

    public void ResetHitboxStyle() {
        ShowHitboxTrail = false;
        HitboxTrailLength = 30;
        HitboxTrailOpacity = 55;
        HitboxLineThickness = 5f;
        HitboxFillOpacity = 0;
        HitboxBlackOutline = false;
        HitboxPlayerColor = DefaultHitboxPlayerColor;
        HitboxPlayerHurtboxColor = DefaultHitboxPlayerHurtboxColor;
        HitboxSolidColor = DefaultHitboxSolidColor;
        HitboxHazardColor = DefaultHitboxHazardColor;
        HitboxTriggerColor = DefaultHitboxTriggerColor;
        HitboxOtherColor = DefaultHitboxOtherColor;
        HitboxDeathColor = DefaultHitboxDeathColor;
        HitboxDeathPlayerColor = DefaultHitboxDeathPlayerColor;
    }

    public static string FormatStatus(AkronStatus status) {
        return status switch {
            AkronStatus.Unclassified => "Unclassified",
            AkronStatus.GoldberryHardlistClean => "Goldberry/Hardlist clear",
            AkronStatus.RegularClean => "Normal clear",
            AkronStatus.Cheat => "Cheat",
            _ => status.ToString()
        };
    }

    public static string DescribeBinding(ButtonBinding binding) {
        if (binding == null) {
            return "Unbound";
        }

        List<string> parts = new List<string>();
        AppendBindingValues(() => binding.Keys, parts);
        AppendBindingValues(() => binding.MouseButtons, parts);
        AppendBindingValues(() => binding.Buttons, parts);
        return parts.Count == 0 ? "Unbound" : string.Join(" / ", parts.Distinct());
    }

    private static void AppendBindingValues(Func<IEnumerable> getValues, List<string> parts) {
        IEnumerable values;
        try {
            values = getValues();
        } catch (InvalidProgramException) {
            return;
        }

        AppendBindingValues(values, parts);
    }

    private static void AppendBindingValues(IEnumerable values, List<string> parts) {
        if (values == null) {
            return;
        }

        foreach (object value in values) {
            string token = SimplifyBindingToken(value);
            if (!string.IsNullOrWhiteSpace(token)) {
                parts.Add(token);
            }
        }
    }

    private static string SimplifyBindingToken(object value) {
        return value switch {
            null => string.Empty,
            Keys.None => string.Empty,
            Buttons button when button == 0 => string.Empty,
            Keys.OemPlus => "+",
            Keys.OemMinus => "-",
            Keys.OemPipe => "\\",
            Keys.OemOpenBrackets => "[",
            Keys.OemCloseBrackets => "]",
            Keys.OemComma => ",",
            Keys.OemPeriod => ".",
            Keys.OemQuestion => "/",
            Keys.OemQuotes => "'",
            Keys.OemSemicolon => ";",
            Keys.OemTilde => "~",
            Keys.PageUp => "PgUp",
            Keys.PageDown => "PgDn",
            _ => value.ToString()?.Replace("LeftShoulder", "LB")
                .Replace("RightShoulder", "RB")
                .Replace("LeftTrigger", "LT")
                .Replace("RightTrigger", "RT")
                .Replace("LeftStick", "LStick")
                .Replace("RightStick", "RStick")
                .Replace("DPad", "DPad ")
                .Replace("NumPad", "Num ")
                .Replace("Oem", string.Empty)
                .Trim() ?? string.Empty
        };
    }

    public void CreateCurrentMapCompatibilityEntry(TextMenu menu, bool inGame) {
        menu.Add(new TextMenu.SubHeader("Current Map Compatibility"));
        if (!inGame || Engine.Scene is not Level level) {
            menu.Add(new TextMenu.Button("Open a map in-game to edit per-map compatibility overrides.") { Selectable = false });
            return;
        }

        menu.Add(new TextMenu.Button("Map: " + level.Session.Area.GetSID()) { Selectable = false });
        menu.Add(new TextMenu.Button("Always Use Broker: " + (AkronMapOverrides.ShouldForceBroker(level) ? "On" : "Off")).Pressed(() => AkronActions.ToggleForceBroker(level)));
        menu.Add(new TextMenu.Button("Allow Unsafe StartPos Restore: " + (AkronMapOverrides.ShouldAllowUnsafeSavestates(level) ? "On" : "Off")).Pressed(() => AkronActions.ToggleUnsafeNativeOverride(level)));
        menu.Add(new TextMenu.Button("Disable Everest-safe Block: " + (AkronMapOverrides.ShouldDisableEverestSafeBlock(level) ? "On" : "Off")).Pressed(() => AkronActions.ToggleEverestSafeBypass(level)));
    }
}

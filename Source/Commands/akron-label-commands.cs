using System;
using System.Globalization;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCommands {
    [Command("akron_hud_labels", "control custom HUD labels: on|off|status|outside on|off|preset <name>|select <n>|padding <n>|gap <n>|export [name]|import <file-or-name>|importlatest|visible on|off|anchor <name>|align left|center|right|font tiny|small|default|large|huge|absolute on|off|x <n>|y <n>|offset-x <n>|offset-y <n>|scale <n>|opacity <n>|line-spacing <n>|color <hex>|shadow on|off|shadow-opacity <n>|overlap off|fade|move|overlap-opacity <n>|overlap-padding <n>|move-anchor <name>|move-offset-x <n>|move-offset-y <n>|text <value>|name <value>")]
    public static void HudLabels(string action = "status", string value = "", string part2 = "", string part3 = "", string part4 = "", string part5 = "", string part6 = "") {
        string joined = JoinCommandText(value, part2, part3, part4, part5, part6);
        AkronCustomHudLabel label = AkronCustomHudLabels.GetActiveLabel();
        switch (NormalizeToken(action)) {
            case "":
            case "status":
                break;
            case "on":
                if (!AkronModule.TryUse(AkronFeatureKind.CustomHudLabels)) {
                    Log("custom-hud-labels: blocked");
                    return;
                }
                AkronModule.Settings.CustomHudLabels = true;
                break;
            case "off":
                AkronModule.Settings.CustomHudLabels = false;
                break;
            case "toggle":
                if (!AkronModule.Settings.CustomHudLabels && !AkronModule.TryUse(AkronFeatureKind.CustomHudLabels)) {
                    Log("custom-hud-labels: blocked");
                    return;
                }
                AkronModule.Settings.CustomHudLabels = !AkronModule.Settings.CustomHudLabels;
                break;
            case "outside":
                if (!TryParseBoolean(value, out bool outside)) {
                    Log("invalid custom-hud-label outside toggle: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelsInNonLevelScenes = outside;
                break;
            case "preset":
                AkronCustomHudLabels.AddPreset(NormalizeToken(value));
                break;
            case "select":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int selected)) {
                    Log("invalid label index: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelIndex = AkronModuleSettings.ClampCustomLabelIndex(selected - 1, AkronModule.Settings.CustomHudLabelDefinitions.Count);
                break;
            case "export":
                string exportedPath = AkronCustomHudLabels.Export();
                Log("custom-hud-label-export: " + exportedPath);
                break;
            case "import":
                if (string.IsNullOrWhiteSpace(joined)) {
                    Log("usage: akron_hud_labels import <file-or-name>");
                    return;
                }

                Log("custom-hud-label-imported: " + AkronCustomHudLabels.Import(joined).ToString(CultureInfo.InvariantCulture));
                break;
            case "importlatest":
                Log("custom-hud-label-imported: " + AkronCustomHudLabels.ImportLatest().ToString(CultureInfo.InvariantCulture));
                break;
            case "padding":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int padding)) {
                    Log("invalid label padding: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelPadding = AkronModuleSettings.ClampCustomLabelPadding(padding);
                break;
            case "gap":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gap)) {
                    Log("invalid label gap: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelGap = AkronModuleSettings.ClampCustomLabelGap(gap);
                break;
            case "visible":
                if (!TryParseBoolean(value, out bool visible)) {
                    Log("invalid label visible toggle: " + value);
                    return;
                }
                label.Visible = visible;
                break;
            case "anchor":
                if (!TryParseHudAnchor(value, out AkronHudAnchor anchor)) {
                    Log("invalid label anchor: " + value);
                    return;
                }
                label.Anchor = anchor;
                label.AbsolutePosition = anchor == AkronHudAnchor.Absolute;
                break;
            case "align":
                if (!TryParseLabelAlignment(value, out AkronLabelTextAlignment alignment)) {
                    Log("invalid label alignment: " + value);
                    return;
                }
                label.TextAlignment = alignment;
                break;
            case "font":
                if (!TryParseLabelFont(value, out AkronLabelFontTheme font)) {
                    Log("invalid label font: " + value);
                    return;
                }
                label.Font = font;
                break;
            case "absolute":
                if (!TryParseBoolean(value, out bool absolute)) {
                    Log("invalid label absolute toggle: " + value);
                    return;
                }
                label.AbsolutePosition = absolute;
                label.Anchor = absolute ? AkronHudAnchor.Absolute : AkronHudAnchor.TopLeft;
                break;
            case "x":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)) {
                    Log("invalid label x: " + value);
                    return;
                }
                label.X = Calc.Clamp(x, 0, 1920);
                break;
            case "y":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) {
                    Log("invalid label y: " + value);
                    return;
                }
                label.Y = Calc.Clamp(y, 0, 1080);
                break;
            case "offsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetX)) {
                    Log("invalid label offset-x: " + value);
                    return;
                }
                label.OffsetX = Calc.Clamp(offsetX, -1920, 1920);
                break;
            case "offsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int offsetY)) {
                    Log("invalid label offset-y: " + value);
                    return;
                }
                label.OffsetY = Calc.Clamp(offsetY, -1080, 1080);
                break;
            case "scale":
                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)) {
                    Log("invalid label scale: " + value);
                    return;
                }
                label.Scale = Calc.Clamp(scale, 0.2f, 1.5f);
                break;
            case "opacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int opacity)) {
                    Log("invalid label opacity: " + value);
                    return;
                }
                label.Opacity = AkronModuleSettings.ClampOpacity(opacity);
                break;
            case "linespacing":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineSpacing)) {
                    Log("invalid label line-spacing: " + value);
                    return;
                }
                label.LineSpacing = AkronModuleSettings.ClampCustomLabelLineSpacing(lineSpacing);
                break;
            case "color":
                if (!TryParseRgb(value, out int color)) {
                    Log("invalid label color: " + value);
                    return;
                }
                label.Color = color;
                break;
            case "shadow":
                if (!TryParseBoolean(value, out bool shadow)) {
                    Log("invalid label shadow toggle: " + value);
                    return;
                }
                label.Shadow = shadow;
                break;
            case "shadowopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int shadowOpacity)) {
                    Log("invalid label shadow opacity: " + value);
                    return;
                }
                label.ShadowOpacity = AkronModuleSettings.ClampOpacity(shadowOpacity);
                break;
            case "overlap":
                if (TryParseBoolean(value, out bool overlapToggle)) {
                    AkronModule.Settings.CustomHudLabelObstructionEnabled = overlapToggle;
                    break;
                }
                if (!TryParseLabelObstructionMode(value, out AkronLabelObstructionMode obstructionMode)) {
                    Log("invalid label overlap mode: " + value);
                    return;
                }
                if (obstructionMode == AkronLabelObstructionMode.Off) {
                    AkronModule.Settings.CustomHudLabelObstructionEnabled = false;
                } else {
                    AkronModule.Settings.CustomHudLabelObstructionMode = obstructionMode;
                }
                break;
            case "overlapenabled":
                if (!TryParseBoolean(value, out bool overlapEnabled)) {
                    Log("invalid label overlap enabled toggle: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructionEnabled = overlapEnabled;
                break;
            case "overlapopacity":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlapOpacity)) {
                    Log("invalid label overlap opacity: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructedOpacity = AkronModuleSettings.ClampOpacity(overlapOpacity);
                break;
            case "overlappadding":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlapPadding)) {
                    Log("invalid label overlap padding: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructionPaddingPixels = AkronModuleSettings.ClampCustomLabelObstructionPaddingPixels(overlapPadding);
                break;
            case "overlaponlycurrent":
            case "overlaponlyoverlapped":
                if (!TryParseBoolean(value, out bool onlyOverlapped)) {
                    Log("invalid label overlap scope toggle: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructionOnlyOverlappedLabel = onlyOverlapped;
                break;
            case "moveanchor":
                if (!TryParseHudAnchor(value, out AkronHudAnchor moveAnchor) || moveAnchor == AkronHudAnchor.Absolute) {
                    Log("invalid label move anchor: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructedAnchor = moveAnchor;
                break;
            case "moveoffsetx":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int moveOffsetX)) {
                    Log("invalid label move offset-x: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructedOffsetX = Calc.Clamp(moveOffsetX, -1920, 1920);
                break;
            case "moveoffsety":
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int moveOffsetY)) {
                    Log("invalid label move offset-y: " + value);
                    return;
                }
                AkronModule.Settings.CustomHudLabelObstructedOffsetY = Calc.Clamp(moveOffsetY, -1080, 1080);
                break;
            case "text":
                label.Text = joined;
                break;
            case "name":
                label.Name = string.IsNullOrWhiteSpace(joined) ? "Label" : joined;
                break;
            default:
                Log("unknown custom-hud-label action: " + action);
                return;
        }

        LogCustomHudLabelSettings();
    }

    private static void LogCustomHudLabelSettings() {
        AkronCustomHudLabel label = AkronCustomHudLabels.GetActiveLabel();
        Log("custom-hud-labels: " + AkronModule.Settings.CustomHudLabels.ToString().ToLowerInvariant());
        Log("custom-hud-labels-outside: " + AkronModule.Settings.CustomHudLabelsInNonLevelScenes.ToString().ToLowerInvariant());
        Log("custom-hud-label-count: " + AkronModule.Settings.CustomHudLabelDefinitions.Count.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-index: " + (AkronModule.Settings.CustomHudLabelIndex + 1).ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-padding: " + AkronModule.Settings.CustomHudLabelPadding.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-gap: " + AkronModule.Settings.CustomHudLabelGap.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-name: " + label.Name);
        Log("custom-hud-label-text: " + label.Text);
        Log("custom-hud-label-visible: " + label.Visible.ToString().ToLowerInvariant());
        Log("custom-hud-label-anchor: " + label.Anchor);
        Log("custom-hud-label-absolute: " + label.AbsolutePosition.ToString().ToLowerInvariant());
        Log("custom-hud-label-position: " + label.X.ToString(CultureInfo.InvariantCulture) + ", " + label.Y.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-offset: " + label.OffsetX.ToString(CultureInfo.InvariantCulture) + ", " + label.OffsetY.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-align: " + label.TextAlignment);
        Log("custom-hud-label-font: " + label.Font);
        Log("custom-hud-label-scale: " + label.Scale.ToString("0.##", CultureInfo.InvariantCulture));
        Log("custom-hud-label-opacity: " + label.Opacity.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-line-spacing: " + label.LineSpacing.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-color: " + FormatRgb(label.Color));
        Log("custom-hud-label-shadow: " + label.Shadow.ToString().ToLowerInvariant());
        Log("custom-hud-label-shadow-opacity: " + label.ShadowOpacity.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-event: " + label.EventMode);
        Log("custom-hud-label-event-style: " + label.EventOverridesStyle.ToString().ToLowerInvariant());
        Log("custom-hud-label-overlap: " + (AkronModule.Settings.CustomHudLabelObstructionEnabled ? "On" : "Off"));
        Log("custom-hud-label-overlap-mode: " + AkronModule.Settings.CustomHudLabelObstructionMode);
        Log("custom-hud-label-overlap-opacity: " + AkronModule.Settings.CustomHudLabelObstructedOpacity.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-overlap-padding: " + AkronModule.Settings.CustomHudLabelObstructionPaddingPixels.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-overlap-only-current: " + AkronModule.Settings.CustomHudLabelObstructionOnlyOverlappedLabel.ToString().ToLowerInvariant());
        Log("custom-hud-label-move-anchor: " + AkronModule.Settings.CustomHudLabelObstructedAnchor);
        Log("custom-hud-label-move-offset: " + AkronModule.Settings.CustomHudLabelObstructedOffsetX.ToString(CultureInfo.InvariantCulture) + ", " + AkronModule.Settings.CustomHudLabelObstructedOffsetY.ToString(CultureInfo.InvariantCulture));
        Log("custom-hud-label-variables: " + AkronCustomHudLabels.VariableCatalog());
    }

    private static bool TryParseLabelAlignment(string value, out AkronLabelTextAlignment alignment) {
        switch (NormalizeToken(value)) {
            case "left":
                alignment = AkronLabelTextAlignment.Left;
                return true;
            case "center":
            case "middle":
                alignment = AkronLabelTextAlignment.Center;
                return true;
            case "right":
                alignment = AkronLabelTextAlignment.Right;
                return true;
            default:
                alignment = AkronLabelTextAlignment.Left;
                return false;
        }
    }

    private static bool TryParseLabelFont(string value, out AkronLabelFontTheme font) {
        switch (NormalizeToken(value)) {
            case "tiny":
                font = AkronLabelFontTheme.Tiny;
                return true;
            case "small":
                font = AkronLabelFontTheme.Small;
                return true;
            case "default":
            case "normal":
                font = AkronLabelFontTheme.Default;
                return true;
            case "large":
                font = AkronLabelFontTheme.Large;
                return true;
            case "huge":
                font = AkronLabelFontTheme.Huge;
                return true;
            default:
                font = AkronLabelFontTheme.Default;
                return false;
        }
    }

    private static bool TryParseLabelObstructionMode(string value, out AkronLabelObstructionMode mode) {
        switch (NormalizeToken(value)) {
            case "off":
            case "none":
            case "disabled":
                mode = AkronLabelObstructionMode.Off;
                return true;
            case "fade":
            case "translucent":
            case "opacity":
                mode = AkronLabelObstructionMode.Fade;
                return true;
            case "move":
            case "relocate":
                mode = AkronLabelObstructionMode.Move;
                return true;
            default:
                mode = AkronLabelObstructionMode.Off;
                return false;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Celeste;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Monocle;

namespace Celeste.Mod.Akron;

public static partial class AkronCustomHudLabels {
    private const float GameWidth = 320f;
    private const float GameHeight = 180f;
    private static readonly Regex TemplateIdentifierPattern = new Regex("[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);
    private static readonly HashSet<string> LevelOnlyVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
        "chapter",
        "room",
        "map",
        "player_x",
        "player_y",
        "speed",
        "stamina",
        "dashes",
        "deaths",
        "room_deaths",
        "attempt",
        "room_time",
        "map_time",
        "hazard_accuracy",
        "hazard_invalid"
    };
    public static void Render(Level level, Player player, ref float y, float? screenWidth = null, float? screenHeight = null, bool? anyHudLabelObstructed = null) {
        if (!AkronModule.Settings.LabelSystemVisible ||
            !AkronModule.Settings.CustomHudLabels ||
            AkronModule.IsOverlayVisible ||
            !AkronModule.TryUse(AkronFeatureKind.CustomHudLabels)) {
            return;
        }

        if (!ActiveFontReady()) {
            return;
        }

        EnsureLabels();
        AkronHudRect? playerHudRect = ResolvePlayerHudRect(level, player);
        LabelLayout layout = new LabelLayout(
            AkronModuleSettings.ClampCustomLabelPadding(AkronModule.Settings.CustomHudLabelPadding),
            AkronModuleSettings.ClampCustomLabelGap(AkronModule.Settings.CustomHudLabelGap),
            screenWidth.GetValueOrDefault(ResolveHudScreenWidth()),
            screenHeight.GetValueOrDefault(ResolveHudScreenHeight()),
            y);
        bool hasLevel = level != null;
        List<LabelRenderPlan> plans = BuildRenderPlans(level, player, layout, hasLevel);
        bool anyLabelObstructed = anyHudLabelObstructed ?? plans.Any(plan => LabelIntersectsPlayer(plan.Position, plan.Size, playerHudRect));
        foreach (LabelRenderPlan plan in plans) {
            Vector2 position = plan.Position;
            LabelStyle style = plan.Style;
            bool labelObstructed = AkronModule.Settings.CustomHudLabelObstructionOnlyOverlappedLabel
                ? LabelIntersectsPlayer(plan.Position, plan.Size, playerHudRect)
                : anyLabelObstructed;
            ApplyObstructionBehavior(plan.Label, labelObstructed, plan.Size, layout, ref position, ref style);
            DrawAlignedText(plan.Label, plan.Text, position, style);
        }

        y = Math.Max(y, layout.TopLeftY);
    }

    internal static bool AnyRenderedLabelIntersectsPlayer(Level level, Player player, float y, float? screenWidth = null, float? screenHeight = null) {
        if (!AkronModule.Settings.LabelSystemVisible ||
            !AkronModule.Settings.CustomHudLabels ||
            AkronModule.IsOverlayVisible ||
            !AkronModule.TryUse(AkronFeatureKind.CustomHudLabels)) {
            return false;
        }

        EnsureLabels();
        AkronHudRect? playerHudRect = ResolvePlayerHudRect(level, player);
        LabelLayout layout = new LabelLayout(
            AkronModuleSettings.ClampCustomLabelPadding(AkronModule.Settings.CustomHudLabelPadding),
            AkronModuleSettings.ClampCustomLabelGap(AkronModule.Settings.CustomHudLabelGap),
            screenWidth.GetValueOrDefault(ResolveHudScreenWidth()),
            screenHeight.GetValueOrDefault(ResolveHudScreenHeight()),
            y);
        return BuildRenderPlans(level, player, layout, level != null)
            .Any(plan => LabelIntersectsPlayer(plan.Position, plan.Size, playerHudRect));
    }

    internal static float CalculateRenderedBottomY(Level level, Player player, float y, float? screenWidth = null, float? screenHeight = null) {
        if (!AkronModule.Settings.LabelSystemVisible ||
            !AkronModule.Settings.CustomHudLabels ||
            AkronModule.IsOverlayVisible ||
            !AkronModule.TryUse(AkronFeatureKind.CustomHudLabels)) {
            return y;
        }

        EnsureLabels();
        LabelLayout layout = new LabelLayout(
            AkronModuleSettings.ClampCustomLabelPadding(AkronModule.Settings.CustomHudLabelPadding),
            AkronModuleSettings.ClampCustomLabelGap(AkronModule.Settings.CustomHudLabelGap),
            screenWidth.GetValueOrDefault(ResolveHudScreenWidth()),
            screenHeight.GetValueOrDefault(ResolveHudScreenHeight()),
            y);
        BuildRenderPlans(level, player, layout, level != null);
        return Math.Max(y, layout.TopLeftY);
    }

    private static List<LabelRenderPlan> BuildRenderPlans(Level level, Player player, LabelLayout layout, bool hasLevel) {
        List<LabelRenderPlan> plans = new List<LabelRenderPlan>();
        foreach (AkronCustomHudLabel label in AkronModule.Settings.CustomHudLabelDefinitions) {
            if (!ShouldRender(label) || !CanRenderInScene(label, hasLevel)) {
                continue;
            }

            string text = Format(label.Text, level, player);
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            LabelStyle style = ResolveStyle(label);
            Vector2 size = MeasureAlignedText(label, text, style.Scale);
            AkronHudAnchor effectiveAnchor = ResolveResponsiveAnchor(label, hasLevel);
            Vector2 position = ResolvePosition(label, effectiveAnchor, size, layout);
            plans.Add(new LabelRenderPlan(label, text, position, size, style));
        }

        return plans;
    }

    public static string Format(string template, Level level, Player player) {
        Dictionary<string, string> variables = BuildVariables(level, player);
        string output = template ?? string.Empty;
        int guard = 0;
        while (guard++ < 128) {
            int start = output.IndexOf('{');
            if (start < 0) {
                return output;
            }

            int end = output.IndexOf('}', start + 1);
            if (end < 0) {
                return output;
            }

            string expression = output.Substring(start + 1, end - start - 1);
            string replacement = EvaluateExpression(expression, variables);
            output = output.Substring(0, start) + replacement + output.Substring(end + 1);
        }

        return output;
    }

    private static bool ShouldRender(AkronCustomHudLabel label) {
        if (label == null || !label.Visible) {
            return false;
        }

        float age = AkronModule.Session == null
            ? float.MaxValue
            : (Engine.FrameCounter - AkronModule.Session.LastDeathHitboxRecordedFrame) / 60f;
        return label.EventMode switch {
            AkronLabelEventMode.OnDeath => AkronEntityInspector.HasVisibleLastDeathHitbox() && age >= label.EventDelaySeconds && age <= label.EventDelaySeconds + label.EventDurationSeconds,
            AkronLabelEventMode.OnNoclipDeath => AkronEntityInspector.HasVisibleLastDeathHitbox() && string.Equals(AkronModule.Session?.LastDeathEntityType, "Noclip", StringComparison.OrdinalIgnoreCase),
            AkronLabelEventMode.OnButtonHold => Input.Jump.Check || Input.Dash.Check || Input.Grab.Check,
            _ => true
        };
    }

    private static Dictionary<string, string> BuildVariables(Level level, Player player) {
        AkronModuleSettings settings = AkronModule.Settings;
        AkronNoclipAccuracySnapshot hazardAccuracy = AkronModule.GetNoclipAccuracySnapshot();
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["app_version"] = AkronModule.Instance?.Metadata?.VersionString ?? "Akron",
            ["overlays"] = settings.DescribePresentationOverlays(),
            ["status"] = AkronModule.Session == null ? "No save" : AkronPolicy.GetLegitimacySensitiveStatusLabel(AkronModule.Session.AttemptStatus),
            ["reason"] = AkronModule.Session?.AttemptReason ?? string.Empty,
            ["chapter"] = level?.Session?.Area.GetSID() ?? "No map",
            ["map"] = level?.Session?.Area.GetSID() ?? "No map",
            ["room"] = level?.Session?.Level ?? "No room",
            ["player_x"] = player == null ? "-" : FormatNumber(player.Position.X, 0),
            ["player_y"] = player == null ? "-" : FormatNumber(player.Position.Y, 0),
            ["speed"] = player == null ? "-" : FormatNumber(player.Speed.Length(), 1),
            ["stamina"] = player == null ? "-" : FormatNumber(player.Stamina, 0),
            ["dashes"] = player == null ? "-" : player.Dashes.ToString(CultureInfo.InvariantCulture),
            ["deaths"] = level == null ? "0" : AkronHudRenderer.FormatHudNumber(AkronHudRenderer.GetCurrentMapDeathTotal(level)),
            ["room_deaths"] = level?.Session?.DeathsInCurrentLevel.ToString(CultureInfo.InvariantCulture) ?? "0",
            ["attempt"] = level == null ? "1" : AkronHudRenderer.FormatHudNumber(AkronHudRenderer.GetCurrentMapDeathTotal(level) + 1),
            ["room_time"] = level == null ? "00:00.000" : AkronHudRenderer.FormatHudTicks(AkronPracticeStats.GetCurrentRoomTime(level)),
            ["map_time"] = level == null ? "00:00.000" : AkronHudRenderer.FormatHudTicks(AkronPracticeStats.GetCurrentMapTime(level)),
            ["fps"] = Engine.Instance == null ? "-" : FormatNumber(1f / Math.Max(Engine.RawDeltaTime, 0.0001f), 0),
            ["tps"] = (AkronModule.Settings.TpsBypass ? AkronModuleSettings.ClampTpsTarget(AkronModule.Settings.TpsBypassTarget) : 60).ToString(CultureInfo.InvariantCulture),
            ["inputs"] = AkronInputHistory.FormatCurrentChord(),
            ["hazard_accuracy"] = hazardAccuracy.Accuracy.ToString("F2", CultureInfo.InvariantCulture),
            ["hazard_invalid"] = hazardAccuracy.InvalidEntries.ToString(CultureInfo.InvariantCulture),
            ["savestate_slot"] = settings.ActiveSavestateSlot.ToString(CultureInfo.InvariantCulture),
            ["tas"] = AkronInterop.CelesteTasLoaded ? "CelesteTAS" : "No TAS",
            ["speedrun_tool"] = AkronInterop.SpeedrunToolLoaded ? "Speedrun Tool" : "No SRT",
            ["clock"] = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)
        };

        values["cfg.safe_mode"] = settings.SafeMode ? "on" : "off";
        values["cfg.noclip"] = settings.Noclip ? "on" : "off";
        values["cfg.timescale"] = AkronModule.Session?.TimescaleMultiplier.ToString("0.0x", CultureInfo.InvariantCulture) ?? "1.0x";
        return values;
    }

    private static string EvaluateExpression(string expression, Dictionary<string, string> variables) {
        if (string.IsNullOrWhiteSpace(expression)) {
            return string.Empty;
        }

        string functionResult = EvaluateFunctionExpression(expression, variables);
        if (functionResult != null) {
            return functionResult;
        }

        string[] parts = expression.Split(':');
        if (parts.Length >= 4 && string.Equals(parts[0], "if", StringComparison.OrdinalIgnoreCase)) {
            return IsTruthy(Lookup(parts[1], variables)) ? parts[2] : parts[3];
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "upper", StringComparison.OrdinalIgnoreCase)) {
            return Lookup(parts[1], variables).ToUpperInvariant();
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "lower", StringComparison.OrdinalIgnoreCase)) {
            return Lookup(parts[1], variables).ToLowerInvariant();
        }

        if (parts.Length >= 2 && string.Equals(parts[0], "cfg", StringComparison.OrdinalIgnoreCase)) {
            return Lookup("cfg." + parts[1], variables);
        }

        if (parts.Length >= 3 && string.Equals(parts[0], "round", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(Lookup(parts[1], variables), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int decimals)) {
            return value.ToString("F" + ClampValue(decimals, 0, 4), CultureInfo.InvariantCulture);
        }

        return Lookup(expression, variables);
    }

    private static string EvaluateFunctionExpression(string expression, Dictionary<string, string> variables) {
        string trimmed = expression.Trim();
        int open = trimmed.IndexOf('(');
        if (open <= 0 || !trimmed.EndsWith(")", StringComparison.Ordinal)) {
            return null;
        }

        string name = trimmed.Substring(0, open).Trim();
        string[] args = trimmed.Substring(open + 1, trimmed.Length - open - 2)
            .Split(',')
            .Select(arg => arg.Trim())
            .ToArray();
        if (args.Length == 0) {
            return string.Empty;
        }

        string first = Lookup(args[0], variables);
        if (string.Equals(name, "round", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float roundValue)) {
            return Math.Round(roundValue).ToString(CultureInfo.InvariantCulture);
        }

        if (string.Equals(name, "precision", StringComparison.OrdinalIgnoreCase) &&
            args.Length >= 2 &&
            float.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out float precisionValue) &&
            int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int decimals)) {
            return precisionValue.ToString("F" + ClampValue(decimals, 0, 4), CultureInfo.InvariantCulture);
        }

        if (string.Equals(name, "upper", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "toUpper", StringComparison.OrdinalIgnoreCase)) {
            return first.ToUpperInvariant();
        }

        if (string.Equals(name, "lower", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "toLower", StringComparison.OrdinalIgnoreCase)) {
            return first.ToLowerInvariant();
        }

        return null;
    }

    private static string Lookup(string key, Dictionary<string, string> variables) {
        return variables.TryGetValue((key ?? string.Empty).Trim(), out string value) ? value : string.Empty;
    }

    private static bool IsTruthy(string value) {
        return !string.IsNullOrWhiteSpace(value) &&
               !string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(value, "-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanRenderInScene(AkronCustomHudLabel label, bool hasLevel) {
        return label != null &&
               (hasLevel ||
                label.AbsolutePosition ||
                label.Anchor == AkronHudAnchor.Absolute ||
                !UsesLevelOnlyVariable(label.Text));
    }

    private static bool UsesLevelOnlyVariable(string template) {
        if (string.IsNullOrWhiteSpace(template)) {
            return false;
        }

        foreach (Match match in TemplateIdentifierPattern.Matches(template)) {
            if (LevelOnlyVariables.Contains(match.Value)) {
                return true;
            }
        }

        return false;
    }

    private static AkronHudAnchor ResolveResponsiveAnchor(AkronCustomHudLabel label, bool hasLevel) {
        if (hasLevel ||
            label.AbsolutePosition ||
            label.Anchor == AkronHudAnchor.Absolute) {
            return label.Anchor;
        }

        return AkronHudAnchor.TopLeft;
    }

    private static Vector2 ResolvePosition(AkronCustomHudLabel label, AkronHudAnchor anchor, Vector2 size, LabelLayout layout) {
        if (label.AbsolutePosition || label.Anchor == AkronHudAnchor.Absolute) {
            Vector2 absolute = new Vector2(label.X + label.OffsetX, label.Y + label.OffsetY);
            if (label.TextAlignment == AkronLabelTextAlignment.Center) {
                absolute.X -= size.X / 2f;
            } else if (label.TextAlignment == AkronLabelTextAlignment.Right) {
                absolute.X -= size.X;
            }

            return absolute;
        }

        return layout.Next(anchor, size) + new Vector2(label.OffsetX, label.OffsetY);
    }

    private static bool LabelIntersectsPlayer(Vector2 position, Vector2 size, AkronHudRect? playerHudRect) {
        AkronModuleSettings settings = AkronModule.Settings;
        return settings != null &&
               settings.CustomHudLabelObstructionEnabled &&
               playerHudRect.HasValue &&
               PlayerIntersectsLabelResponseArea(
                   position.X,
                   position.Y,
                   size.X,
                   size.Y,
                   playerHudRect.Value.X,
                   playerHudRect.Value.Y,
                   playerHudRect.Value.Width,
                   playerHudRect.Value.Height,
                   settings.CustomHudLabelObstructionPaddingPixels);
    }

    private static void ApplyObstructionBehavior(AkronCustomHudLabel label, bool obstructed, Vector2 size, LabelLayout layout, ref Vector2 position, ref LabelStyle style) {
        AkronModuleSettings settings = AkronModule.Settings;
        if (label == null || settings == null || !settings.CustomHudLabelObstructionEnabled || !obstructed) {
            return;
        }

        if (settings.CustomHudLabelObstructionMode == AkronLabelObstructionMode.Move) {
            position = layout.PositionForAnchor(AkronModuleSettings.NormalizeCustomLabelObstructedAnchor(settings.CustomHudLabelObstructedAnchor), size) +
                       new Vector2(settings.CustomHudLabelObstructedOffsetX, settings.CustomHudLabelObstructedOffsetY);
            return;
        }

        int opacity = Math.Min(
            AkronModuleSettings.ClampOpacity(settings.CustomHudLabelObstructedOpacity),
            (int) Math.Round(style.OpacityPercent));
        style = style.WithOpacity(opacity);
    }

    private sealed class LabelRenderPlan {
        public LabelRenderPlan(AkronCustomHudLabel label, string text, Vector2 position, Vector2 size, LabelStyle style) {
            Label = label;
            Text = text;
            Position = position;
            Size = size;
            Style = style;
        }

        public AkronCustomHudLabel Label { get; }
        public string Text { get; }
        public Vector2 Position { get; }
        public Vector2 Size { get; }
        public LabelStyle Style { get; }
    }

    public static bool PlayerIntersectsLabelResponseArea(
        float labelX,
        float labelY,
        float labelWidth,
        float labelHeight,
        float playerX,
        float playerY,
        float playerWidth,
        float playerHeight,
        int paddingPixels) {
        if (labelWidth <= 0f || labelHeight <= 0f || playerWidth <= 0f || playerHeight <= 0f) {
            return false;
        }

        int clampedPadding = AkronModuleSettings.ClampCustomLabelObstructionPaddingPixels(paddingPixels);
        float paddingX = clampedPadding;
        float paddingY = clampedPadding;
        return RectanglesIntersect(
            labelX - paddingX,
            labelY - paddingY,
            labelWidth + paddingX * 2f,
            labelHeight + paddingY * 2f,
            playerX,
            playerY,
            playerWidth,
            playerHeight);
    }

    private static bool RectanglesIntersect(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) {
        return ax < bx + bw &&
               ax + aw > bx &&
               ay < by + bh &&
               ay + ah > by;
    }

    private static void DrawAlignedText(AkronCustomHudLabel label, string text, Vector2 position, LabelStyle style) {
        string[] lines = SplitLines(text);
        if (lines.Length > 1) {
            DrawAlignedTextLines(label, lines, position, style);
            return;
        }

        if (label.Shadow && style.ShadowOpacity > 0) {
            Color shadow = ColorFromRgb(label.ShadowColor) * (style.ShadowOpacity / 100f);
            ActiveFont.Draw(text, position + new Vector2(label.ShadowOffsetX, label.ShadowOffsetY), Vector2.Zero, Vector2.One * style.Scale, shadow);
            ActiveFont.DrawOutline(text, position, Vector2.Zero, Vector2.One * style.Scale, style.Color, 2f, shadow);
            return;
        }

        ActiveFont.Draw(text, position, Vector2.Zero, Vector2.One * style.Scale, style.Color);
    }

    private static void DrawAlignedTextLines(AkronCustomHudLabel label, IReadOnlyList<string> lines, Vector2 position, LabelStyle style) {
        Vector2 totalSize = MeasureAlignedText(label, string.Join("\n", lines), style.Scale);
        float lineHeight = LineHeight(label, style.Scale);
        Color shadow = ColorFromRgb(label.ShadowColor) * (style.ShadowOpacity / 100f);
        for (int index = 0; index < lines.Count; index++) {
            string line = lines[index];
            float lineWidth = ActiveFont.Measure(line).X * style.Scale;
            float x = position.X;
            if (label.TextAlignment == AkronLabelTextAlignment.Center) {
                x += (totalSize.X - lineWidth) * 0.5f;
            } else if (label.TextAlignment == AkronLabelTextAlignment.Right) {
                x += totalSize.X - lineWidth;
            }

            Vector2 linePosition = new Vector2(x, position.Y + index * lineHeight);
            if (label.Shadow && style.ShadowOpacity > 0) {
                ActiveFont.Draw(line, linePosition + new Vector2(label.ShadowOffsetX, label.ShadowOffsetY), Vector2.Zero, Vector2.One * style.Scale, shadow);
                ActiveFont.DrawOutline(line, linePosition, Vector2.Zero, Vector2.One * style.Scale, style.Color, 2f, shadow);
            } else {
                ActiveFont.Draw(line, linePosition, Vector2.Zero, Vector2.One * style.Scale, style.Color);
            }
        }
    }

    private static Vector2 MeasureAlignedText(AkronCustomHudLabel label, string text, float scale) {
        string[] lines = SplitLines(text);
        if (lines.Length <= 1) {
            return ActiveFont.Measure(text) * scale;
        }

        float width = 0f;
        foreach (string line in lines) {
            width = Math.Max(width, ActiveFont.Measure(line).X * scale);
        }

        float lineHeight = LineHeight(label, scale);
        return new Vector2(width, lineHeight * lines.Length);
    }

    private static float LineHeight(AkronCustomHudLabel label, float scale) {
        float multiplier = AkronModuleSettings.ClampCustomLabelLineSpacing(label?.LineSpacing ?? 100) / 100f;
        return ActiveFont.Measure("Ag").Y * scale * multiplier;
    }

    private static string[] SplitLines(string text) {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
    }

    private static bool ActiveFontReady() {
        try {
            ActiveFont.Measure("Ag");
            return true;
        } catch (Exception) {
            return false;
        }
    }

    private static LabelStyle ResolveStyle(AkronCustomHudLabel label) {
        float fontScale = label.Font switch {
            AkronLabelFontTheme.Tiny => 0.24f,
            AkronLabelFontTheme.Small => 0.32f,
            AkronLabelFontTheme.Large => 0.55f,
            AkronLabelFontTheme.Huge => 0.72f,
            _ => label.Scale
        };
        float scale = ClampScale(label.EventOverridesStyle && IsEventActive(label) ? label.EventScale : fontScale);
        int color = label.EventOverridesStyle && IsEventActive(label) ? label.EventColor : label.Color;
        int opacity = label.EventOverridesStyle && IsEventActive(label) ? label.EventOpacity : label.Opacity;
        return new LabelStyle(
            scale,
            ColorFromRgb(color) * (AkronModuleSettings.ClampOpacity(opacity) / 100f),
            AkronModuleSettings.ClampOpacity(label.ShadowOpacity),
            AkronModuleSettings.ClampOpacity(opacity));
    }

    private static AkronHudRect? ResolvePlayerHudRect(Level level, Player player) {
        if (level == null || player == null) {
            return null;
        }

        Rectangle playerBounds = new Rectangle(
            (int) Math.Floor(player.Position.X - 4f),
            (int) Math.Floor(player.Position.Y - 11f),
            8,
            11);
        return AkronScreenProjection.WorldToHudRect(level, playerBounds);
    }

    private static bool IsEventActive(AkronCustomHudLabel label) {
        if (label == null || label.EventMode == AkronLabelEventMode.Always) {
            return false;
        }

        float age = AkronModule.Session == null
            ? float.MaxValue
            : (Engine.FrameCounter - AkronModule.Session.LastDeathHitboxRecordedFrame) / 60f;
        return label.EventMode switch {
            AkronLabelEventMode.OnDeath => AkronEntityInspector.HasVisibleLastDeathHitbox() && age >= label.EventDelaySeconds && age <= label.EventDelaySeconds + label.EventDurationSeconds,
            AkronLabelEventMode.OnNoclipDeath => AkronEntityInspector.HasVisibleLastDeathHitbox() && string.Equals(AkronModule.Session?.LastDeathEntityType, "Noclip", StringComparison.OrdinalIgnoreCase),
            AkronLabelEventMode.OnButtonHold => Input.Jump.Check || Input.Dash.Check || Input.Grab.Check,
            _ => false
        };
    }

    private static float ClampScale(float scale) {
        return scale <= 0f ? 0.42f : ClampValue(scale, 0.20f, 1.50f);
    }

    private static int ClampValue(int value, int minimum, int maximum) {
        if (value < minimum) {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static float ClampValue(float value, float minimum, float maximum) {
        if (value < minimum) {
            return minimum;
        }

        return value > maximum ? maximum : value;
    }

    private static string FormatNumber(float value, int decimals) {
        return value.ToString("F" + decimals, CultureInfo.InvariantCulture);
    }

    private static Color ColorFromRgb(int rgb) {
        return new Color((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
    }

    private static float ResolveHudScreenWidth() {
        return ResolveHudViewportSize().X;
    }

    private static float ResolveHudScreenHeight() {
        return ResolveHudViewportSize().Y;
    }

    private static Vector2 ResolveHudViewportSize() {
        Viewport engineViewport = Engine.Viewport;
        float width = engineViewport.Width;
        float height = engineViewport.Height;

        if (width <= 0f || height <= 0f) {
            width = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferWidth ?? 1280f;
            height = Engine.Instance?.GraphicsDevice?.PresentationParameters.BackBufferHeight ?? 720f;
        }

        float scale = MathHelper.Min(width / GameWidth, height / GameHeight);
        if (scale <= 0f) {
            return new Vector2(1280f, 720f);
        }

        return new Vector2(GameWidth * scale, GameHeight * scale);
    }

    private sealed class LabelLayout {
        private readonly int padding;
        private readonly int gap;
        private readonly float screenWidth;
        private readonly float screenHeight;
        private readonly Dictionary<AkronHudAnchor, float> cursors = new Dictionary<AkronHudAnchor, float>();
        private readonly float topStartY;

        public LabelLayout(int padding, int gap, float screenWidth, float screenHeight, float occupiedTopLeftY) {
            this.padding = padding;
            this.gap = gap;
            this.screenWidth = screenWidth;
            this.screenHeight = screenHeight;
            topStartY = Math.Max(padding, occupiedTopLeftY);
            TopLeftY = topStartY;
        }

        public float TopLeftY { get; private set; }

        public Vector2 Next(AkronHudAnchor anchor, Vector2 size) {
            float cursor = cursors.TryGetValue(anchor, out float existing) ? existing : 0f;
            Vector2 position = PositionForAnchor(anchor, size, cursor);
            float advance = size.Y + gap;
            if (anchor == AkronHudAnchor.TopLeft || anchor == AkronHudAnchor.Absolute) {
                TopLeftY += advance;
            }
            cursors[anchor] = cursor + advance;

            return position;
        }

        public Vector2 PositionForAnchor(AkronHudAnchor anchor, Vector2 size) {
            float cursor = cursors.TryGetValue(anchor, out float existing) ? existing : 0f;
            return PositionForAnchor(anchor, size, cursor);
        }

        private Vector2 PositionForAnchor(AkronHudAnchor anchor, Vector2 size, float cursor) {
            return anchor switch {
                AkronHudAnchor.TopCenter => new Vector2(screenWidth / 2f - size.X / 2f, padding + cursor),
                AkronHudAnchor.TopRight => new Vector2(screenWidth - padding - size.X, padding + cursor),
                AkronHudAnchor.MiddleLeft => new Vector2(padding, screenHeight / 2f - size.Y / 2f + cursor),
                AkronHudAnchor.Center => new Vector2(screenWidth / 2f - size.X / 2f, screenHeight / 2f - size.Y / 2f + cursor),
                AkronHudAnchor.MiddleRight => new Vector2(screenWidth - padding - size.X, screenHeight / 2f - size.Y / 2f + cursor),
                AkronHudAnchor.BottomLeft => new Vector2(padding, screenHeight - padding - size.Y - cursor),
                AkronHudAnchor.BottomCenter => new Vector2(screenWidth / 2f - size.X / 2f, screenHeight - padding - size.Y - cursor),
                AkronHudAnchor.BottomRight => new Vector2(screenWidth - padding - size.X, screenHeight - padding - size.Y - cursor),
                _ => new Vector2(padding, topStartY + cursor)
            };
        }
    }

    private readonly struct LabelStyle {
        public LabelStyle(float scale, Color color, int shadowOpacity, float opacityPercent) {
            Scale = scale;
            Color = color;
            ShadowOpacity = shadowOpacity;
            OpacityPercent = opacityPercent;
        }

        public float Scale { get; }
        public Color Color { get; }
        public int ShadowOpacity { get; }
        public float OpacityPercent { get; }

        public LabelStyle WithOpacity(int opacity) {
            float clampedOpacity = AkronModuleSettings.ClampOpacity(opacity);
            float alphaScale = OpacityPercent <= 0f ? 0f : clampedOpacity / OpacityPercent;
            int shadowOpacity = Math.Min(ShadowOpacity, (int) Math.Round(clampedOpacity));
            return new LabelStyle(Scale, Color * alphaScale, shadowOpacity, clampedOpacity);
        }
    }
}

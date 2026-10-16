using System;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace Celeste.Mod.Akron;

public sealed partial class AkronOverlay
{
    private float sectionScrollOffset;
    private NumericsVector2 sectionScrollDisplaySize;
    private string sectionScrollQuery;

    private void ResetSectionNavigation()
    {
        sectionScrollOffset = 0f;
        sectionScrollDisplaySize = NumericsVector2.Zero;
        sectionScrollQuery = null;
    }

    internal static float GetSectionMinimumVisibleHeight(bool collapsed, float rowHeight)
    {
        return collapsed ? HeaderHeight : HeaderHeight + BodyPadding * 2f + rowHeight;
    }

    internal static float CalculateSectionScrollMaximum(float contentBottom, float displayHeight, float navigationHeight)
    {
        if (!float.IsFinite(contentBottom) || !float.IsFinite(displayHeight) || displayHeight <= 0f)
            return 0f;
        if (contentBottom <= displayHeight)
            return 0f;
        return Math.Max(0f, contentBottom - Math.Max(1f, displayHeight - navigationHeight));
    }

    internal static float ClampSectionScroll(float offset, float maximum)
    {
        return float.IsFinite(offset) ? Math.Clamp(offset, 0f, Math.Max(0f, maximum)) : 0f;
    }

    private void DrawSectionNavigation(float width, float height, float navigationHeight, float maximumScroll)
    {
        float scale = CurrentOverlayScale();
        ImGui.SetNextWindowPos(new NumericsVector2(4f * scale, Math.Max(0f, height - navigationHeight) * scale), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new NumericsVector2(Math.Max(1f, width - 8f) * scale, navigationHeight * scale), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(1f);
        ImGui.Begin("Sections##akron_section_navigation", ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing);
        bool blocked = IsBackgroundActionRowInputBlocked() || HasActiveBindingCapture ||
            ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId);
        ImGui.BeginDisabled(blocked);
        float step = Math.Max(80f, (height - navigationHeight) * 0.75f);
        if (ImGui.Button("Top##sections", new NumericsVector2(48f * scale, 0f)))
            sectionScrollOffset = 0f;
        ImGui.SameLine();
        if (ImGui.Button("Up##sections", new NumericsVector2(48f * scale, 0f)))
            sectionScrollOffset = ClampSectionScroll(sectionScrollOffset - step, maximumScroll);
        ImGui.SameLine();
        if (ImGui.Button("Down##sections", new NumericsVector2(60f * scale, 0f)))
            sectionScrollOffset = ClampSectionScroll(sectionScrollOffset + step, maximumScroll);
        if (width > 340f)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted("Scroll sections");
        }
        ImGui.EndDisabled();
        ImGui.End();
    }
}

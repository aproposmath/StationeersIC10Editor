namespace StationeersIC10Editor;

using System;

using ImGuiNET;

using UnityEngine;

public readonly struct ScopedStyleVar : IDisposable
{
    private readonly int Count = 0;
    public ScopedStyleVar(ImGuiStyleVar obj, float value, bool apply = true)
    {
        Count = apply ? 1 : 0;
        if (Count > 0)
            ImGui.PushStyleVar(obj, value);
    }

    public void Dispose()
    {
        for (var i = 0; i < Count; i++)
            ImGui.PopStyleVar();
    }
}

public readonly struct ScopedFont : IDisposable
{
    private readonly bool Apply;
    private readonly float FontScale;
    public ScopedFont(ImFontPtr font, float fontScale = -1f, bool apply = true)
    {
        Apply = apply;
        if (Apply)
        {
            FontScale = -1.0f;
            if (fontScale != -1f)
            {
                // FontScale = ImGui.GetIO().FontGlobalScale;
                // ImGui.SetWindowFontScale(fontScale);
            }
            ImGui.PushFont(font);
        }
    }

    public void Dispose()
    {
        if (Apply)
        {
            // if (FontScale != -1.0f)
            //     ImGui.GetIO().FontGlobalScale = FontScale;
            ImGui.PopFont();
        }
    }
}

public readonly struct ScopedItemWidth : IDisposable
{
    private readonly bool Apply;
    public ScopedItemWidth(float width, bool apply = true)
    {
        Apply = apply;
        if (apply)
            ImGui.PushItemWidth(width);
    }

    public void Dispose()
    {
        if (Apply)
            ImGui.PopItemWidth();
    }
}

public readonly struct ScopedStyleColor : IDisposable
{
    private readonly int Count = 0;

    public ScopedStyleColor(ImGuiCol obj, uint color, bool apply = true)
    {
        Count = apply ? 1 : 0;
        if (Count > 0)
            ImGui.PushStyleColor(obj, color);
    }

    public ScopedStyleColor(ImGuiCol[] objs, uint color, bool apply = true)
    {
        Count = apply ? objs.Length : 0;
        if (Count > 0)
        {
            foreach (var obj in objs)
                ImGui.PushStyleColor(obj, color);
        }
    }

    public ScopedStyleColor(ImGuiCol[] objs, uint[] colors, bool apply = true)
    {
        Count = apply ? objs.Length : 0;
        for (var i = 0; i < Count; i++)
            ImGui.PushStyleColor(objs[i], colors[i]);
    }

    public void Dispose()
    {
        for (var i = 0; i < Count; i++)
            ImGui.PopStyleColor();
    }
}

public readonly struct ScopedItemFlag : IDisposable
{
    private readonly int Count = 0;

    public ScopedItemFlag(ImGuiItemFlags flag, bool value, bool apply = true)
    {
        Count = apply ? 1 : 0;
        if (Count > 0)
            ImGui.PushItemFlag(flag, value);
    }

    public ScopedItemFlag(ImGuiItemFlags[] flags, bool[] values, bool apply = true)
    {
        Count = apply ? flags.Length : 0;
        for (var i = 0; i < Count; i++)
            ImGui.PushItemFlag(flags[i], values[i]);
    }

    public void Dispose()
    {
        for (var i = 0; i < Count; i++)
            ImGui.PopItemFlag();
    }
}

public readonly struct Pane : IDisposable
{
    public Pane(string name, float widthFraction = 1.0f, float footerSize = -1.0f, bool border = true)
    {
        if (footerSize < 0.0f)
            footerSize = ImGui.GetStyle().FramePadding.y * 2 + Settings.LineHeight;
        var avail = ImGui.GetContentRegionAvail();
        var width = avail.x * widthFraction - ImGui.GetStyle().FramePadding.x * 1;
        var height = avail.y - ImGui.GetStyle().FramePadding.y * 2 - footerSize;
        ImGui.BeginChild(name, new Vector2(width, height), border);
    }

    public void Dispose()
    {
        ImGui.EndChild();
    }
}

public class ImGuiUtils
{
    public static bool Button(string label, Vector2 size = default, string tooltip = null, bool disabled = false)
    {
        // using var _df = new ScopedItemFlag(ImGuiItemFlags.Disabled, disabled, disabled);
        using var _ = new ScopedStyleColor([ImGuiCol.Button, ImGuiCol.ButtonHovered, ImGuiCol.ButtonActive], ICodeFormatter.ColorFromHTML("gray"), disabled);
        var pressed = ImGui.Button(label, size) && !disabled;

        if (string.IsNullOrEmpty(tooltip) == false && ImGui.IsItemHovered() && Settings.ShowTooltip)
            ImGui.SetTooltip(tooltip);

        return pressed;
    }

    public static bool InputFloat(string label, ref float value, float width = -1.0f)
    {
        using var _ = new ScopedItemWidth(width, width != -1.0f);
        return ImGui.InputFloat(label, ref value);
    }

    public static bool InputInt(string label, ref int value, float width = -1.0f, int min = int.MinValue, int max = int.MaxValue)
    {
        using var _ = new ScopedItemWidth(width, width != -1.0f);
        return ImGui.InputInt(label, ref value, min, max);
    }

    public static void Text(string label, float width = -1.0f)
    {
        using var _ = new ScopedItemWidth(width, width != -1.0f);
        ImGui.Text(label);
    }

    public static uint Color(string name)
    {
        return ICodeFormatter.ColorFromHTML(name);
    }

    public static uint Color(Vector4 color)
    {
        return ICodeFormatter.ColorFromVector4(color);
    }

    public static uint Color(float r, float g, float b, float a = 1.0f)
    {
        return ICodeFormatter.ColorFromVector4(r, g, b, a);
    }

    public static uint Color(uint r, uint g, uint b, uint a = 255)
    {
        return (a << 24) | (b << 16) | (g << 8) | r;
    }

}

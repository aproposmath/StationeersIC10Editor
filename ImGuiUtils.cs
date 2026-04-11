namespace StationeersIC10Editor;

using System;

using BepInEx.Configuration;

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

public readonly struct ScopedChild : IDisposable
{
    public ScopedChild(string id) { ImGui.BeginChild(id); }
    public void Dispose() { ImGui.EndChild(); }
}

public readonly struct ScopedFont : IDisposable
{
    private readonly bool Apply;
    public ScopedFont(ImFontPtr font, bool apply = true)
    {
        Apply = apply;
        if (Apply)
            ImGui.PushFont(font);
    }

    public void Dispose()
    {
        if (Apply)
            ImGui.PopFont();
    }
}

public readonly struct ScopedFontScale : IDisposable
{
    private readonly bool Apply;
    private readonly float OldScale;
    public ScopedFontScale(float scale, bool apply = true)
    {
        Apply = apply;
        if (Apply)
        {
            var io = ImGui.GetIO();
            OldScale = io.FontGlobalScale;
            io.FontGlobalScale = scale;
        }
    }

    public void Dispose()
    {
        if (Apply)
            ImGui.GetIO().FontGlobalScale = OldScale;
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
    public Pane(string name, float widthFraction = 1.0f, float footerSize = 0.0f, bool border = true)
    {
        if (footerSize < 0.0f) // skip -footerSize*lineHeight
            footerSize = -footerSize * Settings.LineHeightWithSpacing;
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
    public static bool Checkbox(string label, ref bool value, string tooltip = null)
    {
        var pressed = ImGui.Checkbox(label, ref value);

        if (!string.IsNullOrEmpty(tooltip) && ImGui.IsItemHovered() && Settings.ShowTooltip)
            ImGui.SetTooltip(tooltip);

        return pressed;
    }

    public static bool Button(string label, Vector2 size = default, string tooltip = null, bool disabled = false)
    {
        using var _ = new ScopedStyleColor([ImGuiCol.Button, ImGuiCol.ButtonHovered, ImGuiCol.ButtonActive], ICodeFormatter.ColorFromHTML("gray"), disabled);
        var pressed = ImGui.Button(label, size) && !disabled;

        if (string.IsNullOrEmpty(tooltip) == false && ImGui.IsItemHovered() && Settings.ShowTooltip)
            ImGui.SetTooltip(tooltip);

        return pressed;
    }

    public static bool InputText(string id, ref string value, float width = -1.0f, uint bufferSize = 256)
    {
        using var _ = new ScopedItemWidth(width, width != -1.0f);
        return ImGui.InputText(id, ref value, bufferSize, ImGuiInputTextFlags.EnterReturnsTrue);
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

    public static class Config
    {
        private struct ConfigHelper : IDisposable
        {
            ConfigEntryBase _entry;

            public ConfigHelper(ConfigEntryBase entry)
            {
                _entry = entry;
                if (Button($"D##{_entry.Definition.Key}", new Vector2(), "Reset to default"))
                {
                    L.Debug($"Resetting {_entry.BoxedValue} to default ({_entry.DefaultValue})");
                    _entry.BoxedValue = _entry.DefaultValue;
                }
                ImGui.SameLine();
            }

            public readonly void Dispose()
            {
                if (ImGui.IsItemHovered() && Settings.ShowTooltip)
                    ImGui.SetTooltip(_entry.Description.Description);
            }
        }

        public static bool Bool(string label, ConfigEntry<bool> entry)
        {
            using var _ = new ConfigHelper(entry);
            var value = entry.Value;
            if (ImGui.Checkbox(label, ref value))
            {
                entry.BoxedValue = value;
                return true;
            }
            return false;
        }

        public static float FloatOptionWidth => ImGui.CalcTextSize("000000.00").x;

        public static void Float(string label, ConfigEntry<float> entry, float min, float max)
        {
            using var _ = new ConfigHelper(entry);
            var value = entry.Value;

            if (InputFloat(label, ref value, FloatOptionWidth))
                entry.BoxedValue = value;
        }

        public static void Float(string label, ConfigEntry<float> entry)
        {
            using var _ = new ConfigHelper(entry);
            var value = entry.Value;
            if (InputFloat(label, ref value, FloatOptionWidth))
                entry.BoxedValue = value;
        }

        public static void Int(string label, ConfigEntry<int> entry)
        {
            using var _ = new ConfigHelper(entry);
            var value = entry.Value;
            if (InputInt(label, ref value, FloatOptionWidth, -20, 20))
                entry.BoxedValue = value;
        }

        public static void Char(string label, ConfigEntry<string> entry)
        {
            using var _ = new ConfigHelper(entry);
            var value = $"{entry.Value}";
            if (InputText(label, ref value, 2 * Settings.CharWidth))
                entry.BoxedValue = value.Length > 0 ? $"{value[0]}" : entry.DefaultValue;
        }

    }

}

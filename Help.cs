namespace StationeersIC10Editor;

using System.Collections.Generic;
using System.Diagnostics;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;

public static class HelpWindow
{
    public static bool IsOpen = false;
    private static int _tabIndex = 0;

    private static ImGuiTabItemFlags _TabFlags(int i)
    {
        return i == _tabIndex ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
    }

    public static void Draw()
    {
        if (!IsOpen)
            return;

        using var _ = new ScopedStyleColor(ImGuiCol.WindowBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.2f, 1.0f));
        ImGui.SetNextWindowSize(Scale * new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin(
            "IC10 Editor Help",
            ref IsOpen,
            ImGuiWindowFlags.NoSavedSettings
        );

        if (ImGui.BeginTabBar("EditorTabs"))
        {
            if (ImGui.BeginTabItem("Help", _TabFlags(0)))
            {
                using var _h = new ScopedChild("");
                ImGui.TextWrapped(
                    "This is the IC10 Editor. It allows you to edit the source code of IC10 programs with syntax highlighting, undo/redo, and other features."
                );

                ImGui.Text("");
                ImGui.TextColored(new Vector4(1, 1, 1, 1), "Hints:");
                ImGui.Text("");
                ImGui.TextWrapped("- Click on 'Commit' in the Library window to save a version/snapshot of all scripts.");
                ImGui.TextWrapped("- Right-click on file names/folders in the Library window for actions.");
                ImGui.TextWrapped("- Files in folders are still kept at the original path on disk (the path is encoded in the Title using '|' as a separator).");


                if (Button("Native", buttonSize, "Switch to the native Stationeers IC10 editor."))
                    LibraryWindow.Window.SwitchToNativeEditor();

                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _tabIndex = 0;

            if (ImGui.BeginTabItem("Config", _TabFlags(1)))
            {
                DrawConfig();
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _tabIndex = 1;

            if (ImGui.BeginTabItem("Keybindings", _TabFlags(2)))
            {
                DrawKeybindings();
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _tabIndex = 2;

            if (ImGui.BeginTabItem("VIM", _TabFlags(3)))
            {
                DrawVIM();
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _tabIndex = 3;

        }
        ImGui.EndTabBar();

        ImGui.End();
    }

    public static void DrawConfig()
    {
        using var _ = new ScopedChild("");
        ImGui.Text("\nConfiguration:");
        Config.Bool("Pause Game on Open", IC10EditorPlugin.PauseOnOpen);
        Config.Bool("Collapse when other window is open", IC10EditorPlugin.CollapseOnGameWindow);
        Config.Bool("Enforce 90 Characters per Line Limit", IC10EditorPlugin.EnforceLineLengthLimit);
        Config.Bool("Enforce 128 Lines Limit", IC10EditorPlugin.EnforceLineLimit);
        Config.Bool("Enforce 4096 Bytes Limit", IC10EditorPlugin.EnforceByteLimit);
        Config.Float("UI Scaling", IC10EditorPlugin.ScaleFactor, 0.25f, 5.0f);
        Config.Int("Line Spacing Offset", IC10EditorPlugin.LineSpacingOffset);
        Config.Float("Toolitp delay (ms)", IC10EditorPlugin.TooltipDelay);
        if (Config.Bool("VIM bindings enabled", IC10EditorPlugin.VimBindings) && !VimEnabled)
            LibraryWindow.Window.ActiveEditor.KeyHandler.Mode = KeyMode.Insert;
        Config.Bool(
            "Auto Completion (insert with Tab key)",
            IC10EditorPlugin.EnableAutoComplete
        );
        Config.Bool("Relative line numbers", IC10EditorPlugin.RelativeLineNumbers);
        Config.Bool("Apply patch to keep selected IC10 in computer (Experimental)", IC10EditorPlugin.RestoreSelectedHousing);
        Config.Char("Path Separator", IC10EditorPlugin.PathSeparator);

        ImGui.Separator();
        ImGui.Checkbox("Show debug window", ref DebugWindow.IsOpen);
        ImGui.Separator();
        ImGui.NewLine();
        ImGui.Text("Colors");
        ImGui.SameLine();
        if (ImGui.Button("Reload"))
        {
            IC10EditorPlugin.Instance.Config.Reload();
            IC10EditorPlugin.LoadColorConfig();
        }

        ImGui.TextWrapped(
                "Color settings can be changed in two ways\n"
                + "- BepInEx/config/aproposmath-stationeers-ic10-editor.cfg\n"
                + "\tedit the file and then click the reload button above\n"
                + "- in the Stationeers Launchpad config menu on game startup\n"
                + "\n"
                );
    }

    public static void DrawKeybindings()
    {
        using var _ = new ScopedChild("");
        ImGui.TextWrapped(
            "\nKeyboard Shortcuts:\n"
                + "\n"
                + "Arrow Keys            Move caret\n"
                + "Home/End              Move caret to start/end of line\n"
                + "Page Up/Down          Move caret up/down by 20 lines\n"
                + "Shift+Arrow           Select text while moving caret\n"
                + "Tab                   Autocomplete/Indent\n"
                + "Ctrl + Q              Quit (no confirm, see note below)\n"
                + "Ctrl + W              Close tab (only for library code tabs)\n"
                + "Ctrl + S              Save\n"
                + "Ctrl + E              Motherboard: Save + export code to ic chip\n"
                + "Ctrl + E              Library Tab: Apply code to Motherboard tab\n"
                + "Ctrl + Z              Undo\n"
                + "Ctrl + Y              Redo\n"
                + "Ctrl + C              Copy selected code\n"
                + "Ctrl + V              Paste code from clipboard\n"
                + "Ctrl + A              Select all code\n"
                + "Ctrl + X              Cut selected code\n"
                + "Ctrl + Arrow          Move caret by word\n"
                + "Ctrl + Click          Open Stationpedia page of word at cursor\n"
                + "Ctrl + Space          Next tab\n"
                + "Ctrl + Shift + Space  Previous tab\n"
                + "Ctrl + Number         Switch to tab <Number>\n\n"
        );

        ImGui.Separator();

        ImGui.TextWrapped(
            "\nNotes:\n"
                + "\n"
                + "Closing the editor via Ctrl+Q key or Cancel button will not ask for confirmation, BUT you can always reopen the editor and Undo (Ctrl+Z) to get the state before cancelling.\n"
        );

    }
    public static void DrawVIM()
    {
        using var _ = new ScopedChild("");
        if (Config.Bool("VIM bindings enabled", IC10EditorPlugin.VimBindings) && !VimEnabled)
            LibraryWindow.Window.ActiveEditor.KeyHandler.Mode = KeyMode.Insert;

        ImGui.TextWrapped(
            "\nVIM Mode - Supported Commands:\n"
                + "\n"
                + "Movements (with optional number prefix):\n"
                + "h j, k, l, w, b, 0, $, gg, G, *, #, <C-u>, <C-d>\n\n"
                + "Editing (with optional number and movement or search):\n"
                + "i I a A c C d D dd o O x y yy p ~ << >> u <C-r>\n\n"
                + "Search:\n"
                + "f t gf\n\n"
                + "Other:\n"
                + ". ; n N :w :wq :q\n\n"
                + "Notes:\n"
                + "'gf' opens Stationpedia page of hash/name at cursor\n\n"
        );
    }
}

public static class DebugWindow
{
    public static bool IsOpen = false;
    private static readonly Queue<double> _renderTimes = new();
    public static Stopwatch RenderStopwatch = null;

    public static void Draw()
    {
        if (!IsOpen)
            return;

        using var _ = new ScopedStyleColor(ImGuiCol.WindowBg, Color(0.2f, 0.2f, 0.2f, 1.0f));
        ImGui.SetNextWindowSize(Scale * new Vector2(600, 400), ImGuiCond.FirstUseEver);
        ImGui.Begin(
            "IC10 Debug Window",
            ref IsOpen,
            ImGuiWindowFlags.NoSavedSettings
        );

        double avgRenderTime = 0.0;
        double maxRenderTime = 0.0;

        foreach (var time in _renderTimes)
        {
            avgRenderTime += time;
            if (time > maxRenderTime)
                maxRenderTime = time;
        }
        if (_renderTimes.Count > 0)
            avgRenderTime /= _renderTimes.Count;

        avgRenderTime = (avgRenderTime * 1000000.0);
        maxRenderTime = (maxRenderTime * 1000000.0);

        var e = LibraryWindow.Window.ActiveEditor;

        ImGui.Text($"Render Time: {avgRenderTime:F0} us avg, {maxRenderTime:F0} us max");
        ImGui.Text($"ScrollY: {e._scrollY:F2}");
        ImGui.Text(
            $"Textpos: {e._textAreaOrigin.x:F2}, {e._textAreaOrigin.y:F2}, {e._textAreaOrigin.y + e._scrollY:F2}"
        );
        ImGui.Text($"Textsize: {e._textAreaSize.x:F2}, {e._textAreaSize.y:F2}");
        ImGui.Text($"CaretPixelPos: {e._caretPixelPos.x:F2}, {e._caretPixelPos.y:F2}");
        ImGui.Text($"MousePos: {ImGui.GetMousePos().x:F2}, {ImGui.GetMousePos().y:F2}");
        ImGui.Text(
            $"Mouse relative to text area: {ImGui.GetMousePos().x - e._textAreaOrigin.x:F2}, {ImGui.GetMousePos().y - (e._textAreaOrigin.y + e._scrollY):F2}"
        );
        ImGui.Text($"LineNumberOffset: {Editor.LineNumberOffset}");
        ImGui.Text($"Mouse caret pos: {e.GetTextPositionFromMouse(false)}");
        var mousePos = ImGui.GetMousePos();
        float c1 = (mousePos.x - e._textAreaOrigin.x + ImGui.GetStyle().FramePadding.x) / CharWidth;
        float c2 = c1 - Editor.LineNumberOffset;
        ImGui.Text($"Mouse caret col: {c1}, {c2}");
        ImGui.Text(
            $"Mouse line: {(ImGui.GetMousePos().y - e._textAreaOrigin.y) / LineHeight:F2}"
        );
        ImGui.Text($"CaretPixelPos: {e._caretPixelPos.x:F2}, {e._caretPixelPos.y:F2}");
        ImGui.Text($"Font w/h: {CharWidth:F2}, {LineHeight:F2}");

        if (RenderStopwatch != null)
        {
            var seconds = RenderStopwatch.Elapsed.TotalSeconds;
            _renderTimes.Enqueue(seconds);
            while (_renderTimes.Count > 100)
                _renderTimes.Dequeue();
        }

        ImGui.Separator();
        ImGui.End();
    }

}

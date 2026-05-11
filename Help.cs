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

    // --- Colors ---
    private static readonly Vector4 ColHeading    = new(0.4f, 0.8f, 1.0f, 1.0f);
    private static readonly Vector4 ColSubheading = new(0.6f, 0.9f, 0.6f, 1.0f);
    private static readonly Vector4 ColKey        = new(1.0f, 0.85f, 0.4f, 1.0f);
    private static readonly Vector4 ColHint       = new(0.8f, 0.8f, 0.8f, 1.0f);
    private static readonly Vector4 ColAccent     = new(1.0f, 0.5f, 0.3f, 1.0f);
    private static readonly Vector4 ColGreen      = new(0.4f, 0.9f, 0.4f, 1.0f);
    private static readonly Vector4 ColYellow     = new(1.0f, 0.9f, 0.3f, 1.0f);
    private static readonly Vector4 ColRed        = new(1.0f, 0.4f, 0.4f, 1.0f);
    private static readonly Vector4 ColDim        = new(0.5f, 0.5f, 0.6f, 1.0f);

    private static void Heading(string text)
    {
        ImGui.NewLine();
        ImGui.TextColored(ColHeading, text);
        ImGui.Separator();
    }

    private static void SubHeading(string text)
    {
        ImGui.NewLine();
        ImGui.TextColored(ColSubheading, text);
    }

    private static void Bullet(string text, Vector4? color = null)
    {
        ImGui.TextColored(color ?? ColHint, "  \u2022 ");
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private static void KeyRow(string key, string desc)
    {
        ImGui.TextColored(ColKey, $"  {key,-26}");
        ImGui.SameLine();
        ImGui.TextWrapped(desc);
    }

    private static ImGuiTabItemFlags _TabFlags(int i)
    {
        return i == _tabIndex ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
    }

    public static void Draw()
    {
        if (!IsOpen)
            return;

        using var _bg = new ScopedStyleColor(ImGuiCol.WindowBg, ICodeFormatter.ColorFromVector4(0.08f, 0.08f, 0.14f, 1.0f));
        using var _frame = new ScopedStyleColor(ImGuiCol.FrameBg, ICodeFormatter.ColorFromVector4(0.12f, 0.12f, 0.2f, 1.0f));
        using var _tab = new ScopedStyleColor(ImGuiCol.Tab, ICodeFormatter.ColorFromVector4(0.15f, 0.15f, 0.25f, 1.0f));
        using var _tabActive = new ScopedStyleColor(ImGuiCol.TabActive, ICodeFormatter.ColorFromVector4(0.25f, 0.25f, 0.45f, 1.0f));
        using var _tabHover = new ScopedStyleColor(ImGuiCol.TabHovered, ICodeFormatter.ColorFromVector4(0.3f, 0.3f, 0.5f, 1.0f));

        ImGui.SetNextWindowSize(Scale * new Vector2(650, 450), ImGuiCond.FirstUseEver);
        ImGui.Begin(
            "IC10 Editor Help",
            ref IsOpen,
            ImGuiWindowFlags.NoSavedSettings
        );

        if (ImGui.BeginTabBar("EditorTabs"))
        {
            if (ImGui.BeginTabItem("Overview", _TabFlags(0)))
            {
                DrawOverview();
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

            if (ImGui.BeginTabItem("Version Control", _TabFlags(4)))
            {
                DrawVersionControl();
                ImGui.EndTabItem();
            }
            if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
                _tabIndex = 4;
        }
        ImGui.EndTabBar();

        ImGui.End();
    }

    public static void DrawOverview()
    {
        using var _ = new ScopedChild("");

        Heading("IC10 Editor");
        ImGui.TextWrapped(
            "A code editor for Stationeers IC10 programs with syntax highlighting, "
            + "auto-completion, undo/redo, VIM bindings, and built-in version control."
        );

        SubHeading("Quick Tips");
        Bullet("Click 'Commit' in the Library window to snapshot all scripts.");
        Bullet("Right-click file names or folders in the Library for context actions.");
        Bullet("Folders are virtual — files stay at their original path on disk (the Title encodes the path with the configured separator).");
        Bullet("Use Ctrl+Click on a hash code/name to open its Stationpedia page.");
        Bullet("See the 'Version Control' tab for details on history and diffs.");

        SubHeading("Limits");
        Bullet("IC10 programs: max 128 lines, 90 chars/line, 4096 bytes total.", ColDim);
        Bullet("These limits can be toggled in the Config tab.", ColDim);

        ImGui.NewLine();
        if (Button("Native Editor", new Vector2(200, 0), "Switch to the built-in Stationeers IC10 editor."))
            LibraryWindow.Window.SwitchToNativeEditor();
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

        Heading("Navigation");
        KeyRow("Arrow Keys",            "Move caret");
        KeyRow("Home / End",            "Start / end of line");
        KeyRow("Page Up / Down",        "Move caret by 20 lines");
        KeyRow("Ctrl + Arrow",          "Move caret by word");

        Heading("Selection & Editing");
        KeyRow("Shift + Arrow",         "Select text");
        KeyRow("Tab",                   "Autocomplete / indent");
        KeyRow("Ctrl + A",              "Select all");
        KeyRow("Ctrl + C",              "Copy");
        KeyRow("Ctrl + X",              "Cut");
        KeyRow("Ctrl + V",              "Paste");
        KeyRow("Ctrl + Z",              "Undo");
        KeyRow("Ctrl + Y",              "Redo");

        Heading("Files & Tabs");
        KeyRow("Ctrl + S",              "Save");
        KeyRow("Ctrl + E",              "Export to IC chip (Motherboard tab)");
        KeyRow("Ctrl + E",              "Apply to Motherboard (Library tab)");
        KeyRow("Ctrl + W",              "Close tab (library tabs only)");
        KeyRow("Ctrl + Q",              "Quit (no confirmation)");
        KeyRow("Ctrl + Space",          "Next tab");
        KeyRow("Ctrl + Shift + Space",  "Previous tab");
        KeyRow("Ctrl + Number",         "Switch to tab N");

        Heading("Other");
        KeyRow("Ctrl + Click",          "Open Stationpedia page for word at cursor");

        ImGui.NewLine();
        ImGui.TextColored(ColDim,
            "Note: Closing via Ctrl+Q or Cancel does not confirm — but you can "
            + "reopen the editor and Undo (Ctrl+Z) to recover."
        );
    }
    public static void DrawVIM()
    {
        using var _ = new ScopedChild("");
        if (Config.Bool("VIM bindings enabled", IC10EditorPlugin.VimBindings) && !VimEnabled)
            LibraryWindow.Window.ActiveEditor.KeyHandler.Mode = KeyMode.Insert;

        Heading("VIM Mode");

        SubHeading("Movements (with optional count prefix)");
        ImGui.TextColored(ColKey, "  h  j  k  l  w  b  0  $  gg  G  *  #  <C-u>  <C-d>");

        SubHeading("Editing (with optional count + motion/search)");
        ImGui.TextColored(ColKey, "  i  I  a  A  c  C  d  D  dd  o  O  x  y  yy  p  ~  <<  >>  u  <C-r>");

        SubHeading("Search");
        ImGui.TextColored(ColKey, "  f  t  gf");

        SubHeading("Commands");
        ImGui.TextColored(ColKey, "  .  ;  n  N  :w  :wq  :q");

        ImGui.NewLine();
        Bullet("'gf' opens the Stationpedia page for the hash/name at cursor.", ColDim);
    }

    public static void DrawVersionControl()
    {
        using var _ = new ScopedChild("");

        Heading("Version Control (Fossil)");
        ImGui.TextWrapped(
            "The editor uses Fossil SCM to track script changes. "
            + "Fossil is downloaded automatically on first use."
        );

        SubHeading("Committing");
        Bullet("Click 'Commit' in the Library window to snapshot all scripts.");
        Bullet("Each commit stores the current state of every script file.");
        Bullet("A custom commit message can be provided.");

        SubHeading("File States");
        ImGui.TextColored(ColGreen,  "  o Unchanged");
        ImGui.SameLine();
        ImGui.TextColored(ColDim, " — file matches the last commit.");
        ImGui.TextColored(ColYellow, "  o Modified ");
        ImGui.SameLine();
        ImGui.TextColored(ColDim, " — file has been edited since the last commit.");
        ImGui.TextColored(ColRed,    "  o Untracked");
        ImGui.SameLine();
        ImGui.TextColored(ColDim, " — new file, not yet committed.");

        SubHeading("History & Diffs");
        Bullet("Right-click a file in the Library and choose 'History' to view past versions.");
        Bullet("Select a version to preview its code.");
        Bullet("Diffs show what changed between versions.");

        SubHeading("Backups");
        Bullet($"Backups of all library scripts are created at every game start.");
        Bullet($"Up to {FossilVCS.KeepBackupCount} automatic backups of the repository are kept.");
        Bullet("Backups are stored in <GameDir>/BepInEx/cache/ic10editor/backups");
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

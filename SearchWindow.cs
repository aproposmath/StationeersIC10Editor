namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Assets.Scripts;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;
using static Utils;

public class SearchWindow
{
    public bool IsOpen = true;
    public string SearchQuery = "";
    public string ReplaceWith = "";
    public EditorWindow Window;
    public Editor Editor => Window.ActiveEditor;

    private bool _focusSearch = true;
    private bool _focusReplace = false;

    public SearchWindow(EditorWindow window, string query = null)
    {
        Window = window;
        if (query != null)
            SearchQuery = query;
    }

    public void Open()
    {
        IsOpen = true;
        _focusSearch = true;
        _focusReplace = false;
    }

    public void Close()
    {
        IsOpen = false;
    }

    public TextPosition Find(TextPosition pos, bool findNext = true)
    {
        Editor.Selection.Reset();
        pos = Editor.FindString(pos, SearchQuery, true, findNext);
        if (!(bool)pos) pos = Editor.FindString(new TextPosition(0, 0), SearchQuery);
        if (!(bool)pos) return pos;

        Editor.CaretPos = pos;
        Editor.Selection.Start = pos;
        Editor.Selection.End = new TextPosition(pos.Line, pos.Col + SearchQuery.Length);

        return pos;

    }

    public void Replace()
    {
        var pos = Find(Editor.CaretPos, false);
        if (!(bool)pos) pos = Find(new TextPosition(0, 0), false);
        if (!(bool)pos) return;
        Editor.PushUndoState(false);
        Editor.CaretPos = pos;
        Editor.CurrentLine = Editor.CurrentLine.Remove(pos.Col, SearchQuery.Length).Insert(pos.Col, ReplaceWith);
        // Editor.CaretPos = new TextPosition(pos.Line, pos.Col + ReplaceWith.Length);
        // Editor.Selection = new TextRange(pos, Editor.CaretPos);
        Find(pos);
    }

    public void ReplaceAll()
    {
        var pos = Find(new TextPosition(0, 0), false);
        if (!(bool)pos) return;
        Editor.PushUndoState(false);
        while ((bool)pos)
        {
            Editor.CurrentLine = Editor.CurrentLine.Remove(pos.Col, SearchQuery.Length).Insert(pos.Col, ReplaceWith);
            pos = Find(new TextPosition(pos.Line, pos.Col + ReplaceWith.Length));
        }
    }

    public void Draw()
    {
        if (!IsOpen)
            return;


        ImGui.Begin("Search and Replace", ref IsOpen, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.AlwaysAutoResize);

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape))
            Close();

        ImGui.Text("Search ");
        ImGui.SameLine();
        if (_focusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            _focusSearch = false;
        }
        if (InputText("##search_query", ref SearchQuery, 36 * CharWidth))
        {
            Find(Editor.CaretPos);
            _focusSearch = true;
        }

        ImGui.Text("Replace");
        ImGui.SameLine();
        if (_focusReplace)
        {
            ImGui.SetKeyboardFocusHere();
            _focusReplace = false;
        }
        if (InputText("##replace_with", ref ReplaceWith, 36 * CharWidth))
        {
            Replace();
            _focusReplace = true;
        }

        var size = new Vector2(CharWidth * 12, 0);

        if (Button("Find", size))
            Find(Editor.CaretPos);
        ImGui.SameLine();
        if (Button("Replace", size))
            Replace();
        ImGui.SameLine();
        if (Button("Replace all", size))
            ReplaceAll();

        ImGui.End();
    }
}

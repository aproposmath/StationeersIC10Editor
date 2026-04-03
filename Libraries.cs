namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;

using BepInEx.Configuration;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;
using static Utils;


public static class LibrariesWindow
{
    private static bool _open = false;
    private static List<InstructionData> _libraryCodes = new List<InstructionData>();

    public static List<InstructionData> LibraryCodes => _libraryCodes;
    private static List<InstructionData> _librarySearchResults = new List<InstructionData>();

    static HashSet<string> _libraryTitles;
    private static string _librarySearchText = "";

    private static bool _librarySearchJustOpened = false;
    private static int _librarySelectedIndex = -1;

    public static bool IsOpen => _open;
    public static EditorWindow Window;

    public static void Draw()
    {
        if (!_open)
            return;

        bool open = true;

        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        if (
            ImGui.Begin("Library Search", ref open)
        )
        {
            if (_librarySearchJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _librarySearchJustOpened = false;
            }

            ImGui.Text("Search libraries:");
            ImGui.SameLine();
            var oldSearchText = _librarySearchText;
            ImGui.InputText(
                "##LibrarySearch",
                ref _librarySearchText,
                256,
                ImGuiInputTextFlags.EnterReturnsTrue
            );

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Load first matching entry with Enter key, or any entry with double-click.");

            if (oldSearchText != _librarySearchText)
                Search(_librarySearchText);

            // Search if Enter pressed or text changed
            if (
                (ImGui.IsItemDeactivatedAfterEdit()
                || ImGui.IsItemFocused()) && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
            )
            {
                if (_librarySearchResults.Count > 0)
                    LoadLibraryEntry(_librarySearchResults[0]);
            }

            ImGui.SameLine();

            var libExists = _libraryTitles.Contains(_librarySearchText) || string.IsNullOrWhiteSpace(_librarySearchText);

            var tooltip = "Creates a new library entry using the name in the search box and the current code.";
            if (libExists)
                tooltip = $"Library \"{_librarySearchText}\" already exists.";
            else if (string.IsNullOrWhiteSpace(_librarySearchText))
                tooltip = "Please enter a valid library name to the search field to create a new entry.";

            if (Button("New", Vector2.zero, tooltip, libExists))
            {
                InputSourceCode.Paste(Window.MotherboardTab[0].Code);
                InputSourceCode.Instance.SaveNewWithName(_librarySearchText, "");
                LoadLibraries().Forget();
            }


            ImGui.Separator();

            // Show results
            if (_librarySearchResults.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No results found.");
            }
            else
            {
                using var _ = new ScopedStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
                ImGui.BeginChild("LibrarySearchResults", new Vector2(500, 600), true);
                for (int i = 0; i < _librarySearchResults.Count; i++)
                {
                    var lib = _librarySearchResults[i];

                    var entryLabel = "";
                    if (lib.WorkshopFileHandle != 0)
                        entryLabel = $"{lib.Title} by {lib.Author} (workshop)";
                    else
                        entryLabel = $"{lib.Title} by {lib.Author} (local)";
                    if (ImGui.Selectable(entryLabel, _librarySelectedIndex == i) || _librarySearchResults.Count == 1)
                    {
                        _librarySelectedIndex = i;

                        if (_previewEditor == null)
                        {
                            _previewEditor = new Editor(Window.ActiveEditor.KeyHandler, lib);
                            _previewEditor.IsReadOnly = true;
                        }
                        _previewEditor.ResetCode(lib?.Instructions ?? "", false);
                    }

                    // Double-click to load
                    if (
                        ImGui.IsItemHovered()
                        && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)
                    )
                    {
                        LoadLibraryEntry(lib);
                    }
                }
                ImGui.EndChild();

                ImGui.SameLine();
                ImGui.BeginChild("LibrarySearchPreview", new Vector2(700, 600), true);

                if (
                    _librarySelectedIndex >= 0
                    && _librarySelectedIndex < _librarySearchResults.Count
                )
                {
                    var lib = _librarySearchResults[_librarySelectedIndex];
                    Text($"Title:  {lib.Title}", 300);
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(330);
                    if (Button("Publish", buttonSize, "Publish the library to the workshop"))
                        lib.PublishToWorkshop().Forget();
                    ImGui.SameLine();
                    if (Button("Delete", buttonSize, "Delete the library"))
                    {
                        _confirmDeleteLibWindow = new ConfirmWindow(
                            $"Are you sure to delete the library '{lib.Title}'?"
                        );
                        _confirmDeleteLibWindow.OnConfirm = () =>
                        {
                            InputSourceCode.DeleteInstruction(lib.DirectoryPath.Name);
                            LoadLibraries().Forget();
                            _librarySelectedIndex = -1;
                            _previewEditor = null;
                            _confirmDeleteLibWindow = null;
                        };
                    }

                    if (_confirmDeleteLibWindow != null)
                    {
                        if (!_confirmDeleteLibWindow.IsOpen)
                            _confirmDeleteLibWindow = null;
                        else
                            _confirmDeleteLibWindow.Draw();
                    }

                    ImGui.SameLine();
                    if (Button("Copy", buttonSize, "Copy code to clipboard"))
                        GameManager.Clipboard = lib.Instructions;

                    ImGui.SameLine();
                    if (Button("Load", buttonSize, "Load this library into the editor"))
                        LoadLibraryEntry(lib);

                    ImGui.Text($"Author: {lib.Author}");
                    var date = DateTime.FromFileTimeUtc(lib.DateTime);
                    ImGui.Text($"Date:   {date.ToLocalTime()}");
                    ImGui.TextWrapped($"Description:");
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(605);
                    if (Button("Save", buttonSize, "Save description"))
                        lib.SaveToFile(lib.DirectoryPath);
                    ImGui.InputTextMultiline(
                        "##LibraryDescriptionEdit",
                        ref lib.Description,
                        1024,
                        new Vector2(675, 60)
                    );
                    ImGui.Separator();

                    var heightAvailable = ImGui.GetContentRegionAvail().y - 10;

                    _previewEditor.Update();
                    using var _cbs = new ScopedStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
                    _previewEditor.Draw(
                        ImGui.GetCursorScreenPos(),
                        new Vector2(670, heightAvailable),
                        "##LibraryPreviewEditor"
                    );
                }
                ImGui.EndChild();
            }

            ImGui.Separator();
            if (ImGui.Button("Close"))
                _open = false;
            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _open = false;

            ImGui.End();
        }

        if (!open)
            _open = false;
    }

    public static async UniTask LoadLibraries()
    {
        var items = await NetworkManager.GetLocalAndWorkshopItems(
            SteamTransport.WorkshopType.ICCode
        );

        var libs = new List<InstructionData>();
        var titles = new HashSet<string>();

        foreach (var item in items)
        {
            var data = InstructionData.GetFromFile(item.FilePathFullName);
            data.ItemWrapper = item;
            libs.Add(data);
            titles.Add(data.Title);
        }

        await UniTask.SwitchToMainThread();
        _libraryCodes = libs;
        _libraryTitles = titles;

        if (IsOpen)
            Search(_librarySearchText);
    }

    public static void Open()
    {
        _open = true;
        _librarySearchJustOpened = true;

        ImGui.OpenPopup("Library Search");

        LoadLibraries().Forget();
    }

    static Editor _previewEditor = null;
    static ConfirmWindow _confirmDeleteLibWindow = null;

    private static void Search(string query)
    {
        _librarySearchResults.Clear();

        var q = query.Trim().ToLowerInvariant();

        foreach (var lib in _libraryCodes)
        {
            if (
                string.IsNullOrEmpty(q)
                || lib.Title.ToLowerInvariant().Contains(q)
                || lib.Instructions.ToLowerInvariant().Contains(q)
            )
            {
                _librarySearchResults.Add(lib);
            }
        }
    }

    private static void LoadLibraryEntry(InstructionData lib)
    {
        if (lib == null)
            return;

        var numTabsBefore = Window.Tabs.Count;

        try
        {
            var editor = new Editor(Window.ActiveEditor.KeyHandler, lib);
            editor.ResetCode(lib.Instructions);
            Window.Tabs.Add(new EditorTab(Window, editor, lib));
            Window.SetTab(Window.Tabs.Count - 1);
            _open = false;
        }
        catch (Exception e)
        {
            L.Error($"Failed to load library: {e}");
            while (Window.Tabs.Count > numTabsBefore)
                Window.CloseTab(Window.Tabs.Count - 1);
        }
    }
}

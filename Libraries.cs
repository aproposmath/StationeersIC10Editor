namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;

using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.UI;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;

public class VersionedLibrary
{
    public InstructionData Data;
    public FileState State = FileState.Untracked;

    public VersionedLibrary(InstructionData data)
    {
        Data = data;
    }

    static readonly Dictionary<FileState, uint> Colors = new()
    {
        { FileState.Untracked, ICodeFormatter.ColorFromHTML("red") },
        { FileState.Unchanged, ICodeFormatter.ColorFromHTML("green") },
        { FileState.Modified, ICodeFormatter.ColorFromHTML("yellow") },
    };

    public uint Color => Colors[State];
    public DateTime Date => DateTime.FromFileTimeUtc(Data.DateTime).ToLocalTime();

    public bool IsWorkshop => State == FileState.Workshop;

    public void UpdateFileState(Dictionary<string, FileState> fileStates)
    {
        // check if path is subdir of FossilVCS.ScriptDir
        if (Data.DirectoryPath.Parent.FullName != FossilVCS.ScriptsDir)
        {
            State = FileState.Workshop;
            return;
        }
        State = FileState.Untracked;
        if (fileStates.TryGetValue(Data.DirectoryPath.Name, out var state))
            State = state;
        else
            L.Warning($"File state not found for library: {Data.DirectoryPath.Name}");
    }

    public void Save()
    {
        Data.SaveToFile(Data.DirectoryPath);
        UpdateFileState().Forget();
    }

    public async UniTaskVoid UpdateFileState()
    {
        L.Debug($"Updating file state for library: {Data.DirectoryPath.Name}, State: {State}");
        State = await FossilVCS.GetFileState(Data.DirectoryPath.Name + "/instruction.xml");
        L.Debug($"File state for library {Data.DirectoryPath.Name}: {State}");
    }
    public async UniTask Publish()
    {
        try
        {
            await Data.PublishToWorkshop();
        }
        finally
        {
            await LibrariesWindow.LoadLibraries();
        }
    }

}

public class LibNode
{
    public string Name;
    public List<LibNode> Children = new List<LibNode>();
    public VersionedLibrary Library = null;
    public string Prefix = "";

    public void Draw(bool tree = true)
    {
        if (Library != null)
        {
            var title = Library.Data.Title;
            if (tree && Prefix.Length > 0)
                title = title.Substring(Prefix.Length + 1);
            // if (Library.Data.WorkshopFileHandle != 0)
            //     title += " by " + Library.Data.Author;

            float radius = 5.0f; ;
            var imSize = 0.8f * LineHeight;
            var posNext = ImGui.GetCursorPos() + new Vector2(Mathf.Max(2 * radius, imSize), 0);

            if (Library.State == FileState.Workshop)
            {
                var texPtr = ImGuiManager.ImGuiPointerFor(WorkshopMenu.Instance.SteamImage.texture);

                ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 5f);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 0.5f * LineHeightWithSpacing - 0.5f * imSize);
                ImGui.Image(texPtr, new Vector2(imSize, imSize));
                ImGui.SameLine();
            }
            else
            {
                uint color = Library.Color;
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + imSize / 2 - radius);
                ImGui.GetWindowDrawList().AddCircleFilled(ImGui.GetCursorScreenPos() + new Vector2(0, LineHeightWithSpacing / 2), radius, color, 12);
            }

            ImGui.SetCursorPos(posNext);
            if (ImGui.Selectable(title, LibrariesWindow.Selected == Library) || LibrariesWindow._librarySearchResults.Count == 1)
            {
                LibrariesWindow.Selected = Library;

                if (LibrariesWindow._previewEditor == null)
                {
                    LibrariesWindow._previewEditor = new Editor(LibrariesWindow.Window.ActiveEditor.KeyHandler, Library);
                    LibrariesWindow._previewEditor.IsReadOnly = true;
                }
                LibrariesWindow._previewEditor.ResetCode(Library?.Data.Instructions ?? "", false);
            }
            if (ImGui.IsItemHovered() && ShowTooltip)
                ImGui.SetTooltip($"Title:  {Library.Data.Title}\nPath:   {Library.Data.DirectoryPath.FullName}\nAuthor: {Library?.Data.Author}\nDate:   {Library.Date}");

            // Double-click to load
            if (
                ImGui.IsItemHovered()
                && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)
            )
            {
                LibrariesWindow.LoadLibraryEntry(Library);
            }
            if (Children.Count == 0)
                return;
        }

        if (!tree)
        {
            foreach (var child in Children)
                child.Draw(tree);
            return;
        }

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() - 5);
        if (ImGui.TreeNode(Name))
        {
            foreach (var child in Children)
                child.Draw();
            ImGui.TreePop();
        }
    }
}

public static class LibrariesWindow
{
    private static bool _open = false;
    private static List<VersionedLibrary> _libraryCodes = new List<VersionedLibrary>();

    public static List<VersionedLibrary> LibraryCodes => _libraryCodes;
    public static List<VersionedLibrary> _librarySearchResults = new List<VersionedLibrary>();
    public static Dictionary<string, FileState> _fileStates = new Dictionary<string, FileState>();

    public static List<LibNode> OuterNodes = new List<LibNode>();

    static HashSet<string> _libraryTitles;
    private static string _librarySearchText = "";

    private static bool _librarySearchJustOpened = false;
    public static VersionedLibrary Selected = null;

    public static bool IsOpen => _open;
    public static EditorWindow Window;

    private static bool _searchFullText = false;
    private static bool _showWorkshopItems = true;
    private static bool _showLocalItems = true;
    private static bool _showUntracked = true;
    private static bool _showModified = true;
    private static bool _showUnchanged = true;
    private static char _dirSeparator = '|';

    private static bool _treeView = true;

    private static ConfirmWindow _newLibWindow = null;

    public static void Draw()
    {
        if (!_open)
            return;

        bool open = true;

        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        using var _bg = new ScopedStyleColor(ImGuiCol.WindowBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.1f, 1.0f));

        if (
            ImGui.Begin("Library Search", ref open)
        )
        {
            using var _ = new ScopedStyleColor(ImGuiCol.FrameBg, ICodeFormatter.ColorFromVector4(0.2f, 0.2f, 0.2f, 1.0f));
            if (_librarySearchJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _librarySearchJustOpened = false;
            }

            var width = ImGui.GetContentRegionAvail().x;

            ImGui.Text("Search:");
            ImGui.SameLine();
            var oldSearchText = _librarySearchText;
            InputText(
                "##LibrarySearch",
                ref _librarySearchText,
                20 * CharWidth
            );

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Load first matching entry with Enter key, or any entry with double-click.");

            if (oldSearchText != _librarySearchText)
                Search();

            // Load first entry when Enter pressed
            if (
                (ImGui.IsItemDeactivatedAfterEdit()
                || ImGui.IsItemFocused()) && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
                && _librarySearchResults.Count > 0)
                LoadLibraryEntry(_librarySearchResults[0]);

            ImGui.SameLine();

            if (Checkbox("Full Text  ", ref _searchFullText, "Search also in the code of the library."))
                Search();

            ImGui.SameLine();

            if (Checkbox("Workshop", ref _showWorkshopItems, "Show subscribed Steam workshop libraries."))
                Search();

            ImGui.SameLine();

            if (Checkbox("Local  ", ref _showLocalItems, "Show local libraries."))
                Search();

            ImGui.SameLine();

            if (Checkbox("Untracked", ref _showUntracked, "Show untracked libraries\n  -> no versioned saved yet, red dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Modified", ref _showModified, "Show modified libraries\n  -> changes since last version detected, yellow dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Unchanged  ", ref _showUnchanged, "Show unchanged libraries\n  -> no changes since last version, green dot"))
                Search();

            ImGui.SameLine();

            Checkbox("TreeView", ref _treeView, "Show libraries in tree view\nUse '|' in title to separate folder names");

            ImGui.SameLine();
            ImGui.SetCursorPosX(width - buttonSize.x - 0 * ImGui.GetStyle().ItemSpacing.x);
            if (Button("New", buttonSize, "Create new library from current Motherboard code"))
            {
                _newLibWindow = new ConfirmWindow($"Create new library", null, "Title:");
                _newLibWindow.OnConfirm = () =>
                {
                    InputSourceCode.Paste(Window.MotherboardTab[0].Code);
                    InputSourceCode.Instance.SaveNewWithName(_newLibWindow.UserInput, "");
                    LoadLibraries().Forget();
                    _newLibWindow = null;
                };
            }

            DrawLibrarySearchResults();
            ImGui.SameLine();
            DrawSelectedLibrary();

            if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                _open = false;

            if (_newLibWindow != null)
                _newLibWindow.Draw();

            ImGui.End();

        }

        if (!open)
            _open = false;
    }

    public static async UniTask LoadLibraries()
    {
        await UniTask.SwitchToThreadPool();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = await NetworkManager.GetLocalAndWorkshopItems(
            SteamTransport.WorkshopType.ICCode
        );
        L.Debug($"\tGet {sw.ElapsedMilliseconds}ms");

        _fileStates = await FossilVCS.GetFileStates();
        L.Debug($"\tFileStates {sw.ElapsedMilliseconds}ms");

        var libs = new List<VersionedLibrary>();
        var titles = new HashSet<string>();

        foreach (var item in items)
        {
            try
            {
                var data = InstructionData.GetFromFile(item.FilePathFullName);
                data.ItemWrapper = item;
                var newLib = new VersionedLibrary(data);
                newLib.UpdateFileState(_fileStates);
                libs.Add(newLib);
                titles.Add(data.Title);
            }
            catch
            {
                L.Warning($"Failed to load library: {item.FilePathFullName}");
            }
        }
        L.Debug($"\tBuild {sw.ElapsedMilliseconds}ms");
        libs.Sort((a, b) => a.Data.Title.ToLowerInvariant().CompareTo(b.Data.Title.ToLowerInvariant()));

        L.Debug($"\tLoaded {sw.ElapsedMilliseconds}ms");
        await UniTask.SwitchToMainThread();
        _libraryCodes = libs;
        _libraryTitles = titles;

        Search();
        L.Debug($"\tSearched {sw.ElapsedMilliseconds}ms");
    }

    public static void Open()
    {
        _open = true;
        _librarySearchJustOpened = true;

        ImGui.OpenPopup("Library Search");

        if (_libraryCodes.Count == 0)
            LoadLibraries().Forget();
    }

    public static Editor _previewEditor = null;
    static ConfirmWindow _confirmDeleteLibWindow = null;

    private static void Search()
    {
        _librarySearchResults.Clear();

        var q = _librarySearchText.Trim().ToLowerInvariant();

        foreach (var lib in _libraryCodes)
        {
            if (lib.IsWorkshop && !_showWorkshopItems)
                continue;
            if (!lib.IsWorkshop && !_showLocalItems)
                continue;
            if (lib.State == FileState.Untracked && !_showUntracked)
                continue;
            if (lib.State == FileState.Modified && !_showModified)
                continue;
            if (lib.State == FileState.Unchanged && !_showUnchanged)
                continue;
            if (
                string.IsNullOrEmpty(q)
                || lib.Data.Title.ToLowerInvariant().Contains(q)
                || (_searchFullText && lib.Data.Instructions.ToLowerInvariant().Contains(q))
            )
            {
                _librarySearchResults.Add(lib);
            }
        }
        UpdateTree();
    }

    private static void UpdateTree()
    {
        var nodes = new Dictionary<string, LibNode>();
        var newNodes = new List<LibNode>();
        foreach (var lib in _librarySearchResults)
        {
            var path = lib.Data.Title.Split(_dirSeparator);
            LibNode current = null;
            var prefix = "";
            foreach (var part in path)
            {
                var newPrefix = prefix;
                if (prefix != "")
                    newPrefix += _dirSeparator;
                newPrefix += part;
                if (!nodes.TryGetValue(newPrefix, out var node))
                {
                    node = new LibNode { Name = part, Prefix = prefix };
                    nodes[newPrefix] = node;
                    if (current == null)
                        newNodes.Add(node);
                    else
                        current.Children.Add(node);
                }
                current = node;
                prefix = newPrefix;
            }
            current.Library = lib;
        }

        OuterNodes.Clear();
        foreach (var node in newNodes)
            if (node.Children.Count > 0)
                OuterNodes.Add(node);
        foreach (var node in newNodes)
            if (node.Children.Count == 0)
                OuterNodes.Add(node);
    }

    public static int LoadLibraryEntry(VersionedLibrary lib)
    {
        if (lib == null)
            return -1;

        if (lib.State == FileState.Workshop)
        {
            Window.SetTab(0);
            Window.ActiveTab.Editors[0].ResetCode(lib.Data.Instructions);
            _open = false;
            return 0;
        }

        var tabs = Window.Tabs;

        foreach (var tab in tabs)
        {
            if (tab.Library == null)
                continue;
            if (tab.Library.DirectoryPath.FullName == lib.Data.DirectoryPath.FullName)
            {
                L.Info($"Library {lib.Data.Title} already open, switching to tab");
                var index = tabs.IndexOf(tab);
                Window.SetTab(index);
                tab.Editors[0].ResetCode(lib.Data.Instructions);
                _open = false;
                return index;
            }
        }

        var numTabsBefore = tabs.Count;

        try
        {
            var editor = new Editor(Window.ActiveEditor.KeyHandler, lib);
            editor.ResetCode(lib.Data.Instructions);
            Window.Tabs.Add(new EditorTab(Window, editor, lib.Data));
            Window.SetTab(Window.Tabs.Count - 1);
            _open = false;
            return Window.Tabs.Count - 1;
        }
        catch (Exception e)
        {
            L.Error($"Failed to load library: {e}");
            while (Window.Tabs.Count > numTabsBefore)
                Window.CloseTab(Window.Tabs.Count - 1);
        }
        return -1;
    }

    public static void DrawLibrarySearchResults()
    {
        using var _ = new ScopedStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        using var pane = new Pane("LibrarySearchResults", 0.35f);

        if (_librarySearchResults.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No results found.");
            return;
        }

        foreach (var node in OuterNodes)
            node.Draw(_treeView);
    }

    public static void DrawSelectedLibrary()
    {

        using var pane = new Pane("LibrarySearchPreview");

        if (Selected != null)
        {
            var width = ImGui.GetContentRegionAvail().x;
            var lib = Selected;
            var buttonPos = ImGui.GetCursorPos() + new Vector2(width - 4 * buttonSize.x - 3 * ImGui.GetStyle().ItemSpacing.x, 0);
            Text($"Author: {lib.Data.Author}");
            Text($"Date:   {lib.Date}");
            // Text($"Path:   {lib.Data.DirectoryPath.Name}");
            // ImGui.SameLine();
            Text($"Title: ", width / 2);
            ImGui.SameLine();

            bool isWorkshop = lib.State == FileState.Workshop;

            if (isWorkshop)
                ImGui.Text(lib.Data.Title);
            else
                InputText("##title", ref lib.Data.Title, width / 2);

            ImGui.SameLine();

            ImGui.SetCursorPosX(width - buttonSize.x + ImGui.GetStyle().ItemSpacing.x);
            if (Button("Save", buttonSize, "Save title/description", isWorkshop))
            {
                lib.Data.SaveToFile(lib.Data.DirectoryPath);
                LoadLibraries().Forget();
                // Search();
            }

            var pos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(buttonPos);
            if (Button("Publish", buttonSize, "Publish the library to the workshop"))
                lib.Publish().Forget();
            ImGui.SameLine();
            if (Button("Delete", buttonSize, "Delete the library"))
            {
                _confirmDeleteLibWindow = new ConfirmWindow($"Delete {lib.Data.Title}",
                    $"Are you sure?"
                );
                _confirmDeleteLibWindow.OnConfirm = () =>
                {
                    InputSourceCode.DeleteInstruction(lib.Data.DirectoryPath.Name);
                    LoadLibraries().Forget();
                    Selected = null;
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
                GameManager.Clipboard = lib.Data.Instructions;

            ImGui.SameLine();
            if (Button("Load", buttonSize, "Load this library into the editor"))
                LoadLibraryEntry(lib);

            ImGui.SetCursorPos(pos);

            ImGui.InputTextMultiline("LibraryDescriptionEdit", ref lib.Data.Description, 1024, new Vector2(width, 60), isWorkshop ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);

            if (ImGui.IsItemHovered() && string.IsNullOrEmpty(lib.Data.Description))
                ImGui.SetTooltip("Description");

            _previewEditor.Update();
            using var _cbs = new ScopedStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
            _previewEditor.Draw(
                ImGui.GetCursorScreenPos(),
                ImGui.GetContentRegionAvail(),
                "##LibraryPreviewEditor"
            );
        }
    }
}

namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.UI;
using Assets.Scripts.Util;

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

    public string Tooltip
    {
        get
        {

            var sb = new StringBuilder();
            sb.AppendLine($"Title:          {Data.Title}");
            sb.AppendLine($"Author:         {Data.Author}");
            sb.AppendLine($"Path:           {Data.DirectoryPath}");
            sb.AppendLine($"Last Modified:  {Data.DateTime}");
            sb.AppendLine($"Version Status: {State}");
            sb.AppendLine($"Description:    {Data.Description}");
            return sb.ToString();
        }
    }
}


public class LibNode
{
    private static LibNode _draggedNode = null;
    public string Name;
    public List<LibNode> Children = new List<LibNode>();
    public VersionedLibrary Library = null;
    public string Prefix = "";
    public int Count = 0;

    public bool IsLeaf => Children.Count == 0;
    public string FullName => Prefix + (Prefix.Length > 0 ? LibrariesWindow._dirSeparator : "") + Name;

    private void MoveTo(LibNode target, string prefix = null)
    {
        if (target == this || target.FullName.StartsWith(FullName))
            return;

        L.Debug($"Moving library {FullName} to {target.FullName} prefix={prefix}");
        var sep = LibrariesWindow._dirSeparator;
        if (IsLeaf)
        {
            var title = Library.Data.Title;
            var oldTitle = title;
            var lastSeparatorIndex = title.LastIndexOf(sep);
            if (lastSeparatorIndex != -1)
                title = title.Substring(lastSeparatorIndex + 1);
            title = prefix + title;
            if (!target.IsLeaf)
                title = target.Name + sep + title;
            if (!string.IsNullOrWhiteSpace(target.Prefix))
                title = target.Prefix + sep + title;

            L.Debug($"Moving library {oldTitle} to {title}");
            if (oldTitle != title)
            {
                Library.Data.Title = title;
                Library.Save();
                LibrariesWindow.NeedsUpdate();
            }
            return;
        }

        if (prefix == null)
            prefix = Name + sep;

        foreach (var child in Children)
            child.MoveTo(target, prefix);
    }

    public void Draw(bool tree = true)
    {
        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        bool hasChildren = Children.Count > 0;
        bool hasLibrary = Library != null;
        bool isSelected = hasLibrary && LibrariesWindow.Selected == Library;
        string imguiLabel = Name + (hasLibrary ? "##" + Library.Data.Title : $"##{FullName}");

        flags |= isSelected ? ImGuiTreeNodeFlags.Selected : 0;
        flags |= (hasChildren && tree) ? 0 : ImGuiTreeNodeFlags.Leaf;
        // ImGuiTreeNodeFlags.arr
        // flags |= ImGuiTreeNodeFlags.OpenOnArrow; // helps avoid click/drag conflicts


        bool isOpen = ImGui.TreeNodeEx(imguiLabel, flags);

        if (ImGui.BeginDragDropTarget())
        {

            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("LIBRARY_NODE");
                if (payload.NativePtr != null && payload.Delivery)
                    _draggedNode.MoveTo(this);
            }
            ImGui.EndDragDropTarget();
        }

        if (hasChildren)
        {
            ImGui.SameLine();
            ImGui.Text($"({Count})");
        }



        if (ImGui.BeginDragDropSource())
        {
            // using var _cbg = new ScopedStyleColor(ImGuiCol.PopupBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.2f, 1.0f));
            _draggedNode = this;
            // ImGui.Text(Name);
            ImGui.SetDragDropPayload("LIBRARY_NODE", "data");
            ImGui.TextUnformatted(Name);
            ImGui.EndDragDropSource();
        }

        if (hasLibrary)
        {

            float radius = 5.0f; ;
            var imSize = 0.9f * LineHeight;

            if (Library.State == FileState.Workshop)
            {
                var texPtr = ImGuiManager.ImGuiPointerFor(WorkshopMenu.Instance.SteamImage.texture);
                var spos = ImGui.GetCursorScreenPos() - new Vector2(imSize, 0.51f * LineHeightWithSpacing + 0.5f * imSize);
                ImGui.GetWindowDrawList().AddImage(texPtr, spos, spos + new Vector2(imSize, imSize));
            }
            else
            {
                uint color = Library.Color;
                var spos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddCircleFilled(spos - new Vector2(imSize / 2, LineHeightWithSpacing / 2), radius, color, 12);
            }

            if (ImGui.IsItemHovered())
            {
                if (ShowTooltip)
                    ImGui.SetTooltip(Library.Tooltip);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    LibrariesWindow.Selected = Library;

                    if (LibrariesWindow._previewEditor == null)
                    {
                        LibrariesWindow._previewEditor = new Editor(LibrariesWindow.Window.ActiveEditor.KeyHandler, Library);
                        LibrariesWindow._previewEditor.IsReadOnly = true;
                    }
                    LibrariesWindow._previewEditor.ResetCode(Library?.Data.Instructions ?? "", false);
                }

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    LibrariesWindow.LoadLibraryEntry(Library);
            }
        }

        if (isOpen)
        {
            foreach (var child in Children)
                if (!child.IsLeaf)
                    child.Draw();

            foreach (var child in Children)
                if (child.IsLeaf)
                    child.Draw();

            ImGui.TreePop();
        }

    }

    public void Sort()
    {
        // first sort by leaf status, then by name
        Children.Sort((a, b) => a.IsLeaf.CompareTo(b.IsLeaf) != 0 ? a.IsLeaf.CompareTo(b.IsLeaf) : string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        foreach (var child in Children)
            child.Sort();
    }

    public int UpdateCount()
    {
        Count = IsLeaf ? 1 : 0;
        foreach (var child in Children)
            Count += child.UpdateCount();
        return Count;
    }
}

public static class LibrariesWindow
{
    public static bool IsOpen = false;
    private static List<VersionedLibrary> _libraryCodes = new List<VersionedLibrary>();

    public static List<VersionedLibrary> LibraryCodes => _libraryCodes;
    public static List<VersionedLibrary> _librarySearchResults = new List<VersionedLibrary>();
    public static Dictionary<string, FileState> _fileStates = new Dictionary<string, FileState>();

    public static List<LibNode> OuterNodes = new List<LibNode>();

    private static string _librarySearchText = "";

    private static bool _librarySearchJustOpened = false;
    public static VersionedLibrary Selected = null;

    public static EditorWindow Window;

    private static bool _searchCode = false;
    private static bool _searchAuthor = false;
    private static bool _showWorkshopItems = true;
    private static bool _showLocalItems = true;
    private static bool _showUntracked = true;
    private static bool _showModified = true;
    private static bool _showUnchanged = true;
    public static char _dirSeparator = '|';

    private static bool _treeView = true;

    private static ConfirmWindow _confirmWindow = null;
    private static bool _needsUpdate = false;
    private static FileHistoryWindow _fileHistoryWindow = null;

    public static void NeedsUpdate()
    {
        _needsUpdate = true;
    }

    public static void Draw()
    {
        if (!IsOpen)
            return;

        if (_needsUpdate)
        {
            Search();
            _needsUpdate = false;
        }

        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        using var _bg = new ScopedStyleColor(ImGuiCol.WindowBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.1f, 1.0f));
        using var _cbg = new ScopedStyleColor(ImGuiCol.PopupBg, ICodeFormatter.ColorFromVector4(0.1f, 0.1f, 0.2f, 1.0f));

        if (
            ImGui.Begin("Library Search", ref IsOpen)
        )
        {
            using var _ = new ScopedStyleColor(ImGuiCol.FrameBg, ICodeFormatter.ColorFromVector4(0.2f, 0.2f, 0.2f, 1.0f));
            if (_librarySearchJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _librarySearchJustOpened = false;
            }

            var width = ImGui.GetContentRegionAvail().x;

            ImGui.Text("Search: ");
            ImGui.SameLine();
            var oldSearchText = _librarySearchText;
            InputText(
                "##LibrarySearch",
                ref _librarySearchText,
                30 * CharWidth
            );

            if (ImGui.IsItemHovered() && ShowTooltip)
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

            if (Checkbox("Code", ref _searchCode, "Full-text search in code"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Author", ref _searchAuthor, "Search author names"))
                Search();

            ImGui.SameLine(); ImGui.Text("  "); ImGui.SameLine();

            Checkbox("Tree View", ref _treeView, "Show libraries in tree view\nUse '|' in title to separate folder names\nExample: 'My Folder|My Library'");


            ImGui.SameLine();
            ImGui.SetCursorPosX(width - 3 * buttonSize.x - 2 * ImGui.GetStyle().ItemSpacing.x);
            if (Button("Reload", buttonSize, "Reload all scripts"))
                NeedsUpdate();

            ImGui.SameLine();
            if (Button("Commit", buttonSize, "Commit all files/changes to version control"))
            {
                var paths = new List<string>();
                var names = new List<string>();
                foreach (var lib in _libraryCodes)
                {
                    if (lib.State == FileState.Modified || lib.State == FileState.Untracked)
                    {
                        paths.Add(lib.Data.DirectoryPath.Name + "/instruction.xml");
                        names.Add(lib.Data.Title);
                    }
                }
                var msg = "Files to be commited: \n" + string.Join("\n", names);
                _confirmWindow = new ConfirmWindow($"Commit all changed files", msg, "Message");
                _confirmWindow.OnConfirm = () =>
                {
                    try
                    {
                        FossilVCS.AddAndCommit([.. paths], _confirmWindow.UserInput).Forget();
                    }
                    catch (Exception ex)
                    {
                        msg = $"Failed to commit: {ex.Message}";
                        L.Error(ex.Message);
                        L.Error(ex.StackTrace);
                    }
                };

            }
            ImGui.SameLine();
            if (Button("New", buttonSize, "Create new library from current Motherboard code"))
            {
                _confirmWindow = new ConfirmWindow($"Create new library", null, "Title:");
                _confirmWindow.OnConfirm = () =>
                {
                    var title = _confirmWindow.UserInput;
                    var filename = Regexes.CleanInvalidXmlChars(title).SanitizeFilename();
                    var path = Path.Combine(StationSaveUtils.GetSavePathScriptsSubDir().FullName, filename);
                    if (Directory.Exists(path))
                    {
                        var i = 0;
                        while (Directory.Exists($"{path}_{i}"))
                            i++;
                        path = $"{path}_{i}";
                    }

                    L.Debug($"Creating new library at {path}");
                    var newDir = Directory.CreateDirectory(path);
                    var newData = new InstructionData
                    {
                        Title = title,
                        Description = "",
                        Author = NetworkManager.CurrentTransport.IsInitialised ? NetworkManager.Username : "Unknown",
                        Instructions = Window.MotherboardTab[0].Code
                    };
                    L.Debug($"Saving new library data to {newDir.FullName}");
                    newData.SaveToFile(newDir);
                    newData.ItemWrapper = SteamTransport.ItemWrapper.WrapLocalItem(new FileInfo(newDir.FullName + "/instruction.xml"), SteamTransport.WorkshopType.ICCode);
                    var newLib = new VersionedLibrary(newData)
                    {
                        State = FileState.Untracked
                    };
                    LoadLibraryEntry(newLib);
                    LoadLibraries().Forget();
                    _confirmWindow = null;
                };
            }

            ImGui.Text("Filter: ");
            ImGui.SameLine();

            if (Checkbox("Workshop ", ref _showWorkshopItems, "Show subscribed Steam workshop libraries."))
                Search();

            ImGui.SameLine();

            if (Checkbox("Local", ref _showLocalItems, "Show local libraries."))
                Search();

            ImGui.SameLine(); ImGui.Text(" "); ImGui.SameLine();
            ImGui.SeparatorEx(ImGuiSeparatorFlags.Vertical);
            ImGui.SameLine(); ImGui.Text(" "); ImGui.SameLine();

            if (Checkbox("Untracked ", ref _showUntracked, "Show untracked libraries\n  -> no versioned saved yet, red dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Modified ", ref _showModified, "Show modified libraries\n  -> changes since last version detected, yellow dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Unchanged", ref _showUnchanged, "Show unchanged libraries\n  -> no changes since last version, green dot"))
                Search();


            DrawLibrarySearchResults();
            ImGui.SameLine();
            DrawSelectedLibrary();

            if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape))
                IsOpen = false;

            if (_confirmWindow != null)
                _confirmWindow.Draw();

            ImGui.End();

        }

        if (_fileHistoryWindow != null)
            _fileHistoryWindow.Draw();

        if (!IsOpen)
            IsOpen = false;
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

        foreach (var item in items)
        {
            try
            {
                var data = InstructionData.GetFromFile(item.FilePathFullName);
                data.ItemWrapper = item;
                var newLib = new VersionedLibrary(data);
                newLib.UpdateFileState(_fileStates);
                libs.Add(newLib);
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

        Search();
        L.Debug($"\tSearched {sw.ElapsedMilliseconds}ms");
    }

    public static void Open()
    {
        IsOpen = true;
        _librarySearchJustOpened = true;

        ImGui.OpenPopup("Library Search");

        if (_libraryCodes.Count == 0)
            LoadLibraries().Forget();
    }

    public static Editor _previewEditor = null;
    static ConfirmWindow _confirmDeleteLibWindow = null;

    public static void Search()
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
                || (_searchAuthor && lib.Data.Author.ToLowerInvariant().Contains(q))
                || (_searchCode && lib.Data.Instructions.ToLowerInvariant().Contains(q))
            )
            {
                _librarySearchResults.Add(lib);
            }
        }
        UpdateTree();
    }

    public static void UpdateTree()
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
                if (newPrefix == lib.Data.Title)
                    newPrefix = lib.Data.DirectoryPath.Name;
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
        foreach (var node in OuterNodes)
        {
            node.Sort();
            node.UpdateCount();
        }

    }

    public static int LoadLibraryEntry(VersionedLibrary lib, FileVersion version = null)
    {
        if (lib == null)
            return -1;

        var code = version?.Library?.Instructions ?? lib.Data.Instructions;

        if (lib.State == FileState.Workshop)
        {
            Window.SetTab(0);
            Window.ActiveTab.Editors[0].ResetCode(code);
            IsOpen = false;
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
                tab.Editors[0].ResetCode(code);
                IsOpen = false;
                return index;
            }
        }

        var numTabsBefore = tabs.Count;

        try
        {
            var editor = new Editor(Window.ActiveEditor.KeyHandler, lib);
            editor.ResetCode(code);
            Window.Tabs.Add(new EditorTab(Window, editor, lib));
            Window.SetTab(Window.Tabs.Count - 1);
            IsOpen = false;
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
            {
                if (InputText("##title", ref lib.Data.Title, width / 2))
                {
                    lib.Data.SaveToFile(lib.Data.DirectoryPath);
                    LoadLibraries().Forget();
                }
                if (ShowTooltip && ImGui.IsItemHovered())
                    ImGui.SetTooltip("Title, use '|' to separate folder names, e.g.\nMy Folder|A Subfolder|Filename");
            }

            ImGui.SameLine();

            ImGui.SetCursorPosX(width - 2 * buttonSize.x);
            if (Button("Apply", buttonSize, "Save description", isWorkshop))
            {
                lib.Data.SaveToFile(lib.Data.DirectoryPath);
                LoadLibraries().Forget();
                // Search();
            }
            ImGui.SameLine();
            if (Button("Publish", buttonSize, "Publish the library to the workshop"))
                lib.Publish().Forget();

            var pos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(buttonPos);
            if (Button("History", buttonSize, "View the library's history"))
            {
                _fileHistoryWindow = new FileHistoryWindow(lib);
                _fileHistoryWindow.Open();
            }
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

            var numLines = Mathf.Clamp(lib.Data.Description.Split('\n').Length, 2, 5);
            var height = (numLines - 1) * LineHeightWithSpacing + LineHeight;
            ImGui.InputTextMultiline("", ref lib.Data.Description, 1024, new Vector2(width, height), isWorkshop ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);

            if (ShowTooltip && ImGui.IsItemHovered())
                ImGui.SetTooltip("Description, click 'Apply' to apply changes");

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

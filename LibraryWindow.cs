// todo: fix drag and drop
// todo: check rename
namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Assets.Scripts.Networking;
using Assets.Scripts.Networking.Transports;
using Assets.Scripts.UI;
using Assets.Scripts.Util;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;
using static Settings;

public class VersionedScript(InstructionData data)
{
    public string Path => Data.DirectoryPath.FullName;
    public string Title => Data.Title.Split(PathSeparator[0]).Last();
    public InstructionData Data = data;
    public FileState State = FileState.Untracked;
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

    public async UniTaskVoid UpdateFileState()
    {
        L.Debug($"Updating file state for library: {Data.DirectoryPath.Name}, State: {State}");
        State = await FossilVCS.GetFileState(Data.DirectoryPath.Name + "/instruction.xml");
        L.Debug($"File state for library {Data.DirectoryPath.Name}: {State}");
    }

    public void Save()
    {
        Data.SaveToFile(Data.DirectoryPath);
        LibraryWindow.NeedsReload(this);
    }

    public async UniTask Publish()
    {
        try
        {
            await Data.PublishToWorkshop();
        }
        finally
        {
            await LibraryWindow.LoadScripts();
        }
    }

    public string Tooltip
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Name:           {LibNode.GetName(Data.Title)}");
            sb.AppendLine($"Author:         {Data.Author}");
            sb.AppendLine($"Filename:       {Data.DirectoryPath.Name}/instruction.xml");
            sb.AppendLine($"Last Modified:  {Date}");
            sb.AppendLine($"Version Status: {State}");
            sb.AppendLine($"Description:    {Data.Description}");
            sb.AppendLine($"\nRight click for more options");
            if (State == FileState.Untracked)
            {
                sb.AppendLine("");
                sb.AppendLine("This script is not tracked by version control yet!");
                sb.AppendLine("Consider clicking 'Commit All' at the top right to make a snapshot of all scripts.");
            }
            if (State == FileState.Modified)
            {
                sb.AppendLine("");
                sb.AppendLine("This script has changed since the last version");
            }
            return sb.ToString();
        }
    }
}


public class LibNode
{
    private static LibNode _draggedNode = null;
    public string Name;
    public List<LibNode> Children = new List<LibNode>();
    public VersionedScript Script = null;
    public string Prefix = "";
    public int Count = 0;

    public bool IsScript => Script != null;
    public bool IsFolder => Script == null;
    public string FullName => Prefix + (Prefix.Length > 0 ? LibraryWindow._dirSeparator : "") + Name;

    public LibNode(string fullName)
    {
        Name = GetName(fullName);
        Prefix = GetPrefix(fullName);
    }

    public static string GetPrefix(string name)
    {
        var lastSep = name.LastIndexOf(LibraryWindow._dirSeparator);
        return lastSep == -1 ? "" : name.Substring(0, lastSep);
    }

    public static string GetName(string name)
    {
        var lastSep = name.LastIndexOf(LibraryWindow._dirSeparator);
        return lastSep == -1 ? name : name.Substring(lastSep + 1);
    }

    public static string Combine(string prefix, string name)
    {
        if (string.IsNullOrEmpty(prefix))
            return name;
        return prefix + LibraryWindow._dirSeparator + name;
    }


    public void Add(LibNode node)
    {
        Children.Add(node);
    }

    public void Rename(string newName)
    {
        if (IsFolder)
        {
            LibraryWindow.Folders.Remove(FullName);
            LibraryWindow.Folders.Add(newName);
        }
        if (IsScript)
        {
            Script.Data.Title = newName;
            Script.Data.SaveToFile(Script.Data.DirectoryPath);
        }
        Name = GetName(newName);
        Prefix = GetPrefix(newName);
        foreach (var child in Children)
            child.MoveTo(this);
    }

    public void Copy(string newName = null)
    {
        if (!IsScript)
            return;

        newName ??= FullName + " (Copy)";
        L.Debug($"Copying script: {FullName} to {newName}");

        var oldPath = Script.Data.DirectoryPath.FullName;
        var newData = InstructionData.GetFromFile(oldPath + "/instruction.xml");
        newData.Title = newName;
        var count = 0;
        var newPath = oldPath + "_";
        while (Directory.Exists(newPath + $"{count}"))
            count++;
        var newDir = Directory.CreateDirectory(newPath + $"{count}");
        newData.SaveToFile(newDir);
    }

    public void Delete()
    {
        if (Script != null)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            InputSourceCode.DeleteInstruction(Script.Data.DirectoryPath.Name);
            L.Debug($"Delete: {FullName} took {sw.ElapsedMilliseconds}ms");
        }
        if (IsFolder)
            LibraryWindow.Folders.Remove(FullName);
        foreach (var child in Children)
            child.Delete();
    }

    public List<string> GetAllScriptPaths()
    {
        var paths = new List<string>();
        if (IsScript)
            paths.Add(FullName.Replace(PathSeparator, "/"));
        foreach (var child in Children)
            paths.AddRange(child.GetAllScriptPaths());
        return paths;
    }

    private void MoveTo(LibNode target, string prefix = null)
    {
        if (target == this || (IsFolder && target.FullName.StartsWith(FullName)))
            return;

        if (IsScript)
        {
            var newTitle = Combine(target.IsScript ? target.Prefix : target.FullName, Combine(prefix, Name));
            if (Script.Data.Title != newTitle)
            {
                L.Debug($"Moving library {Script.Data.Title} to {newTitle}");
                Script.Data.Title = newTitle;
                Script.Save();
                LibraryWindow.NeedsUpdate();
            }
            return;
        }

        if (IsFolder)
            LibraryWindow.Folders.Remove(FullName);

        prefix = Combine(prefix, Name);

        foreach (var child in Children)
            child.MoveTo(target, prefix);
    }

    public void Draw(bool treeView = true)
    {
        if (IsFolder && LibraryWindow.HasFilter && Count == 0)
            return;
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        var isSelected = IsScript && LibraryWindow.SelectedNode == this;
        var imguiLabel = $"{Name}##{FullName}_" + (IsScript ? "lib" : "dir");

        flags |= isSelected ? ImGuiTreeNodeFlags.Selected : 0;
        flags |= (IsFolder && treeView) ? 0 : ImGuiTreeNodeFlags.Leaf;
        if (FullName == "")
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        var isOpen = treeView && ImGui.TreeNodeEx(imguiLabel, flags);
        if (!treeView && IsScript)
            ImGui.Selectable(" " + FullName.Replace("|", "/"), isSelected);

        if (ImGui.BeginPopupContextItem())
        {
            var prefix = IsScript ? Prefix : FullName;
            LibraryWindow.SelectLibrary(this);
            var type = IsScript ? "Script" : "Folder";
            ImGui.Text($"Edit {type} '{Name}'");
            ImGui.Separator();
            if (ImGui.Selectable("Rename"))
                LibraryWindow.Rename(this);
            if (IsScript)
            {
                if (Script.State != FileState.Unchanged && ImGui.Selectable("Commit"))
                    LibraryWindow.Commit(this);
                if (ImGui.Selectable("History"))
                    LibraryWindow.OpenHistory(this);
                if (ImGui.Selectable("Copy"))
                    LibraryWindow.Copy(this);
                if (ImGui.Selectable("Load"))
                    LibraryWindow.LoadScript(Script);
            }
            if (ImGui.Selectable("Delete"))
                LibraryWindow.Delete(this);
            ImGui.Separator();
            if (ImGui.Selectable("New Folder"))
                LibraryWindow.CreateFolder(prefix);
            if (ImGui.Selectable("New Script"))
                LibraryWindow.CreateScript(prefix);
            ImGui.EndPopup();
        }

        if (treeView && ImGui.BeginDragDropSource())
        {
            _draggedNode = this;
            ImGui.SetDragDropPayload("LibNode", "folder");
            ImGui.TextUnformatted(Name);
            ImGui.EndDragDropSource();
        }

        if (treeView && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("LibNode");
                if (payload.NativePtr != null && payload.Delivery)
                {
                    L.Debug($"Dragging {_draggedNode.FullName} -> {FullName}");
                    _draggedNode.MoveTo(this);
                }
            }
            ImGui.EndDragDropTarget();
        }

        if (IsFolder && treeView)
        {
            ImGui.SameLine();
            ImGui.Text($"({Count})");
        }

        if (IsScript)
        {
            var radius = 5.0f; ;
            var imSize = 0.9f * LineHeight;
            var imPos = ImGui.GetCursorScreenPos() - new Vector2(0, 0.6f * LineHeight);

            if (Script.State == FileState.Workshop)
            {
                var texPtr = ImGuiManager.ImGuiPointerFor(WorkshopMenu.Instance.SteamImage.texture);
                imPos -= new Vector2(imSize / 2, 0.01f * LineHeight + 0.5f * imSize);
                ImGui.GetWindowDrawList().AddImage(texPtr, imPos, imPos + new Vector2(imSize, imSize));
            }
            else
                ImGui.GetWindowDrawList().AddCircleFilled(imPos, radius, Script.Color, 12);

            if (ImGui.IsItemHovered())
            {
                if (ShowTooltip)
                    ImGui.SetTooltip(Script.Tooltip);

                if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                    LibraryWindow.SelectLibrary(this);

                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    LibraryWindow.LoadScript(Script);
            }
        }

        if (isOpen || !treeView)
        {
            foreach (var child in Children)
                child.Draw(treeView);

            if (treeView)
                ImGui.TreePop();
        }
    }

    public void Sort()
    {
        // first sort by leaf status, then by name
        Children.Sort((a, b) => a.IsScript.CompareTo(b.IsScript) != 0 ? a.IsScript.CompareTo(b.IsScript) : string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        foreach (var child in Children)
            child.Sort();
    }

    public int UpdateCount()
    {
        Count = IsScript ? 1 : 0;
        foreach (var child in Children)
            Count += child.UpdateCount();
        return Count;
    }
}

public static class LibraryWindow
{
    public static bool IsOpen = false;
    public static List<VersionedScript> VersionedScripts = [];
    public static LibNode Root = null;

    public static List<VersionedScript> _SearchResults = [];
    public static HashSet<string> Folders = [];

    public static LibNode SelectedNode = null;

    public static EditorWindow Window;

    public static bool HasFilter => _hasFilter;

    private static string _searchText = "";
    private static bool _hasWindowJustOpened = false;
    private static bool _searchInCode = false;
    private static bool _searchInAuthor = false;
    private static bool _showWorkshop = true;
    private static bool _showLocal = true;
    private static bool _showUntracked = true;
    private static bool _showModified = true;
    private static bool _showUnchanged = true;
    private static bool _hasFilter = false;
    public static char _dirSeparator = '|';

    private static bool _treeView = true;

    private static ConfirmWindow _confirmWindow = null;
    // private static ConfirmWindow _confirmDeleteLibWindow = null;
    private static bool _needsUpdate = false;
    private static bool _needsReloadAll = false;
    private static readonly List<VersionedScript> _scriptsToReload = [];
    private static FileHistoryWindow _fileHistoryWindow = null;
    private static Editor _previewEditor = null;


    public static void NeedsUpdate()
    {
        _needsUpdate = true;
    }

    public static void NeedsReload(VersionedScript script = null)
    {
        if (script == null)
            _needsReloadAll = true;
        else
            _scriptsToReload.Add(script);
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
            ImGui.Begin("Library", ref IsOpen)
        )
        {
            using var _ = new ScopedStyleColor(ImGuiCol.FrameBg, ICodeFormatter.ColorFromVector4(0.2f, 0.2f, 0.2f, 1.0f));
            if (_hasWindowJustOpened)
            {
                ImGui.SetKeyboardFocusHere();
                _hasWindowJustOpened = false;
            }

            var width = ImGui.GetContentRegionAvail().x;

            ImGui.Text("Search: ");
            ImGui.SameLine();
            var oldSearchText = _searchText;
            InputText(
                "##LibrarySearch",
                ref _searchText,
                30 * CharWidth
            );

            if (ImGui.IsItemHovered() && ShowTooltip)
                ImGui.SetTooltip("Load first matching entry with Enter key, or any entry with double-click.");

            if (oldSearchText != _searchText)
                Search();

            // Load first entry when Enter pressed
            if (
                (ImGui.IsItemDeactivatedAfterEdit()
                || ImGui.IsItemFocused()) && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
                && _SearchResults.Count > 0)
                LoadScript(_SearchResults[0]);

            ImGui.SameLine();

            if (Checkbox("Code", ref _searchInCode, "Full-text search in code"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Author", ref _searchInAuthor, "Search author names"))
                Search();

            ImGui.SameLine(); ImGui.Text("  "); ImGui.SameLine();

            Checkbox("Tree View", ref _treeView, "Show Folders in tree view");


            ImGui.SameLine();
            ImGui.SetCursorPosX(width - 3 * largeButtonSize.x - 2 * ImGui.GetStyle().ItemSpacing.x);
            if (Button("Commit All", largeButtonSize, "Commit all files/changes to version control"))
                CommitAll();
            ImGui.SameLine();
            if (Button("New Folder", largeButtonSize, "Create new root folder\nUse right-click in the tree view for nested folders"))
                CreateFolder();

            ImGui.SameLine();
            if (Button("New Script", largeButtonSize, "Create new script from current Motherboard code"))
                CreateScript();

            ImGui.Text("Filter: ");
            ImGui.SameLine();

            if (Checkbox("Workshop ", ref _showWorkshop, "Show subscribed Steam workshop scripts."))
                Search();

            ImGui.SameLine();

            if (Checkbox("Local", ref _showLocal, "Show local scripts."))
                Search();

            ImGui.SameLine(); ImGui.Text(" "); ImGui.SameLine();
            ImGui.SeparatorEx(ImGuiSeparatorFlags.Vertical);
            ImGui.SameLine(); ImGui.Text(" "); ImGui.SameLine();

            if (Checkbox("Untracked ", ref _showUntracked, "Show untracked scripts\n  -> no versioned saved yet, red dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Modified ", ref _showModified, "Show modified scripts\n  -> changes since last version detected, yellow dot"))
                Search();

            ImGui.SameLine();

            if (Checkbox("Unchanged", ref _showUnchanged, "Show unchanged scripts\n  -> no changes since last version, green dot"))
                Search();


            DrawLibrarySearchResults();
            ImGui.SameLine();
            DrawSelectedLibrary();

            if (ImGui.IsWindowFocused() && ImGui.IsKeyPressed(ImGuiKey.Escape))
                IsOpen = false;

            _confirmWindow?.Draw();

            ImGui.End();
        }

        _fileHistoryWindow?.Draw();

        if (!IsOpen)
            IsOpen = false;

        if (_needsReloadAll || _scriptsToReload.Count > 2)
        {
            _needsReloadAll = false;
            _scriptsToReload.Clear();
            LoadScripts().Forget();
        }

        if (_scriptsToReload.Count > 0)
        {
            foreach (var script in _scriptsToReload)
                script.UpdateFileState().Forget();
            _scriptsToReload.Clear();
        }
    }

    public static void CommitAll()
    {
        var paths = new List<string>();
        var names = new List<string>();
        foreach (var lib in VersionedScripts)
        {
            if (lib.State == FileState.Modified || lib.State == FileState.Untracked)
            {
                paths.Add(lib.Data.DirectoryPath.Name);
                names.Add(lib.Data.Title.Replace(_dirSeparator, '/'));
            }
        }
        var msg = "Files to be commited: \n" + string.Join("\n", names);
        _confirmWindow = new ConfirmWindow($"Commit all changed files", msg, "Message");
        _confirmWindow.OnConfirm = () =>
        {
            FossilVCS.AddAndCommit([.. paths], _confirmWindow.UserInput).Forget();
        };
    }

    public static void CreateFolder(string prefix = null)
    {
        L.Debug($"CreateFolder: prefix={prefix}");
        _confirmWindow = new ConfirmWindow($"Create Folder", null, "Folder Name");
        _confirmWindow.OnConfirm = () =>
        {
            var name = LibNode.Combine(prefix, _confirmWindow.UserInput);
            L.Warning($"Create Folder: {name}, not implemented!");
            Folders.Add(name);
            NeedsUpdate();
        };
    }

    public static void CreateScript(string prefix = null)
    {
        _confirmWindow = new ConfirmWindow($"Create Script", null, "Script Name");
        _confirmWindow.OnConfirm = () =>
        {
            var name = LibNode.Combine(prefix, _confirmWindow.UserInput);
            var filename = Regexes.CleanInvalidXmlChars(name).SanitizeFilename();
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
                Title = name,
                Description = "",
                Author = NetworkManager.CurrentTransport.IsInitialised ? NetworkManager.Username : "Unknown",
                Instructions = Window.MotherboardTab[0].Code
            };
            L.Debug($"Saving new library data to {newDir.FullName}");
            newData.SaveToFile(newDir);
            newData.ItemWrapper = SteamTransport.ItemWrapper.WrapLocalItem(new FileInfo(newDir.FullName + "/instruction.xml"), SteamTransport.WorkshopType.ICCode);
            var newLib = new VersionedScript(newData)
            {
                State = FileState.Untracked
            };
            LoadScript(newLib);
            LoadScripts().Forget();
            _confirmWindow = null;
        };
    }

    public static void OpenHistory(LibNode node)
    {
        if (node == null || !node.IsScript)
            return;
        _fileHistoryWindow = new FileHistoryWindow(node.Script);
        _fileHistoryWindow.Open();
    }

    public static void Commit(LibNode node)
    {
        if (node == null || !node.IsScript)
            return;

        var script = node.Script;

        _confirmWindow = new ConfirmWindow($"Commit {script.Path}", null, "Message:");
        _confirmWindow.OnConfirm = () =>
        {
            var msg = _confirmWindow.UserInput;
            script.Save();
            FossilVCS.AddAndCommit([script.Data.DirectoryPath.Name], _confirmWindow.UserInput).ContinueWith(() =>
            {
                NeedsReload(node.Script);
            });
        };
    }

    public static void Copy(LibNode node)
    {
        node.Copy();
        LoadScripts().Forget();
    }

    public static void Rename(LibNode node)
    {
        var oldName = node.FullName;
        _confirmWindow = new ConfirmWindow($"Rename {node.Name}", null, "New Name");
        _confirmWindow.OnConfirm = () =>
        {
            var newName = LibNode.Combine(node.Prefix, _confirmWindow.UserInput);
            if (newName != node.FullName)
            {
                L.Debug($"Rename: old={oldName} new={newName}");
                node.Rename(newName);
                NeedsUpdate();
            }
        };
    }

    public static void Delete(LibNode node)
    {
        if (node.IsFolder && node.Children.Count == 0)
        {
            node.Delete();
            return;
        }
        var type = node.IsFolder ? "Folder" : "Script";
        var msg = $"Are you sure?\nThis will delete following scripts:\n\n  {string.Join("\n  ", node.GetAllScriptPaths())}";
        msg += $"\n\nNote: At game startup a backup is stored at\n  {FossilVCS.BackupDir}\n ";
        _confirmWindow = new ConfirmWindow($"Delete {type}: {node.Name}", msg);
        _confirmWindow.OnConfirm = () =>
        {
            // temporarily disable vanilla scripts reloading while deleting files
            var helpWindow = ScriptHelpWindow.ScriptLibraryWindow;
            var helpMode = helpWindow.HelpMode;
            helpWindow.HelpMode = HelpMode.None;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            node.Delete();
            L.Debug($"Delete: {node.FullName} took {sw.ElapsedMilliseconds}ms");
            LoadScripts().Forget();
            SelectedNode = null;
            _previewEditor = null;
            _confirmWindow = null;
            helpWindow.HelpMode = helpMode;
            helpWindow.ReloadFileList();
        };
    }
    public static void SelectLibrary(LibNode node)
    {
        if (node.IsFolder)
            return;
        SelectedNode = node;
        if (node == null)
            return;
        if (_previewEditor == null)
        {
            _previewEditor = new Editor(Window.ActiveEditor.KeyHandler, node.Script);
            _previewEditor.IsReadOnly = true;
        }
        _previewEditor.ResetCode(node.Script.Data.Instructions ?? "", false);
    }


    public static async UniTask LoadScripts()
    {
        await UniTask.SwitchToThreadPool();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var items = await NetworkManager.GetLocalAndWorkshopItems(
            SteamTransport.WorkshopType.ICCode
        );
        L.Debug($"\tGet {sw.ElapsedMilliseconds}ms");

        var fileStates = await FossilVCS.GetFileStates();
        L.Debug($"\tFileStates {sw.ElapsedMilliseconds}ms");

        var libs = new List<VersionedScript>();

        foreach (var item in items)
        {
            try
            {
                var data = InstructionData.GetFromFile(item.FilePathFullName);
                data.ItemWrapper = item;
                var newLib = new VersionedScript(data);
                newLib.UpdateFileState(fileStates);
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
        VersionedScripts = libs;

        Search();
        L.Debug($"\tSearched {sw.ElapsedMilliseconds}ms");
    }

    public static void Open()
    {
        IsOpen = true;
        _hasWindowJustOpened = true;

        ImGui.OpenPopup("Library Search");

        if (VersionedScripts.Count == 0)
            LoadScripts().Forget();
    }

    public static void Search()
    {
        _SearchResults.Clear();

        _hasFilter = !string.IsNullOrEmpty(_searchText) || !_showWorkshop || !_showLocal || !_showUntracked || !_showModified || !_showUnchanged;
        if (!HasFilter)
        {
            _SearchResults = [.. VersionedScripts];
            UpdateTree();
            return;
        }

        var q = _searchText.Trim().ToLowerInvariant();

        foreach (var lib in VersionedScripts)
        {
            if (lib.IsWorkshop && !_showWorkshop)
                continue;
            if (!lib.IsWorkshop && !_showLocal)
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
                || (_searchInAuthor && lib.Data.Author.ToLowerInvariant().Contains(q))
                || (_searchInCode && lib.Data.Instructions.ToLowerInvariant().Contains(q))
            )
            {
                _SearchResults.Add(lib);
            }
        }
        UpdateTree();
    }

    public static void UpdateTree()
    {
        // Step 1: Collect all appearing folder names
        foreach (var lib in _SearchResults)
        {
            var index = lib.Data.Title.IndexOf(_dirSeparator);
            while (index >= 0)
            {
                Folders.Add(lib.Data.Title.Substring(0, index));
                index = lib.Data.Title.IndexOf(_dirSeparator, index + 1);
            }
        }

        // Step 2: Build the folder tree
        var folderMap = new Dictionary<string, LibNode>();
        Root = new LibNode("");
        folderMap[""] = Root;

        foreach (var folder in Folders.OrderBy(f => f))
        {
            folderMap[folder] = new LibNode(folder);
            folderMap[LibNode.GetPrefix(folder)].Add(folderMap[folder]);
        }

        // Step 3: Add scripts to their respective folders
        foreach (var lib in _SearchResults)
            folderMap[LibNode.GetPrefix(lib.Data.Title)].Add(new LibNode(lib.Data.Title) { Script = lib });

        Root.Sort();
        Root.UpdateCount();
    }

    public static int LoadScript(VersionedScript lib, FileVersion version = null)
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
            if (tab.Script == null)
                continue;
            if (tab.Script.Path == lib.Path)
            {
                L.Info($"Library {lib.Path} already open, switching to tab");
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

        if (_SearchResults.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1), "No results found.");
            return;
        }

        if (Root != null)
        {
            if (_treeView)
                ImGui.Unindent(10.0f);
            else
                ImGui.Indent(5.0f);
            Root.Draw(_treeView);
        }
    }

    public static void DrawSelectedLibrary()
    {

        using var pane = new Pane("LibrarySearchPreview");

        if (SelectedNode != null)
        {
            var width = ImGui.GetContentRegionAvail().x;
            var script = SelectedNode.Script;
            var buttonPos = ImGui.GetCursorPos() + new Vector2(width - 3 * buttonSize.x - 2 * ImGui.GetStyle().ItemSpacing.x, 0);
            Text($"Author: {script.Data.Author}");
            Text($"Date:   {script.Date}");
            Text($"Name: ", width / 2);
            ImGui.SameLine();

            var isWorkshop = script.State == FileState.Workshop;

            if (isWorkshop)
                ImGui.Text(script.Data.Title);
            else
            {
                var name = LibNode.GetName(script.Data.Title);
                if (InputText("##name", ref name, width / 2))
                {
                    SelectedNode.Rename(LibNode.Combine(SelectedNode.Prefix, name));
                    LoadScripts().Forget();
                }
            }

            ImGui.SameLine();

            ImGui.SetCursorPosX(width - 2 * buttonSize.x);
            if (Button("Apply", buttonSize, "Save description", isWorkshop))
                script.Save();

            ImGui.SameLine();
            if (Button("Publish", buttonSize, "Publish the script to the Steam Workshop"))
                script.Publish().Forget();

            var pos = ImGui.GetCursorPos();
            ImGui.SetCursorPos(buttonPos);
            if (Button("History", buttonSize, "Browse old versions of this script"))
                OpenHistory(SelectedNode);
            ImGui.SameLine();
            if (Button("Delete", buttonSize, "Delete the script"))
                Delete(SelectedNode);

            ImGui.SameLine();
            if (Button("Load", buttonSize, "Load this library into the editor"))
                LoadScript(script);

            ImGui.SetCursorPos(pos);

            var numLines = Mathf.Clamp(script.Data.Description.Split('\n').Length, 2, 5);
            var height = (numLines - 1) * LineHeightWithSpacing + LineHeight;
            ImGui.InputTextMultiline("", ref script.Data.Description, 1024, new Vector2(width, height), isWorkshop ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);

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

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
    public FileState State = FileState.Unknown;
    static readonly Dictionary<FileState, uint> Colors = new()
    {
        { FileState.Unknown, ICodeFormatter.ColorFromHTML("gray") },
        { FileState.Untracked, ICodeFormatter.ColorFromHTML("red") },
        { FileState.Unchanged, ICodeFormatter.ColorFromHTML("green") },
        { FileState.Modified, ICodeFormatter.ColorFromHTML("yellow") },
        { FileState.Workshop, ICodeFormatter.ColorFromHTML("white") },
    };
    static readonly Dictionary<FileState, string> Statuses = new() {
        { FileState.Unknown, "Loading..." },
        { FileState.Untracked, "Untracked" },
        { FileState.Unchanged, "Unchanged" },
        { FileState.Modified, "Modified" },
        { FileState.Workshop, "Subscribed from Workshop" },
    };

    public uint Color => Colors[State];
    public string StatusString => Statuses[State];
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

    public async UniTask UpdateFileState()
    {
        var oldState = State;
        State = await FossilVCS.GetFileState(Data.DirectoryPath.Name + "/instruction.xml");
        L.Debug($"File state for library {Data.DirectoryPath.FullName}: {oldState} -> {State}");
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
            var (success, workshopId) = await Data.PublishToWorkshop();
            if (success)
            {
                Data.WorkshopFileHandle = workshopId;
                Save();
            }
        }
        finally
        {
            LibraryWindow.NeedsReload(this);
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
            sb.AppendLine($"Workshop ID:    {Data.WorkshopFileHandle}");
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
            LibraryWindow.NeedsReload(Script);
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
        var newPath = oldPath + "_";

        if (Script.State == FileState.Workshop)
        {
            newData.WorkshopFileHandle = 0;
            newName = Script.Title + " (Copy)";
            var filename = Regexes.CleanInvalidXmlChars(newName).SanitizeFilename();
            newPath = Path.Combine(StationSaveUtils.GetSavePathScriptsSubDir().FullName, filename);
        }

        newData.Title = newName;
        if (Directory.Exists(newPath))
        {
            newPath += "_";
            var count = 0;
            while (Directory.Exists(newPath + $"{count}"))
                count++;
            newPath += $"{count}/";
        }
        var newDir = Directory.CreateDirectory(newPath);
        newData.SaveToFile(newDir);
    }

    public void Delete()
    {
        if (Script != null)
        {
            InputSourceCode.DeleteInstruction(Script.Data.DirectoryPath.Name);
            LibraryWindow.Window.CloseTab(LibraryWindow.GetTabIndexForScript(Script));
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
            paths.Add(FullName.Replace(PathSeparator, "/") + "\n    " + Script.Path + "\n");
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
        if (Name == null)
            return;
        if (IsFolder && LibraryWindow.HasFilter && Count == 0)
            return;
        var flags = ImGuiTreeNodeFlags.SpanAvailWidth;
        var isSelected = IsScript && LibraryWindow.SelectedNode == this;
        var imguiId = IsScript ? Script.Path : FullName;
        var imguiLabel = $"{Name}##{imguiId}";

        flags |= isSelected ? ImGuiTreeNodeFlags.Selected : 0;
        flags |= (IsFolder && treeView) ? 0 : ImGuiTreeNodeFlags.Leaf;
        if (FullName == "")
            flags |= ImGuiTreeNodeFlags.DefaultOpen;

        if (!treeView && IsScript)
            ImGui.Selectable("  " + FullName.Replace("|", "/"), isSelected);

        var isOpen = treeView && ImGui.TreeNodeEx(imguiLabel, flags);

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
                if (Script.State != FileState.Workshop)
                {
                    if (ImGui.Selectable("Commit"))
                        LibraryWindow.Commit(this);
                    if (ImGui.Selectable("History"))
                        LibraryWindow.OpenHistory(this);
                }
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

        if (treeView && (!IsScript || Script.State != FileState.Workshop) && ImGui.BeginDragDropSource())
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
            var showSteamLogo = Script.Data.WorkshopFileHandle != 0 || Script.State == FileState.Workshop;
            var hasVersion = Script.State != FileState.Workshop;

            var imSize = 0.9f * LineHeight;
            var radius = 5.0f;

            var imPos = ImGui.GetCursorScreenPos() - new Vector2(0, 0.6f * LineHeight);
            if (!treeView)
                imPos.x += 0.8f * CharWidth;

            if (showSteamLogo)
            {
                var texPtr = ImGuiManager.ImGuiPointerFor(WorkshopMenu.Instance.SteamImage.texture);
                var pos = imPos - new Vector2(imSize / 2, 0.01f * LineHeight + 0.5f * imSize);
                var color = hasVersion ? Script.Color : 0xFFFFFFFF;
                ImGui.GetWindowDrawList().AddImage(texPtr, pos, pos + new Vector2(imSize, imSize), color);
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
    public static List<VersionedScript> LocalScripts = [];
    public static List<VersionedScript> VersionedScripts = [];
    public static List<VersionedScript> WorkshopScripts = [];
    public static Dictionary<string, VersionedScript> VersionedScriptsByPath = [];
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
    private static bool _isLoadingWorkshopScripts = false;
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

        SetImGuiWindowCollapsed();
        ImGui.Begin("Library", ref IsOpen);
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
        ImGui.SetCursorPosX(width - 4 * largeButtonSize.x - 3 * ImGui.GetStyle().ItemSpacing.x);
        if (Button("Reload", largeButtonSize, "Reload all scripts"))
            NeedsReload();
        ImGui.SameLine();
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

        _fileHistoryWindow?.Draw();

        if (!IsOpen)
            IsOpen = false;

        if (_needsReloadAll || _scriptsToReload.Count > 0)
            NeedsUpdate();

        if (_needsReloadAll || _scriptsToReload.Count > 2)
        {
            _needsReloadAll = false;
            _scriptsToReload.Clear();
            LoadScripts().Forget();
        }

        foreach (var script in _scriptsToReload)
            script.UpdateFileState().Forget();
        _scriptsToReload.Clear();
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
            // L.Warning($"Create Folder: {name}, not implemented!");
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
            NeedsReload();
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
            CommitAsync(script, msg).Forget();
        };
    }

    public static async UniTask CommitAsync(VersionedScript script, string msg)
    {
        // await UniTask.SwitchToThreadPool();
        await FossilVCS.AddAndCommit([script.Data.DirectoryPath.Name], msg);
        await UniTask.SwitchToMainThread();
        NeedsReload(script);
    }

    public static void Copy(LibNode node)
    {
        node.Copy();
        NeedsReload();
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
            NeedsUpdate();
            return;
        }
        var type = node.IsFolder ? "Folder" : "Script";
        var msg = $"Are you sure?\nThis will delete following scripts:\n\n  {string.Join("\n  ", node.GetAllScriptPaths())}";
        msg += $"\n\nNote: At game startup a backup is stored at\n  {FossilVCS.BackupDir}\n ";
        _confirmWindow = new ConfirmWindow($"Delete {type}: {node.Name}", msg);
        _confirmWindow.OnConfirm = () =>
        {
            // temporarily disable vanilla scripts reloading while deleting files (this takes a looong time otherwise...)
            var helpWindow = ScriptHelpWindow.ScriptLibraryWindow;
            var helpMode = helpWindow.HelpMode;
            helpWindow.HelpMode = HelpMode.None;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            node.Delete();
            L.Debug($"Delete: {node.FullName} took {sw.ElapsedMilliseconds}ms");
            NeedsReload();
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

    public static List<VersionedScript> LoadLocalScripts()
    {
        L.Debug($"LoadLocalScripts");
        var itemType = SteamTransport.WorkshopType.ICCode;
        var localDirInfo = itemType.GetLocalDirInfo();
        var fileName = itemType.GetLocalFileName();
        var items = new List<VersionedScript>();
        if (localDirInfo.Exists)
        {
            foreach (var f in localDirInfo.GetDirectories("*", SearchOption.AllDirectories).SelectMany((DirectoryInfo d) => d.GetFiles()))
                if (f.Name == fileName)
                {
                    try
                    {
                        var instructionData = InstructionData.GetFromFile(f.FullName);
                        instructionData.ItemWrapper = SteamTransport.ItemWrapper.WrapLocalItem(f, itemType);
                        if (instructionData != null)
                        {
                            var script = new VersionedScript(instructionData);
                            items.Add(script);
                        }
                        else
                            L.Warning($"Failed to load script {f.FullName}: instructionData is null");
                    }
                    catch (Exception e)
                    {
                        L.Error($"Failed to load script {f.FullName}: {e}");
                    }
                }
        }
        return items;
    }

    public static async UniTask<bool> LoadWorkshopScripts(uint page = 1u)
    {
        if (_isLoadingWorkshopScripts)
            return false;

        _isLoadingWorkshopScripts = true;

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var delayTask = UniTask.Delay(10000);
            var task = SteamTransport.Workshop_QueryItemsAsync(SteamTransport.WorkshopType.ICCode, page);
            var (finished, result) = await UniTask.WhenAny(task, delayTask);
            if (!finished)
                L.Warning("Workshop script loading is taking longer than 10 seconds...");

            var items = (finished ? result : await task).ToList();
            var elapsed = sw.ElapsedMilliseconds;

            if (finished)
                L.Debug($"Loaded {items.Count} workshop scripts at page {page} in {elapsed}ms");
            else
                L.Info($"Loaded {items.Count} workshop scripts at page {page} in {elapsed}ms");

            var newWorkshopScripts = new List<VersionedScript>();
            foreach (var item in result)
            {
                var instructionData = InstructionData.GetFromFile(item.FilePathFullName);
                instructionData.ItemWrapper = item;
                instructionData.WorkshopFileHandle = item.Id;
                var script = new VersionedScript(instructionData);
                script.State = FileState.Workshop;
                script.Data.Title = "Workshop" + _dirSeparator + script.Title;
                newWorkshopScripts.Add(script);
            }
            if (page == 1)
            {
                WorkshopScripts = newWorkshopScripts;
                NeedsUpdate();
                return true;
            }
            else if (newWorkshopScripts.Count > 0)
            {
                WorkshopScripts.AddRange(newWorkshopScripts);
                NeedsUpdate();
                return true;
            }
            return false;
        }
        catch (Exception ex)
        {
            L.Error($"Failed to load workshop scripts: {ex.Message}");
            L.Error($"Stack trace: {ex.StackTrace}");
        }
        finally
        {
            _isLoadingWorkshopScripts = false;
        }
        return false;
    }

    public static async UniTask LoadScripts()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        uint page = 1;
        var loadWorkshopTask = LoadWorkshopScripts(page);
        LocalScripts = LoadLocalScripts();
        L.Debug($"\tLoadLocalScripts {sw.ElapsedMilliseconds}ms");

        Search();
        L.Debug($"\tSearched {sw.ElapsedMilliseconds}ms");

        await UniTask.SwitchToThreadPool();
        var fileStates = await FossilVCS.GetFileStates();
        await UniTask.SwitchToMainThread();
        foreach (var script in LocalScripts)
            script.UpdateFileState(fileStates);

        var workshopDone = await loadWorkshopTask;
        while (workshopDone)
            workshopDone = await LoadWorkshopScripts(++page);

        // iterate over all scripts in all editor tabs and replace the VersionedScript reference with the newly loaded one (to update file states and workshop info)
        foreach (var tab in Window.Tabs)
            foreach (var editor in tab.Editors)
                if (editor.Library != null)
                {
                    var path = editor.Library.Path;
                    if (VersionedScriptsByPath.TryGetValue(path, out var updatedScript))
                    {
                        editor.Target = updatedScript;
                        if (editor.IsReadOnly)
                            editor.ResetCode(updatedScript.Data.Instructions ?? "", false);
                    }
                }
    }

    public static void Open()
    {
        if (IsOpen == false)
        {
            LocalScripts = [];
            WorkshopScripts = [];
            _SearchResults = [];
            NeedsReload();
        }

        IsOpen = true;
        _hasWindowJustOpened = true;

        ImGui.OpenPopup("Library Search");
    }

    public static void Search()
    {
        List<VersionedScript> libs = [.. LocalScripts, .. WorkshopScripts];

        libs.Sort((a, b) => a.Data.Title.ToLowerInvariant().CompareTo(b.Data.Title.ToLowerInvariant()));
        VersionedScripts = libs;

        VersionedScriptsByPath.Clear();
        foreach (var script in VersionedScripts)
            VersionedScriptsByPath[script.Path] = script;
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

    public static int GetTabIndexForScript(VersionedScript lib)
    {
        for (var i = 0; i < Window.Tabs.Count; i++)
            if (Window.Tabs[i].FilePath == lib.Path)
                return i;

        return -1;
    }

    public static int LoadScript(VersionedScript lib, FileVersion version = null, bool toMotherboard = false)
    {
        if (lib == null)
            return -1;

        var code = version?.Library?.Instructions ?? lib.Data.Instructions;

        if (lib.State == FileState.Workshop || toMotherboard)
        {
            Window.SetTab(0);
            Window.ActiveTab.Editors[0].ResetCode(code);
            IsOpen = false;
            return 0;
        }

        var tabIndex = GetTabIndexForScript(lib);
        if (tabIndex >= 0)
        {
            L.Debug($"Library {lib.Path} already open, switching to tab");
            Window.SetTab(tabIndex);
            Window.Tabs[tabIndex].Editors[0].ResetCode(code);
            IsOpen = false;
            return tabIndex;
        }

        var numTabsBefore = Window.Tabs.Count;

        try
        {
            var editor = new Editor(Window.ActiveEditor.KeyHandler, lib);
            editor.ResetCode(code);
            Window.Tabs.Add(new EditorTab(Window, editor, lib.Path));
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
            var spacing = ImGui.GetStyle().ItemSpacing;
            var buttonPos = ImGui.GetCursorPos() + new Vector2(width - 3 * buttonSize.x - 2 * ImGui.GetStyle().ItemSpacing.x, 0);
            var buttonPos2 = buttonPos + buttonSize + spacing;
            var status = script.StatusString;
            var isWorkshop = script.State == FileState.Workshop;

            if (!isWorkshop && script.Data.WorkshopFileHandle != 0)
                status += ", Published";

            var pos = ImGui.GetCursorPos();

            ImGui.SetCursorPos(buttonPos);
            if (Button("History", buttonSize, "Browse old versions of this script", isWorkshop))
                OpenHistory(SelectedNode);
            ImGui.SameLine();
            if (Button("Edit", buttonSize, "Edit script in new tab", isWorkshop))
                LoadScript(script);

            ImGui.SameLine();
            if (Button("Load", buttonSize, "Load script into Motherboard"))
                LoadScript(script, null, true);


            // ImGui.SetCursorPosX(width - 2 * buttonSize.x);
            ImGui.SetCursorPos(buttonPos2);
            if (Button("Save", buttonSize, "Save description and title", isWorkshop))
                script.Save();

            ImGui.SameLine();
            if (Button("Publish", buttonSize, "Publish the script to the Steam Workshop", isWorkshop))
                script.Publish().Forget();

            ImGui.SetCursorPos(pos);

            Text($"Author: {script.Data.Author}");
            Text($"Date:   {script.Date}");
            Text($"Status: {status}");
            Text($"Name: ", width / 2);
            ImGui.SameLine();

            if (isWorkshop)
                ImGui.Text(script.Title);
            else
            {
                var name = LibNode.GetName(script.Data.Title);
                if (InputText("##name", ref name, width / 2))
                {
                    SelectedNode.Rename(LibNode.Combine(SelectedNode.Prefix, name));
                }
            }

            var numLines = Mathf.Clamp(script.Data.Description.Split('\n').Length, 2, 5);
            var height = (numLines - 1) * LineHeightWithSpacing + LineHeight;
            ImGui.InputTextMultiline("", ref script.Data.Description, 1024, new Vector2(width, height), isWorkshop ? ImGuiInputTextFlags.ReadOnly : ImGuiInputTextFlags.None);


            if (ShowTooltip && ImGui.IsItemHovered())
                ImGui.SetTooltip("Description, click 'Save' to apply changes");

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

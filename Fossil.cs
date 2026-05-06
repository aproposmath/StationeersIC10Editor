namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

using Assets.Scripts.UI;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;

public static class FossilInstaller
{
    public static string CacheDir => Path.Combine(BepInEx.Paths.CachePath, "ic10editor");
    public static readonly string ScriptsDir = StationSaveUtils.GetSavePathScriptsSubDir().FullName;

    public static bool IsWine()
    {
        var wineLoader = Environment.GetEnvironmentVariable("WINELOADERNOEXEC");
        var winePrefix = Environment.GetEnvironmentVariable("WINEPREFIX");
        return wineLoader != null || winePrefix != null;
    }

    public static string PlatformSuffix => IsWine() ? "-wine" : "";

    public const string FossilVersion = "2.28";
    public static readonly string FossilZipName = $"fossil-{FossilVersion}.zip";
    public static readonly string FossilZipPath = Path.Combine(CacheDir, FossilZipName);

    public static readonly string FossilDownloadUrl = "https://github.com/aproposmath/StationeersIC10Editor/releases/download/assets/fossil-2.28.zip";
    public static readonly string FossilExe = Path.Combine(CacheDir, $"fossil{PlatformSuffix}.exe");
    public static readonly string FossilExeSHA256 = IsWine() ?
        "ca154cabb98b5278009c7bba38afcfcf2de5a1ef7971b9aeff12c9bf6772dc89" :
        "4a7886f3a49429b6f802e5ac89a3adf349f910cbe6376c5cb120bf4a958eb0fe";

    private static bool _IsFossilExeVerified = false;

    public static bool IsFossilExeValid
    {
        get
        {
            if (_IsFossilExeVerified)
                return true;

            if (!File.Exists(FossilExe))
                return false;

            _IsFossilExeVerified = ComputeSHA256(FossilExe) == FossilExeSHA256;
            return _IsFossilExeVerified;
        }
    }

    public static void EnsureInstalled()
    {
        L.Debug($"Ensuring Fossil {FossilVersion} is installed");

        if (IsFossilExeValid)
            return;

        if (!File.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);

        for (var retry = 0; retry < 3; retry++)
        {

            if (File.Exists(FossilExe))
                File.Delete(FossilExe);

            if (File.Exists(FossilZipPath))
                File.Delete(FossilZipPath);

            L.Debug($"Downloading Fossil {FossilVersion} from {FossilDownloadUrl}");
            var client = new WebClient();
            client.DownloadFile(FossilDownloadUrl, FossilZipPath);
            using var archive = ZipFile.OpenRead(FossilZipPath);
            L.Debug($"Extracting Fossil {FossilVersion} from {FossilZipPath} to {CacheDir}");
            archive.ExtractToDirectory(CacheDir);

            if (IsFossilExeValid)
                return;
        }

        throw new InvalidOperationException($"Fossil executable {FossilExe} is corrupted.");
    }

    public static string ComputeSHA256(string filePath)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha256.ComputeHash(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

}

public class FossilVCS
{
    public static readonly string ScriptsDir = FossilInstaller.ScriptsDir;
    public static readonly string CacheDir = FossilInstaller.CacheDir;
    public static readonly string BackupDir = Path.Combine(CacheDir, "backups");
    public static readonly string RepoFileName = ".fossil.repo";
    public static readonly string RepoFilePath = Path.Combine(ScriptsDir, RepoFileName);
    public static int KeepBackupCount = 50;


    public static async UniTask<string> RunAsync(string args)
    {
        L.Debug($"Running Fossil command: \"{args}\" at \"{ScriptsDir}\"");
        var sw = Stopwatch.StartNew();
        // await UniTask.SwitchToThreadPool();
        var psi = new ProcessStartInfo
        {
            FileName = FossilInstaller.FossilExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ScriptsDir,
        };
        psi.EnvironmentVariables["FOSSIL_HOME"] = Path.Combine(CacheDir);

        var tcs = new TaskCompletionSource<int>();

        var output = new StringBuilder();
        var error = new StringBuilder();

        using var process = new Process();
        process.StartInfo = psi;
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                error.AppendLine(e.Data);
        };

        process.Exited += (s, e) =>
        {
            tcs.TrySetResult(process.ExitCode);
        };

        if (!process.Start())
            throw new InvalidOperationException("Failed to start process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exitCode = await tcs.Task;

        if (exitCode != 0)
        {
            L.Info($"Error while running Fossil command: {args}");
            L.Info("\t" + output.ToString());
            var stdErr = error.ToString();
            L.Info("\t" + stdErr);
            throw new Exception(stdErr);
        }

        L.Debug($"\tcommand |{args}| took {sw.ElapsedMilliseconds}ms");
        // L.Debug($"\toutput: {output}");
        // L.Debug($"\terror: {error}");
        return output.ToString();
    }

    public static async UniTask MakeBackup()
    {
        if (!Directory.Exists(BackupDir))
            Directory.CreateDirectory(BackupDir);

        var archiveName = $"scripts_backup_{DateTime.Now:yyyyMMdd_HHmmss}.zip";
        var zipPath = Path.Combine(BackupDir, archiveName);

        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            foreach (var dir in Directory.GetDirectories(ScriptsDir))
            {
                var path = Path.Combine(dir, "instruction.xml");
                if (!File.Exists(path))
                    continue;
                var relativePath = Path.Combine(Path.GetFileName(dir), "instruction.xml");
                archive.CreateEntryFromFile(path, relativePath);
            }

            if (File.Exists(RepoFilePath))
                archive.CreateEntryFromFile(RepoFilePath, RepoFileName);
        }

        L.Info($"Created backup of all scripts (including fossil repo): {zipPath}");

        var backupFiles = new List<string>(Directory.GetFiles(BackupDir, "scripts_backup_*.zip"));
        backupFiles.Sort();
        L.Info($"Found {backupFiles.Count} backup files, keeping one per month and the last {KeepBackupCount}");

        var monthlyBackups = new Dictionary<string, string>();
        foreach (var file in backupFiles)
        {
            var date = Path.GetFileNameWithoutExtension(file).Substring("scripts_backup_".Length);
            monthlyBackups[date.Substring(0, 6)] = file;
        }

        var backupsToDelete = backupFiles.GetRange(0, Math.Max(0, backupFiles.Count - KeepBackupCount)).Except(monthlyBackups.Values).ToList();
        foreach (var file in backupsToDelete)
        {
            L.Info($"Deleting old backup: {file}");
            File.Delete(file);
        }
    }

    public static async UniTaskVoid Init()
    {
        L.Debug($"Initializing Fossil repository at {RepoFilePath}");
        await UniTask.SwitchToThreadPool();
        FossilInstaller.EnsureInstalled();

        var repoName = Path.GetFileName(RepoFilePath);

        await MakeBackup();

        if (!File.Exists(RepoFilePath))
            await RunAsync($"init {repoName}");

        try
        {
            if ((await RunAsync("status -b")).Trim() == "none")
                await RunAsync($"open -k {repoName}");

            if ((await RunAsync("status -b")).Trim() == "none")
                throw new Exception("Failed to open Fossil repository");

            await RunAsync("settings ignore-glob \"*/*.png,*.vdf\"");
        }
        catch (Exception ex)
        {
            L.Info($"Error while opening Fossil repository: {ex.Message}");
        }
    }

    public static async UniTask AddAndCommit(string[] paths, string message = null)
    {
        var arg = "";
        foreach (var path in paths)
            arg += $" \"{path}/instruction.xml\"";

        L.Debug($"Adding and committing {arg}, message: {message}");
        if (string.IsNullOrEmpty(message))
            message = $"Update {paths}";
        message = message.Replace("\"", "'");
        await RunAsync($"add {arg}");
        await RunAsync($"commit --no-warnings -m \"{message}\"");
        await LibraryWindow.LoadScripts();
    }

    public static async UniTask<FileState> GetFileState(string path)
    {
        if ((await RunAsync("extra")).Contains(path))
            return FileState.Untracked;
        var diff = await RunAsync("ls -v");
        // L.Debug($"diff: {diff}");
        if (diff.Contains("UNCHANGED  " + path))
        {
            // L.Debug($"File {path} is unchanged");
            return FileState.Unchanged;
        }
        return FileState.Modified;
    }


    private readonly static Dictionary<string, FileState> _StatesMap = new()
    {
        { "UNCHANGED", FileState.Unchanged },
        { "ADDED", FileState.Untracked },
        { "EDITED", FileState.Modified },
    };

    public static async UniTask<Dictionary<string, FileState>> GetFileStates()
    {
        var result = new Dictionary<string, FileState>();
        static string GetName(string path) => path.Split('/')[0];

        var extraPromise = RunAsync("extra");
        var ls = await RunAsync("ls -v");
        var extra = await extraPromise;

        foreach (var path in extra.Split('\n'))
            result[GetName(path)] = FileState.Untracked;
        var missingPaths = new List<string>();
        foreach (var line in ls.Split('\n'))
        {
            // L.Debug($"ls line: |{line}|");
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || !trimmed.EndsWith("instruction.xml"))
                continue;
            var stateString = trimmed.Substring(0, trimmed.IndexOf(' '));
            if (stateString == "MISSING")
                missingPaths.Add(trimmed.Substring(11));
            else if (_StatesMap.TryGetValue(stateString, out var state))
                result[GetName(trimmed.Substring(11))] = state;
            else if (stateString != "DELETED")
                L.Warning($"Unknown fossil state state: {stateString}");
        }

        if (missingPaths.Count > 0)
        {
            var arg = "";
            foreach (var path in missingPaths)
                arg += $" \"{path}\"";
            await RunAsync($"rm {arg}");
            await RunAsync($"commit -m \"Deleted\"");
        }
        return result;
    }

    public static async UniTask<List<FileVersion>> Log(string path)
    {
        if (string.IsNullOrEmpty(path))
            path = RepoFilePath;
        var log = await RunAsync($"timeline -p \"{path}\" --format \"%h\t%d\t%c\"");
        L.Debug($"Log result: {log}");
        var lines = log.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l));

        var result = new List<FileVersion>();
        foreach (var line in lines)
        {
            var parts = line.Split('\t');
            L.Debug($"Log line: {line}, parts: {parts.Length}");
            if (parts.Length < 3)
                continue;

            var message = parts[2];
            for (var i = 3; i < parts.Length; i++)
                message += "_" + parts[i];

            result.Add(new FileVersion { Path = path, Hash = parts[0], Date = parts[1], Message = message });
        }
        return result;
    }

    public static async UniTask<string> GetVersionAsync(string path, string version)
    {
        return await RunAsync($"cat {path} -r {version}");
    }

    public static async UniTask<string> GetDiff(string path, string old_version, string new_version = null)
    {
        var cmd = $"diff {path} --to {old_version}";
        if (!string.IsNullOrEmpty(new_version))
            cmd += $" --from {new_version}";
        var diff = await RunAsync(cmd);
        // remove last line
        if (!string.IsNullOrEmpty(diff))
            diff = diff.Substring(0, diff.LastIndexOf('\n'));
        if (!string.IsNullOrEmpty(diff))
            diff = diff.Substring(0, diff.LastIndexOf('\n') + 1);
        return diff;
    }

}

public enum FileState
{
    Unknown,
    Untracked,
    Unchanged,
    Modified,
    Workshop,
}

public class FileVersion
{
    public string Path;
    public string Hash;
    public string Date;
    public string Message;
    public VersionedScript VersionedScript;
    public InstructionData Library = null;

    public async UniTask LoadLibrary()
    {
        if (Library != null)
            return;
        var _RawContent = await FossilVCS.RunAsync($"cat \"{Path}\\instruction.xml\" -r {Hash}");
        var xmlSerializer = new XmlSerializer(typeof(InstructionData));
        using var textReader = new StringReader(_RawContent);
        Library = (InstructionData)xmlSerializer.Deserialize(textReader);
    }

    public string Label => string.IsNullOrEmpty(Date) || string.IsNullOrEmpty(Message) ? $"{Date}{Message}" : $"{Date} - {Message}";
}


public class FileHistoryWindow
{
    public bool IsOpen = false;
    public List<FileVersion> Versions = new List<FileVersion>();
    private Editor _previewEditor;
    public FileVersion SelectedVersion = null;
    public VersionedScript Library;

    public FileHistoryWindow(VersionedScript library)
    {
        Library = library;
    }

    public void Open()
    {
        IsOpen = true;
        LoadVersions().Forget();
    }

    public async UniTaskVoid LoadVersions()
    {
        var newVersions = await FossilVCS.Log(Library.Data.DirectoryPath.Name);
        var currentVersion = new FileVersion { Hash = "", Path = "", Date = "", Message = "Last saved", Library = Library.Data };
        newVersions.Insert(0, currentVersion);
        foreach (var v in newVersions)
            v.VersionedScript = Library;
        Versions = newVersions;
    }

    public void Close()
    {
        IsOpen = false;
    }

    public async UniTaskVoid LoadVersionCode(FileVersion version)
    {
        await version.LoadLibrary();
        _previewEditor.ResetCode(version.Library.Instructions, false);
    }

    public void Draw()
    {
        if (!IsOpen)
            return;
        using var _ = new ScopedStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);
        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        Settings.SetImGuiWindowCollapsed();
        var title = Library?.Data?.Title ?? "<no data>";
        ImGui.Begin($"Version History: {title}", ref IsOpen, ImGuiWindowFlags.NoSavedSettings);
        using (new Pane("Versions", 0.4f, -1))
        {
            foreach (var version in Versions)
            {
                if (ImGui.Selectable(version.Label, SelectedVersion == version) && SelectedVersion != version)
                {
                    SelectedVersion = version;
                    if (_previewEditor == null)
                    {
                        _previewEditor = new Editor(null, Library);
                        _previewEditor.IsReadOnly = true;
                    }
                    _previewEditor.ResetCode("# Loading version...", false);
                    LoadVersionCode(version).Forget();
                }
            }
        }

        ImGui.SameLine();

        using (new Pane("LibrarySearchPreview", 1.0f, -1))
        {
            if (_previewEditor != null)
            {
                _previewEditor.Update();
                using var __ = new ScopedStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
                _previewEditor.Draw(
                    ImGui.GetCursorScreenPos(),
                    ImGui.GetContentRegionAvail(),
                    "##LibraryVersionPreviewEditor"
                );
            }
        }

        var buttonSize = new Vector2(100, 0);

        if (Button("Close", buttonSize))
            IsOpen = false;

        ImGui.SameLine();

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().x - buttonSize.x - ImGui.GetStyle().FramePadding.x);

        if (Button("Load", buttonSize, "Load this version into editor", SelectedVersion == null))
        {
            LibraryWindow.LoadScript(SelectedVersion.VersionedScript, SelectedVersion);
            IsOpen = false;
        }

        if (ImGui.IsKeyDown(ImGuiKey.Escape))
            Close();

        ImGui.End();
    }
}

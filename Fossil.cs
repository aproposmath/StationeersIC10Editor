namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Xml.Serialization;

using Assets.Scripts.UI;

using Cysharp.Threading.Tasks;

using ImGuiNET;

using UnityEngine;

using static ImGuiUtils;

public class FossilVCS
{
    public static string CacheDir => Path.Combine(BepInEx.Paths.CachePath, "ic10editor");
    public const string FossilVersion = "2.28";
    public const string FossilZipName = $"fossil-w64-{FossilVersion}.zip";
    public static readonly string FossilZipPath = Path.Combine(CacheDir, FossilZipName);
    public const string FossilDownloadUrl = $"https://www3.fossil-scm.org/home/uv/{FossilZipName}";
    public static readonly string FossilExe = Path.Combine(CacheDir, "fossil.exe");
    public static readonly string FossilExeSHA256 = "4a7886f3a49429b6f802e5ac89a3adf349f910cbe6376c5cb120bf4a958eb0fe";

    public static readonly string ScriptsDir = StationSaveUtils.GetSavePathScriptsSubDir().FullName;
    public static readonly string RepoFilePath = Path.Combine(ScriptsDir, ".fossil.repo");

    private static bool _IsFossilExeVerified = false;

    public static bool IsFossilExeValid
    {
        get
        {
            if (_IsFossilExeVerified)
                return true;

            if (!File.Exists(FossilExe))
                return false;

            _IsFossilExeVerified = true;  // ComputeSHA256(FossilExe) == FossilExeSHA256;
            return _IsFossilExeVerified;
        }
    }

    public static async UniTask<string> RunAsync(string args)
    {
        L.Debug($"Running Fossil command: \"{args}\" at \"{ScriptsDir}\"");
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = FossilExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ScriptsDir,
        };
        psi.EnvironmentVariables["FOSSIL_HOME"] = Path.Combine(CacheDir);

        await UniTask.SwitchToThreadPool();

        // Hack: this is not async, it's still blocking a (non-main)thread
        using var process = Process.Start(psi);
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            L.Info($"Error while running Fossil command: {args}");
            L.Info("\t" + process.StandardOutput.ReadToEnd());
            var stdErr = process.StandardError.ReadToEnd();
            L.Info("\t" + stdErr);
            throw new Exception(stdErr);
        }
        L.Debug($"\tcommand |{args}| took " + sw.ElapsedMilliseconds + "ms");
        return process.StandardOutput.ReadToEnd();
    }

    public static string Run(string args)
    {
        L.Debug($"Running Fossil command: \"{args}\" at \"{ScriptsDir}\"");
        var psi = new ProcessStartInfo
        {
            FileName = FossilExe,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = ScriptsDir,
        };
        psi.EnvironmentVariables["FOSSIL_HOME"] = Path.Combine(CacheDir);

        using var process = Process.Start(psi);
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            L.Info($"Error while running Fossil command: {args}");
            L.Info("\t" + process.StandardOutput.ReadToEnd());
            var stdErr = process.StandardError.ReadToEnd();
            L.Info("\t" + stdErr);
            throw new Exception(stdErr);
        }
        return process.StandardOutput.ReadToEnd();
    }


    public static void Init()
    {
        L.Debug($"Initializing Fossil repository at {RepoFilePath}");
        EnsureInstalled();

        var repoName = Path.GetFileName(RepoFilePath);

        if (!File.Exists(RepoFilePath))
            Run($"init {repoName}");

        try
        {
            Run($"open -k {repoName}");
        }
        catch (Exception ex)
        {
            L.Info($"Error while opening Fossil repository: {ex.Message}");
        }
    }

    public static void AddAndCommit(string path, string message = null)
    {
        L.Debug($"Adding and committing {path}, message: {message}");
        if (string.IsNullOrEmpty(message))
            message = $"Update {path}";
        message = message.Replace("\"", "'");
        Run($"add \"{path}\"");
        Run($"commit --no-warnings -m \"{message}\"");
    }

    public static async UniTask<FileState> GetFileState(string path)
    {
        if ((await RunAsync("extra")).Contains(path))
            return FileState.Untracked;
        var diff = await RunAsync("ls -v");
        L.Debug($"diff: {diff}");
        if (diff.Contains("UNCHANGED  " + path))
        {
            L.Debug($"File {path} is unchanged");
            return FileState.Unchanged;
        }
        return FileState.Modified;
    }

    public static Dictionary<string, FileState> GetFileStates()
    {
        var result = new Dictionary<string, FileState>();
        static string GetName(string path) => path.Split('/')[0];
        foreach (var path in Run("extra").Split('\n'))
            result[GetName(path)] = FileState.Untracked;
        foreach (var line in Run("ls -v").Split('\n'))
        {
            if (string.IsNullOrEmpty(line) || !line.EndsWith("instruction.xml"))
                continue;
            var state = line.Trim().StartsWith("EDITED ") ? FileState.Modified : FileState.Unchanged;
            result[GetName(line.Substring(11))] = state;
            L.Debug($"File state for {GetName(line.Substring(11))}: {state}");
            L.Debug($"\tline: {line}");
        }
        return result;
    }

    public static List<FileVersion> Log(string path)
    {
        if (string.IsNullOrEmpty(path))
            path = RepoFilePath;
        var log = Run($"timeline -p \"{path}\" --format \"%h\t%d\t%c\"");
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

    public static string GetVersion(string path, string version)
    {
        return Run($"cat {path} -r {version}");
    }

    public static string GetDiff(string path, string old_version, string new_version = null)
    {
        var cmd = $"diff {path} --to {old_version}";
        if (!string.IsNullOrEmpty(new_version))
            cmd += $" --from {new_version}";
        var diff = Run(cmd);
        // remove last line
        if (!string.IsNullOrEmpty(diff))
            diff = diff.Substring(0, diff.LastIndexOf('\n'));
        if (!string.IsNullOrEmpty(diff))
            diff = diff.Substring(0, diff.LastIndexOf('\n') + 1);
        return diff;
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

public enum FileState
{
    Untracked,
    Unchanged,
    Modified,
}

public class FileVersion
{
    public string Path;
    public string Hash;
    public string Date;
    public string Message;
    public InstructionData Library = null;

    public string _RawContent = null;
    public string Content
    {
        get
        {
            if (Library == null)
            {
                _RawContent = FossilVCS.Run($"cat \"{Path}\\instruction.xml\" -r {Hash}");
                var xmlSerializer = new XmlSerializer(typeof(InstructionData));
                using var textReader = new StringReader(_RawContent);
                Library = (InstructionData)xmlSerializer.Deserialize(textReader);
            }
            return Library.Instructions;
        }
    }

    public string Label => string.IsNullOrEmpty(Date) || string.IsNullOrEmpty(Message) ? $"{Date}{Message}" : $"{Date} - {Message}";
}


public class FileHistoryWindow
{
    public bool IsOpen = false;
    public List<FileVersion> Versions = new List<FileVersion>();
    private Editor _previewEditor;
    public FileVersion SelectedVersion = null;
    private EditorTab _tab;
    public InstructionData Library => _tab.Library;

    public FileHistoryWindow(EditorTab tab)
    {
        _tab = tab;
    }

    public void Open()
    {
        IsOpen = true;
        Versions = FossilVCS.Log(Library.DirectoryPath.Name);
        var currentVersion = new FileVersion { Hash = "", Path = "", Date = "", Message = "Last saved", Library = Library };
        Versions.Insert(0, currentVersion);
    }

    public void Close()
    {
        IsOpen = false;
    }

    public void Draw()
    {
        if (!IsOpen)
            return;
        using var _ = new ScopedStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);
        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        if (
            ImGui.Begin($"Version History: {Library.Title}", ref IsOpen)
        )
        {
            using (new Pane("Versions", 0.4f))
            {
                foreach (var version in Versions)
                {
                    if (ImGui.Selectable(version.Label, SelectedVersion == version))
                    {
                        SelectedVersion = version;
                        if (_previewEditor == null)
                        {
                            _previewEditor = new Editor(null, Library);
                            _previewEditor.IsReadOnly = true;
                        }
                        _previewEditor.ResetCode(version.Content, false);
                    }
                }
            }

            ImGui.SameLine();

            using (new Pane("LibrarySearchPreview"))
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
                _tab.Editors[0].ResetCode(SelectedVersion.Content);
                IsOpen = false;
            }
        }

        if (ImGui.IsKeyDown(ImGuiKey.Escape))
            Close();

        ImGui.End();
    }
}

namespace StationeersIC10Editor;

using System;
using System.IO;
using System.Net;
using System.IO.Compression;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;

using Assets.Scripts.UI;

using ImGuiNET;

using UnityEngine;


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

    public static string Run(string args)
    {
        L.Debug($"Running Fossil command: {args}");
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
        try
        {
            Run($"commit --no-warnings -m \"{message}\"");
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("allow-empty"))
                return;
            throw;
        }
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

public class FileVersion
{
    public string Path;
    public string Hash;
    public string Date;
    public string Message;
    public string _Content = null;

    public string Content
    {
        get
        {
            if (_Content == null)
            {
                const string startTag = "<Instructions>";
                const string endTag = "</Instructions>";
                var content = FossilVCS.Run($"cat \"{Path}\\instruction.xml\" -r {Hash}");
                var startIndex = content.IndexOf(startTag, StringComparison.Ordinal) + startTag.Length;
                var endIndex = content.IndexOf(endTag, startIndex, StringComparison.Ordinal);
                L.Debug($"Content: {content}, startIndex: {startIndex}, endIndex: {endIndex}");
                _Content = content.Substring(startIndex, endIndex - startIndex);
            }
            return _Content;
        }
    }
    
    public string Label => $"{Date} - {Message}";
}


public class FileHistoryWindow
{
    public bool IsOpen = false;
    public InstructionData Library;
    public List<FileVersion> Versions = new List<FileVersion>();
    private Editor _previewEditor;

    public FileHistoryWindow(InstructionData data)
    {
        Library = data;
    }

    public void Open()
    {
        IsOpen = true;
        Versions = FossilVCS.Log(Library.DirectoryPath.Name);
        var currentVersion = new FileVersion { Hash = "", Path = "", Date = "", Message = "Current save", _Content = Library.Instructions };
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
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 2.0f);
        ImGui.SetNextWindowSize(new Vector2(1300, 800), ImGuiCond.FirstUseEver);
        if (
            ImGui.Begin($"Version History of {Library.Title}", ref IsOpen)
        )
        {
            ImGui.BeginChild("Versions", new Vector2(500, 600), true);
            foreach (var version in Versions)
            {
                if (ImGui.Selectable(version.Label))
                {
                    if (_previewEditor == null)
                    {
                        _previewEditor = new Editor(null, Library);
                        _previewEditor.IsReadOnly = true;
                    }
                    _previewEditor.ResetCode(version.Content, false);
                }
            }
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("LibrarySearchPreview", new Vector2(700, 600), true);
            _previewEditor.Update();
            ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 0);
            _previewEditor.Draw(
                ImGui.GetCursorScreenPos(),
                new Vector2(670, 600),
                "##LibraryVersionPreviewEditor"
            );
            ImGui.PopStyleVar();
            ImGui.EndChild();
            
            if(ImGui.Button("Close"))
            {
                IsOpen = false;
            }
        }
        ImGui.End();
    }
}

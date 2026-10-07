namespace StationeersIC10Editor;

using System;
using System.IO;
using System.IO.Compression;

using Cysharp.Threading.Tasks;

using UnityEngine.Networking;

// Downloadable egg assets (music, synth songs, sprite images): fetched once as a zip from the
// "assets" GitHub release (independent of the mod version tags) and extracted into
// <BepInEx cache>/ic10editor/egg, after that everything works offline. Everything egg related
// lives in that folder: the extracted zip, version.txt, the zip downloads and the TextureCache.
//
// Zip layout (built by tools/make_egg_assets.sh, file name egg_assets_<Version>.zip):
//   music/*.ogg      party and playlist tracks
//   songs/*.json     synth songs (kitt1..7.json jump themes)
//   music/*.json     synth songs for the playlist (made with tools/prep_song.py)
//   images/*.png     sprite parts (DJ headphones, face mask)
public static class EggAssets
{
    public const string Version = "1.0";
    public static readonly string DownloadUrl = $"https://github.com/aproposmath/StationeersIC10Editor/releases/download/assets/egg_assets_{Version}.zip";

    public static readonly string AssetsDir = Path.Combine(BepInEx.Paths.CachePath, "ic10editor", "egg");
    // A zip placed here by hand is used instead of downloading (testing before the upload exists).
    public static readonly string LocalZipPath = Path.Combine(AssetsDir, "egg_assets.zip");
    static readonly string DownloadZipPath = Path.Combine(AssetsDir, "egg_assets.download.zip");
    static readonly string MarkerPath = Path.Combine(AssetsDir, "version.txt");
    // Subfolders coming from the zip, replaced on (re)install.
    static readonly string[] ZipDirs = ["music", "songs", "images"];

    public static bool Ready { get; private set; }
    public static bool Failed { get; private set; }
    public static bool Done => Ready || Failed;
    public static string Status { get; private set; } = "";

    static bool _running;

    public static string MusicDir => Path.Combine(AssetsDir, "music");
    public static string SongsDir => Path.Combine(AssetsDir, "songs");
    public static string SongPath(string name) => Path.Combine(SongsDir, name + ".json");
    public static string ImagePath(string name) => Path.Combine(AssetsDir, "images", name);

    static bool Installed => File.Exists(MarkerPath) && File.ReadAllText(MarkerPath).Trim() == Version;

    public static async UniTask Ensure()
    {
        if (Done)
            return;
        if (_running)
        {
            while (_running)
                await UniTask.Yield();
            return;
        }
        _running = true;
        try
        {
            if (Installed)
            {
                Ready = true;
                return;
            }
            Directory.CreateDirectory(AssetsDir);

            var zip = LocalZipPath;
            if (!File.Exists(zip))
            {
                zip = DownloadZipPath;
                if (!await Download(zip))
                {
                    Failed = true;
                    return;
                }
            }

            Status = "Unpacking";
            await UniTask.RunOnThreadPool(() =>
            {
                if (File.Exists(MarkerPath))
                    File.Delete(MarkerPath);
                foreach (var dir in ZipDirs)
                    if (Directory.Exists(Path.Combine(AssetsDir, dir)))
                        Directory.Delete(Path.Combine(AssetsDir, dir), true);
                using (var archive = ZipFile.OpenRead(zip))
                    archive.ExtractToDirectory(AssetsDir);
                File.WriteAllText(MarkerPath, Version);
                if (zip != LocalZipPath)
                    File.Delete(zip);
            });
            Ready = true;
            L.Debug($"Egg assets {Version} installed to {AssetsDir}");
        }
        catch (Exception e)
        {
            L.Debug($"Egg assets setup failed: {e.Message}");
            Failed = true;
        }
        finally
        {
            _running = false;
        }
    }

    static async UniTask<bool> Download(string target)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Status = attempt == 1 ? "Downloading" : $"Downloading (retry {attempt})";
            try
            {
                if (File.Exists(target))
                    File.Delete(target);
                using var request = UnityWebRequest.Get(DownloadUrl);
                request.downloadHandler = new DownloadHandlerFile(target);
                request.timeout = 120;
                await request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                    return true;
                L.Debug($"Egg assets download failed: {request.error}");
            }
            catch (Exception e)
            {
                L.Debug($"Egg assets download failed: {e.Message}");
            }
        }
        return false;
    }
}

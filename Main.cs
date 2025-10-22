namespace StationeersIC10Editor
{
    using UnityEngine;
    using System;
    using BepInEx;
    using HarmonyLib;
    using System.Collections;

    class L
    {
        private static BepInEx.Logging.ManualLogSource _logger;

        public static void SetLogger(BepInEx.Logging.ManualLogSource logger)
        {
            _logger = logger;
        }

        public static void Info(string message)
        {
            _logger?.LogInfo(message);
        }

        public static void Error(string message)
        {
            _logger?.LogError(message);
        }

        public static void Warning(string message)
        {
            _logger?.LogWarning(message);
        }

    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class IC10EditorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "aproposmath-stationeers-ic10-editor"; // Change this to your own unique Mod ID
        public const string PluginName = "IC10Editor";
        public const string PluginVersion = VersionInfo.Version;

        private void Awake()
        {
            try
            {
                L.SetLogger(this.Logger);
                this.Logger.LogInfo(
                    $"Awake ${PluginName} {VersionInfo.VersionGit}, build time {VersionInfo.BuildTime}");

                var harmony = new Harmony(PluginGuid);
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                this.Logger.LogError($"Error during ${PluginName} {VersionInfo.VersionGit} init: {ex}");
            }
        }
    }
}

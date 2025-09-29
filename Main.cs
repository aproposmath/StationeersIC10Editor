using System;
using System.Diagnostics;
using Assets.Scripts;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace ExampleMod
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ExampleModPlugin : BaseUnityPlugin
    {
        public const string pluginGuid = "00000000-1111-2222-3333-444444444444"; // Change this to your own unique GUID
        public const string pluginName = "ExampleMod";
        public const string pluginVersion = VersionInfo.Version;

        private void Awake()
        {
            try
            {
                Logger.LogInfo(
                    $"Awake ${pluginName} {VersionInfo.VersionGit}, build time {VersionInfo.BuildTime}"
                );

                var harmony = new Harmony(pluginGuid);
                harmony.PatchAll();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error during ${pluginName} {VersionInfo.VersionGit} init: {ex}");
            }
        }
    }
}

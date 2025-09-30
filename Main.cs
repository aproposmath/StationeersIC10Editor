using System;
using BepInEx;
using HarmonyLib;

namespace ExampleMod
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class ExampleModPlugin : BaseUnityPlugin
    {
        public const string pluginGuid = "aproposmath-stationeers-example-mod"; // Change this to your own unique Mod ID
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

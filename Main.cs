namespace StationeersIC10Editor;

using System;
using System.Collections.Generic;

using BepInEx;
using BepInEx.Configuration;

using HarmonyLib;

class L
{
    private static BepInEx.Logging.ManualLogSource _logger;

    public static void SetLogger(BepInEx.Logging.ManualLogSource logger)
    {
        _logger = logger;
    }

    public static void Debug(string message)
    {
#if DEBUG
        _logger?.LogDebug(message);
#endif
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
    private Harmony _harmony;

    public static ConfigEntry<bool> PauseOnOpen;
    public static ConfigEntry<bool> VimBindings;
    public static ConfigEntry<bool> EnforceLineLimit;
    public static ConfigEntry<bool> EnforceByteLimit;
    public static ConfigEntry<bool> EnableAutoComplete;
    public static ConfigEntry<float> ScaleFactor;
    public static ConfigEntry<float> TooltipDelay;
    public static ConfigEntry<int> LineSpacingOffset;
    public static ConfigEntry<bool> CollapseOnGameWindow;
    public static ConfigEntry<bool> RelativeLineNumbers;

    public static Dictionary<string, ConfigEntry<string>> Colors = new();
    public static IC10EditorPlugin Instance { get; private set; }

    public static Dictionary<string, string> ColorDefaults = new()
    {
        { "Default", "#FFFFFFFF" },
        { "Error", "#FF0000FF" },
        { "Warning", "#FF8F00FF" },
        { "Comment", "#808080FF" },
        { "LineNumber", "#808080FF" },
        { "Selection", "#1A44B0FF" },
        { "Number", "#20B2AAFF" },
        { "Instruction", "#FFFF00FF" },
        { "Device", "#00FF00FF" },
        { "LogicType", "#FF8000FF" },
        { "Register", "#0080FFFF" },
        { "BasicEnum", "#20B2AAFF" },
        { "Define", "#20B2AAFF" },
        { "Alias", "#4D4DCCFF" },
        { "Label", "#A128C1FF" },
    };

    private void BindAllConfigs()
    {
        VimBindings = Config.Bind(
            "General",
            "Enable VIM bindings (experimental!)",
            false,
            "Enable VIM bindings"
        );
        EnforceLineLimit = Config.Bind(
            "General",
            "Enforce 128 line limit",
            true,
            "Enforce the 128 line limit of IC10 programs"
        );
        EnforceByteLimit = Config.Bind(
            "General",
            "Enforce 4KB size limit",
            true,
            "Enforce the 4KB byte size of IC10 programs"
        );
        PauseOnOpen = Config.Bind(
            "General",
            "Pause game when IC10 editor is open",
            true,
            "Pause the game when the IC10 editor window is open"
        );
        ScaleFactor = Config.Bind(
            "General",
            "UI Scale Factor",
            1.0f,
            "Scale factor for the IC10 editor UI"
        );
        LineSpacingOffset = Config.Bind(
            "General",
            "Line Spacing Offset",
            0,
            "Integer to increase/decrease line spacing"
        );
        TooltipDelay = Config.Bind(
            "General",
            "Tooltip Delay",
            100f,
            "Delay in seconds before tooltips are shown"
        );
        EnableAutoComplete = Config.Bind(
            "General",
            "Autocompletion",
            true,
            "Enable autocompletion/suggestions (trigger with Tab key)"
        );
        CollapseOnGameWindow = Config.Bind(
            "General",
            "CollapseOnGameWindow",
            true,
            "Automatically collapse the IC10 editor when Stationpedia or other game windows are opened"
        );
        RelativeLineNumbers = Config.Bind(
            "General",
            "RelativeLineNumbers",
            false,
            "Show relative line numbers"
        );


        foreach (var kv in ColorDefaults)
        {
            Colors[kv.Key] = Config.Bind(
                "Colors",
                kv.Key,
                kv.Value
            );
        }

        LoadColorConfig();
        Config.ConfigReloaded += (_, e) =>
        {
            try
            {
                LoadColorConfig();
            }
            catch (Exception ex)
            {
                L.Error($"Error applying color scheme: {ex}");
            }
        };
    }

    public static void LoadColorConfig()
    {
        bool hasColorChanged = false;
        foreach (var kv in ColorDefaults)
        {
            if (Colors.TryGetValue(kv.Key, out var colorConfig))
            {
                // use reflection to set the static color fields in IC10CodeFormatter
                var name = "Color" + kv.Key;
                var fieldType = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public;
                var colorField = typeof(ICodeFormatter).GetField(name, fieldType);
                if (colorField == null)
                    colorField = typeof(IC10.IC10CodeFormatter).GetField(name, fieldType);

                if (colorField == null)
                {
                    L.Error($"Could not find color field for {kv.Key}");
                    continue;
                }
                var oldColor = (uint)colorField.GetValue(null);
                var newValue = ICodeFormatter.ColorFromHTML(colorConfig.Value);
                if (oldColor != newValue)
                {
                    colorField.SetValue(null, newValue);
                    hasColorChanged = true;
                    L.Info($"Color for {kv.Key} changed to {colorConfig.Value}");
                }
            }
        }
        if (hasColorChanged)
        {
            foreach (var editor in IC10EditorPatches.AllEditors)
                foreach (var tab in editor.Tabs)
                    tab[0].CodeFormatter.ResetCode(tab[0].Code);
        }
    }



    private void Awake()
    {
        try
        {
            L.SetLogger(this.Logger);
            this.Logger.LogInfo(
                $"Awake {PluginName} {VersionInfo.VersionGit}, build time {VersionInfo.BuildTime}"
            );
            Instance = this;
            BindAllConfigs();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll();

            CodeFormatters.RegisterFormatter("Plain", typeof(PlainTextFormatter));
            CodeFormatters.RegisterFormatter("IC10", typeof(IC10.IC10CodeFormatter), true);
            // CodeFormatters.RegisterFormatter("C#", typeof(CSharpFormatter));
            // CodeFormatters.RegisterFormatter("Python", typeof(ImGuiEditor.LSP.LSPFormatter));
        }
        catch (Exception ex)
        {
            this.Logger.LogError(
                $"Error during {PluginName} {VersionInfo.VersionGit} init: {ex}"
            );
        }
    }

    private void OnDestroy()
    {
#if DEBUG
        L.Info($"OnDestroy ${PluginName} {VersionInfo.VersionGit}");
        IC10EditorPatches.Cleanup();
        _harmony.UnpatchSelf();
#endif
    }
}

namespace StationeersIC10Editor;

using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;
using Assets.Scripts.UI.ImGuiUi;

using HarmonyLib;

[HarmonyPatch]
public static class IC10EditorPatches
{
    // Keep a separate editor for each motherboard's source code
    // so that switching between them preserves state (undo operations etc.)
    // This data is lost on save/reload of the game.
    public static ConditionalWeakTable<ProgrammableChipMotherboard, EditorWindow> EditorData =
        new ConditionalWeakTable<ProgrammableChipMotherboard, EditorWindow>();
    public static List<EditorWindow> AllEditors = new List<EditorWindow>();

    public static void Cleanup()
    {
        foreach (var editor in AllEditors)
            editor.HideWindow();
        AllEditors.Clear();
        EditorData = new ConditionalWeakTable<ProgrammableChipMotherboard, EditorWindow>();
    }

    private static EditorWindow GetEditor(ProgrammableChipMotherboard isc)
    {
        EditorWindow editor;
        if (!EditorData.TryGetValue(isc, out editor))
        {
            editor = new EditorWindow(isc);
            EditorData.Add(isc, editor);
            AllEditors.Add(editor);
        }

        return editor;
    }

    [HarmonyPatch(typeof(InputSourceCode), nameof(InputSourceCode.ShowInputPanel))]
    [HarmonyPrefix]
    public static void InputSourceCode_ShowInputPanel_Prefix(
        string title,
        ref string defaultText
    )
    {
        EditorWindow.UseNativeEditor = false;
        var editor = GetEditor(InputSourceCode.Instance.PCM);
        editor.SetTitle(title);
        if(editor.MotherboardTab[0].Code != defaultText)
            editor.MotherboardTab[0].ResetCode(defaultText);
        editor.ShowWindow();
        defaultText = "__IC10PLACEHOLDER__"; // The editor causes lag for large code, so don't paste it now
    }

    [HarmonyPatch(typeof(ImguiCreativeSpawnMenu))]
    [HarmonyPatch(nameof(ImguiCreativeSpawnMenu.Draw))]
    [HarmonyPostfix]
    static void ImguiCreativeSpawnMenu_Draw_Postfix()
    {
        try
        {
            foreach (var editor in AllEditors)
                editor.Draw();
        }
        catch (System.Exception e)
        {
            L.Error("Exception in Editor Draw:");
            L.Error(e.ToString());
        }
    }

    [HarmonyPatch(typeof(EditorLineOfCode))]
    [HarmonyPatch(nameof(EditorLineOfCode.HandleUpdate))]
    [HarmonyPrefix]
    static bool EditorLineOfCode_HandleUpdate_Prefix()
    {
        return EditorWindow.UseNativeEditor;
    }

    [HarmonyPatch(typeof(InputSourceCode))]
    [HarmonyPatch(nameof(InputSourceCode.HandleInput))]
    [HarmonyPrefix]
    static bool InputSourceCode_HandleInput_Prefix()
    {
        return EditorWindow.UseNativeEditor;
    }

    [HarmonyPatch(typeof(InputSourceCode))]
    [HarmonyPatch(nameof(InputSourceCode.Copy))]
    [HarmonyPrefix]
    static bool InputSourceCode_Copy_Prefix(ref string __result)
    {
        if (EditorWindow.UseNativeEditor)
            return true;

        var editor = GetEditor(InputSourceCode.Instance.PCM);
        __result = editor.Code;
        return false;
    }

    [HarmonyPatch(typeof(InputSourceCode))]
    [HarmonyPatch(nameof(InputSourceCode.Paste))]
    [HarmonyPrefix]
    static bool InputSourceCode_Copy_Paste(ref string value)
    {
        if (EditorWindow.UseNativeEditor)
            return true;

        // See the patch for ShowInputPanel - we set a placeholder value there
        if (value != "__IC10PLACEHOLDER__")
            GetEditor(InputSourceCode.Instance.PCM).MotherboardTab[0].ResetCode(value);

        return false;
    }
}

namespace StationeersIC10Editor;


using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.UI;
using Assets.Scripts.UI.ImGuiUi;
using Assets.Scripts.Objects.Electrical;

using HarmonyLib;
using Cysharp.Threading.Tasks;

using Assets.Scripts.GridSystem;
using Assets.Scripts;
using Assets.Scripts.Objects;

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
        if (editor.MotherboardTab[0].Code != defaultText)
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

[HarmonyPatch]
[HarmonyPatch(typeof(ProgrammableChipMotherboard))]
public static class ChipMotherboardPatches
{
    static HashSet<ProgrammableChipMotherboard> deserializingDevices = new HashSet<ProgrammableChipMotherboard>();

    static async UniTaskVoid HandleDeviceListChangeAsync(ProgrammableChipMotherboard __instance, ICircuitHolder oldHolder)
    {
        await UniTask.SwitchToMainThread();
        if (GameManager.GameState != GameState.Running)
            return;

        if(deserializingDevices.Contains(__instance)) {
            // we are deserializing, skip restoring
            return;
        }

        // wait until the original async method is done (it sets _DevicesChanged to false at the end)
        while (__instance._DevicesChanged)
            await UniTask.NextFrame();

        if(deserializingDevices.Contains(__instance)) {
            // we are deserializing, skip restoring
            return;
        }

        // find old holder index and re-select it
        // ignore index 0, since that's the default anyway (and sometimes it would overwrite the deserialized value)
        for (int i = 1; i < __instance._circuitHolders.Count; i++)
        {
            if (__instance._circuitHolders[i] == oldHolder)
            {
                L.Debug($"Restoring old circuit holder for motherboard {__instance.name} to index {i}");
                __instance._dropdown.ItemClicked(i);
                break;
            }
        }
    }


    [HarmonyPatch(nameof(ProgrammableChipMotherboard.HandleDeviceListChange))]
    [HarmonyPrefix]
    static bool HandleDeviceListChangePrefix(ProgrammableChipMotherboard __instance)
    {
        if(!Settings.RestoreSelectedHousing)
            return true;

        if(deserializingDevices.Contains(__instance))
            return false;

        // get the index before it is reset by HandleDeviceListChange
        // if the index or holder is invalid, there is no need to restore it later
        var index = __instance._dropdown.SelectedIndex;
        if (index < 0 || index >= __instance._circuitHolders.Count)
            return true;

        var oldHolder = __instance._circuitHolders[index];
        if (oldHolder == null)
            return true;

        // since HandleDeviceListChange is async, we need to run our code async as well
        // and wait until the original method is done
        HandleDeviceListChangeAsync(__instance, oldHolder).Forget();
        return true;
    }

    static async UniTaskVoid DeserializeSaveAsync(ProgrammableChipMotherboard __instance)
    {
        // "block" HandleDeviceListChange during deserialization
        // then wait until the game is running and devices have settled
        // then restore the selected device
        deserializingDevices.Add(__instance);
        try
        {
            int index = __instance._dropdown.SelectedIndex;

            await UniTask.SwitchToMainThread();

            while (GameManager.GameState != GameState.Running)
                await UniTask.NextFrame();

            while (__instance._DevicesChanged)
                await UniTask.NextFrame();

            await UniTask.NextFrame();

            var dropdown = __instance._dropdown;

            if (index >= 0 && index < __instance._circuitHolders.Count)
                __instance._dropdown.ItemClicked(index);
        }
        finally
        {
            deserializingDevices.Remove(__instance);
        }
    }

    [HarmonyPatch(nameof(ProgrammableChipMotherboard.DeserializeSave))]
    [HarmonyPostfix]
    static void DeserializeSavePostfix(ProgrammableChipMotherboard __instance, ThingSaveData savedData)
    {
        // After deserialization, restore the setting when the game is runnings
        // and all HandleDeviceListChange calls are done
        if(Settings.RestoreSelectedHousing)
            DeserializeSaveAsync(__instance).Forget();
    }
}

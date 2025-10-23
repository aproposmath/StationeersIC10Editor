namespace StationeersIC10Editor
{
    using System.Collections.Generic;
    using System.Runtime.CompilerServices;
    using Assets.Scripts.UI;
    using Assets.Scripts.UI.ImGuiUi;
    using Assets.Scripts.Objects.Motherboards;
    using HarmonyLib;

    [HarmonyPatch]
    public static class IC10EditorPatches
    {
        // Keep a separate editor for each motherboard's source code
        // so that switching between them preserves state (undo operations etc.)
        // This data is lost on save/reload of the game.
        public static ConditionalWeakTable<ProgrammableChipMotherboard, IC10Editor> EditorData =
            new ConditionalWeakTable<ProgrammableChipMotherboard, IC10Editor>();
        public static List<IC10Editor> AllEditors = new List<IC10Editor>();

        private static IC10Editor GetEditor(ProgrammableChipMotherboard isc)
        {
            L.Info($"Getting IC10Editor for source code {isc}");
            IC10Editor editor;
            if (!EditorData.TryGetValue(isc, out editor))
            {
                editor = new IC10Editor(isc);
                EditorData.Add(isc, editor);
                AllEditors.Add(editor);
            }

            return editor;
        }

        [HarmonyPatch(
                typeof(InputSourceCode),
                nameof(InputSourceCode.ShowInputPanel))]
        [HarmonyPrefix]
        public static void InputSourceCode_ShowInputPanel_Prefix(
            string title,
            ref string defaultText
            )
        {
            IC10Editor.UseNativeEditor = false;
            var editor = GetEditor(InputSourceCode.Instance.PCM);
            editor.SetTitle(title);
            editor.ResetCode(defaultText);
            editor.ShowWindow();
            defaultText = "__IC10PLACEHOLDER__"; // The editor causes lag for large code, so don't paste it now
        }

        [HarmonyPatch(typeof(ImguiCreativeSpawnMenu))]
        [HarmonyPatch(nameof(ImguiCreativeSpawnMenu.Draw))]
        [HarmonyPostfix]
        static void ImguiCreativeSpawnMenu_Draw_Postfix()
        {
            foreach (var editor in AllEditors)
                editor.Draw();
        }

        [HarmonyPatch(typeof(EditorLineOfCode))]
        [HarmonyPatch(nameof(EditorLineOfCode.HandleUpdate))]
        [HarmonyPrefix]
        static bool EditorLineOfCode_HandleUpdate_Prefix()
        { return IC10Editor.UseNativeEditor; }

        [HarmonyPatch(typeof(InputSourceCode))]
        [HarmonyPatch(nameof(InputSourceCode.HandleInput))]
        [HarmonyPrefix]
        static bool InputSourceCode_HandleInput_Prefix()
        { return IC10Editor.UseNativeEditor; }

        [HarmonyPatch(typeof(InputSourceCode))]
        [HarmonyPatch(nameof(InputSourceCode.Copy))]
        [HarmonyPrefix]
        static bool InputSourceCode_Copy_Prefix(ref string __result)
        {
            if (IC10Editor.UseNativeEditor)
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
            if (IC10Editor.UseNativeEditor)
                return true;

            // See the patch for ShowInputPanel - we set a placeholder value there
            if (value != "__IC10PLACEHOLDER__")
                GetEditor(InputSourceCode.Instance.PCM).ResetCode(value);

            return false;

        }
    }
}

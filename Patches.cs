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
        public static void InputSourceCode_ShowInputPanel_Postfix(
            string title,
            string defaultText
            )
        {
            var editor = GetEditor(InputSourceCode.Instance.PCM);
            editor.SetTitle(title);
            editor.SetSourceCode(defaultText);
            editor.ShowWindow();
        }

        [HarmonyPatch(typeof(ImguiCreativeSpawnMenu))]
        [HarmonyPatch(nameof(ImguiCreativeSpawnMenu.Draw))]
        [HarmonyPostfix]
        static void ImguiCreativeSpawnMenuDrawPatch_Postfix()
        {
            foreach (var editor in AllEditors)
                editor.Draw();
        }

        [HarmonyPatch(typeof(EditorLineOfCode))]
        [HarmonyPatch(nameof(EditorLineOfCode.HandleUpdate))]
        [HarmonyPrefix]
        static bool EditorLineOfCodeHandleUpdatePatch_Prefix()
        { return false; }

        [HarmonyPatch(typeof(InputSourceCode))]
        [HarmonyPatch(nameof(InputSourceCode.HandleInput))]
        [HarmonyPrefix]
        static bool InputSourceCodeHandleInputPatch_Prefix()
        { return false; }
    }
}

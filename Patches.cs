using System.Collections.Generic;
using System.Reflection.Emit;
using Assets.Scripts.Inventory;
using HarmonyLib;

namespace ExampleMod
{
    // Example patch to change the blueprint color to blue instead of yellow,
    // when cable/pipe ports are matching
    [HarmonyPatch(typeof(InventoryManager), nameof(InventoryManager.PlacementMode))]
    public static class Patch_InventoryManager_PlacementMode
    {
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions
        )
        {
            var code = new List<CodeInstruction>(instructions);

            // Getters for Color.yellow and Color.blue
            var colorYellowGetter = AccessTools.PropertyGetter(
                typeof(UnityEngine.Color),
                nameof(UnityEngine.Color.yellow)
            );
            var colorBlueGetter = AccessTools.PropertyGetter(
                typeof(UnityEngine.Color),
                nameof(UnityEngine.Color.blue)
            );

            for (int i = 0; i < code.Count; i++)
            {
                // Replace any call to Color.yellow with Color.blue
                if (code[i].Calls(colorYellowGetter))
                {
                    code[i] = new CodeInstruction(OpCodes.Call, colorBlueGetter)
                        .WithLabels(code[i].labels) // Preserve labels, if any
                        .WithBlocks(code[i].blocks); // Preserve exception blocks, if any
                }
            }
            return code;
        }
    }
}

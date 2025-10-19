using System.Diagnostics.CodeAnalysis;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using HarmonyLib;

namespace InventoryTweaks.Patches;

internal class InventoryWindowManagerPatches
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindow))]
    [HarmonyPatch(nameof(InventoryWindow.SetVisible))]
    public static void SetVisible_Prefix(
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        InventoryWindow __instance,
        [SuppressMessage("ReSharper", "IdentifierTypo")]
        bool isVisble)
    {
        // This fixes the phantom window issue. The layout will render all items that are active.
        // If we mark this game object as inactive, it will no longer be rendered in the layout.
        // This results in no gaps between open windows.
        __instance.RectTransform.gameObject.SetActive(isVisble);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindow))]
    [HarmonyPatch(nameof(InventoryWindow.Assign))]
    public static void Assign(
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        InventoryWindow __instance,
        Slot parentSlot)
    {
        // Minor fix to window names, used for debugging windows.
        if (string.IsNullOrEmpty(parentSlot.StringKey))
            __instance.name = $"Window {parentSlot.Get()?.DisplayName}";
    }
}
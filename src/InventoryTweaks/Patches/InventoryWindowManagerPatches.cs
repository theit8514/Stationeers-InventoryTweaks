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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindowManager), "OrderVisibleSlots")]
    public static bool OrderVisibleSlots_Prefix(
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        InventoryWindowManager __instance,
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        ref SlotDisplayButton __state)
    {
        // Store the current scroll button before reordering
        __state = InventoryWindowManager.CurrentScollButton;

        // Let the original method run
        return true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(InventoryWindowManager), "OrderVisibleSlots")]
    public static void OrderVisibleSlots_Postfix(
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        InventoryWindowManager __instance,
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        SlotDisplayButton __state)
    {
        // After reordering, try to restore the selection
        if (__state?.Slot != null)
            __instance.TryUpdateSelectedInventorySlot(__state.Slot);
    }
}
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using HarmonyLib;

namespace InventoryTweaks.Patches;

internal class InventoryManagerPatches
{
    /// <summary>
    ///     Completely replace the SmartStow function.
    /// </summary>
    /// <param name="selectedSlot"></param>
    /// <returns><see langword="false" /> to stop base game execution</returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryManager))]
    [HarmonyPatch(nameof(InventoryManager.SmartStow))]
    [HarmonyPriority(2000)]
    public static bool SmartStow_Prefix(Slot selectedSlot)
    {
        NewInventoryManager.SmartStow(selectedSlot);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryWindowManager), "SinglePressInteraction")]
    public static bool SinglePressInteraction_Prefix()
    {
        return !NewInventoryManager.BeforeSinglePressInteraction(InventoryWindowManager.CurrentScollButton);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.AllowSwap), typeof(Slot), typeof(DynamicThing))]
    // ReSharper disable once InconsistentNaming
    public static bool AllowSwap_Prefix(ref bool __result, Slot sourceSlot, DynamicThing destination)
    {
        if (sourceSlot?.Get() == null || destination == null)
            return true;

        if (NewInventoryManager.AllowSwap(sourceSlot, destination) == false)
        {
            __result = false;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.AllowSwap), typeof(Slot), typeof(Slot))]
    // ReSharper disable once InconsistentNaming
    public static bool AllowSwap_Prefix(ref bool __result, Slot sourceSlot, Slot destinationSlot)
    {
        if (sourceSlot?.Get() == null || destinationSlot?.Get() == null)
            return true;

        if (NewInventoryManager.AllowSwap(sourceSlot, destinationSlot) == false)
        {
            __result = false;
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.AllowMove))]
    // ReSharper disable once InconsistentNaming
    public static bool AllowMove_Prefix(ref bool __result, DynamicThing thing, Slot destinationSlot)
    {
        if (thing == null || destinationSlot == null)
            return true;

        if (NewInventoryManager.AllowMove(thing, destinationSlot) == false)
        {
            __result = false;
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Prefix the PlayerMoveToSlot function to add to our OriginalSlots dictionary.
    /// </summary>
    /// <param name="__instance">The slot that the item is being moved to</param>
    /// <param name="thingToMove">The thing being moved</param>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.PlayerMoveToSlot))]
    // ReSharper disable once InconsistentNaming
    public static bool PlayerMoveToSlot_Prefix(Slot __instance, DynamicThing thingToMove)
    {
        return NewInventoryManager.BeforePlayerMoveToSlot(__instance, thingToMove);
    }
}
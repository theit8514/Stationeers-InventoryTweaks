using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using HarmonyLib;

namespace InventoryTweaks.Patches;

internal class InventoryManagerPatches
{
    /// <summary>
    ///     Completely replace the DoubleClickMoveToHand function.
    /// </summary>
    /// <param name="selectedSlot"></param>
    /// <returns><see langword="false" /> to stop base game execution</returns>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryManager))]
    [HarmonyPatch(nameof(InventoryManager.DoubleClickMoveToHand))]
    [HarmonyPriority(2000)]
    public static bool DoubleClickMoveToHand_Prefix(Slot selectedSlot)
    {
        NewInventoryManager.DoubleClickMove(selectedSlot);
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.AllowSwap), typeof(Slot), typeof(DynamicThing))]
    // ReSharper disable once InconsistentNaming
    public static bool AllowSwap_Prefix(ref bool __result, Slot sourceSlot, DynamicThing destination)
    {
        if (sourceSlot?.Occupant == null || destination == null)
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
        if (sourceSlot?.Occupant == null || destinationSlot?.Occupant == null)
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
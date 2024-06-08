using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using HarmonyLib;
using Objects.Items;
using UnityEngine;

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
        if (sourceSlot?.Get() == null || destination == null)
        {
            return true;
        }

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
        {
            return true;
        }

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
        {
            return true;
        }

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

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ToolUse), nameof(ToolUse.Deconstruct))]
    [System.Obsolete]
    // ReSharper disable once InconsistentNaming
    public static bool OnUseItem_Prefix(ToolUse __instance, ConstructionEventInstance eventInstance)
    {
        SpawnItem(__instance, eventInstance, __instance.ToolEntry);
        SpawnItem(__instance, eventInstance, __instance.ToolEntry2);

        return false;
    }

    private static void SpawnItem(ToolUse toolUse, ConstructionEventInstance eventInstance, Item tool)
    {
        if (tool == null)
        {
            return;
        }

        int num = (tool == toolUse.ToolEntry) ? toolUse.EntryQuantity : toolUse.EntryQuantity2;
        if (tool is Tool || num == 0)
        {
            return;
        }

        Stackable stackable = tool as Stackable;
        Slot slot = null;
        if (eventInstance.OtherHandSlot != null)
        {

            if (stackable)
            {
                Stackable stackable2 = eventInstance.OtherHandSlot.Get<Stackable>();
                if (stackable2 && stackable2.CanStack(stackable))
                {
                    int num2 = Mathf.Min(stackable2.MaxQuantity - stackable2.Quantity, num);
                    if (num2 > 0)
                    {
                        _ = stackable2.AddQuantity(num2);
                        num -= num2;
                    }
                }
            }
        }

        if (num <= 0)
        {
            return;
        }

        Item item = OnServer.Create<Item>(tool, eventInstance.Position, eventInstance.Rotation);
        if (item)
        {
            if (slot != null)
            {
                OnServer.MoveToSlot(item, slot);
            }

            Stackable stackable3 = item as Stackable;
            if (stackable3)
            {
                stackable3.SetQuantity(num);
            }

            Consumable consumable = item as Consumable;
            if (consumable)
            {
                consumable.Quantity = num;
            }

            if (item is IConstructionKit && eventInstance.Parent is Structure structure && structure.CurrentBuildStateIndex <= 0 && eventInstance.Parent.PaintableMaterial is not null && eventInstance.Parent.CustomColor.Index != item.CustomColor.Index)
            {
                OnServer.SetCustomColor(item, eventInstance.Parent.CustomColor.Index);
            }

            _ = NewInventoryManager.MoveItem(stackable3);
        }
    }
}
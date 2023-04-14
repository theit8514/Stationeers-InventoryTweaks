using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;

namespace InventoryTweaks.Patches;

internal class InventoryManagerPatches
{
    private static readonly List<InteractableType> ExcludeHandSlots = new()
    {
        InteractableType.Slot1,
        InteractableType.Slot2
    };

    [HarmonyPrefix]
    [HarmonyPatch(typeof(InventoryManager))]
    [HarmonyPatch(nameof(InventoryManager.DoubleClickMoveToHand))]
    public static bool DoubleClickMoveToHand_Prefix(Slot selectedSlot)
    {
        // Continue to base code for non-stacking items
        if (selectedSlot == null || selectedSlot.Occupant == null || selectedSlot.Occupant is not Stackable stack)
            return true;

        // If the selected item is in our hand, try to fill the inventory first
        if (InventoryManager.LeftHandSlot.Occupant == selectedSlot.Occupant ||
            InventoryManager.RightHandSlot.Occupant == selectedSlot.Occupant)
        {
            Plugin.Log.LogInfo($"Finding slot in inventory to place {stack.Quantity} of {stack.DisplayName}");
            // Search for valid slots in the inventory
            // Sorted by quantity descending (fill larger stacks first)
            var slots = GetSlotsWithSameStack(stack, ExcludeHandSlots)
                .OrderByDescending(slot => slot.Stack.Quantity)
                .ToArray();
            Plugin.Log.LogDebug($"Found {slots.Length} slots to place the item");
            foreach (var stackSlot in slots)
            {
                var slot = stackSlot.Slot;
                var targetStack = stackSlot.Stack;

                // Merge the items into the target stack.
                var target = $"slot {slot.SlotId} {slot.DisplayName}";
                Plugin.Log.LogInfo(
                    $"Merging {stack.Quantity} items into {target} which has {targetStack.Quantity} items.");
                OnServer.Merge(targetStack, stack);
                // The source stack will now contain the remaining quantity or zero.
                Plugin.Log.LogDebug($"Target quantity: {targetStack.Quantity} Source quantity: {stack.Quantity}");

                // Break from this loop if there are no more items to process.
                if (stack.Quantity <= 0)
                    break;
            }

            // If there are no more items to process then skip the base code execution
            if (stack.Quantity <= 0)
            {
                UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                return false;
            }

            // A partial stack was left over, return true to continue base code execution
            Plugin.Log.LogInfo("Remaining items have no partial stacks to fill..." +
                               "Continuing to main game handler.");
            return true;
        }

        // Try to fill hand slots with this item
        return FillHandSlot(InventoryManager.LeftHandSlot, selectedSlot, stack) &&
               FillHandSlot(InventoryManager.RightHandSlot, selectedSlot, stack);
    }

    private static bool FillHandSlot(Slot targetSlot, Slot selectedSlot, Stackable stack)
    {
        Plugin.Log.LogDebug($"Hand slot {targetSlot.DisplayName} occupant: {targetSlot.Occupant?.DisplayName}");
        if (targetSlot.Occupant == null ||
            targetSlot.Occupant == selectedSlot.Occupant)
            return true;

        var targetStack = targetSlot.Occupant as Stackable;
        if (targetStack == null || !targetStack.CanStack(stack))
            return true;

        // Merge the items into the target stack.
        Plugin.Log.LogInfo(
            $"Merging {stack.Quantity} items into {targetSlot.DisplayName} which has {targetStack.Quantity} items.");
        OnServer.Merge(targetStack, stack);
        // The source stack will now contain the remaining quantity or zero.
        Plugin.Log.LogDebug($"Target quantity: {targetStack.Quantity} Source quantity: {stack.Quantity}");

        if (stack.Quantity <= 0)
        {
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            return false;
        }

        // Continue processing
        return true;
    }

    private static IEnumerable<StackSlot> GetSlotsWithSameStack(Stackable stack,
        ICollection<InteractableType> excludeTypes)
    {
        var slots = FindSlotsOfHuman(excludeTypes) // Get slots of the attached player
            .Concat(FindSlotsFromOpenWindows()) // and all slots from open containers
            .Where(slot => slot.Occupant != null); // Only include occupied slots
        foreach (var slot in slots)
        {
            // If the slot occupant is not a stack item, skip it
            if (slot.Occupant is not Stackable occupantStack)
                continue;

            // If our source stack can stack with the target stack and
            // the target stack is not full, add it to our valid slots.
            if (occupantStack.CanStack(stack) && !occupantStack.IsStackFull)
                yield return new StackSlot(occupantStack, slot);
        }
    }

    private static IEnumerable<Slot> FindSlotsOfHuman(ICollection<InteractableType> excludeTypes)
    {
        return InventoryManager.ParentHuman.Slots // get base inventory slots
            .Where(slot => !excludeTypes.Contains(slot.Action)); // except slots from the excludeTypes
    }

    private static IEnumerable<Slot> FindSlotsFromOpenWindows(
        bool requiredVisible = true)
    {
        return InventoryWindowManager.Instance.Windows
            .Where(window => !(!window.IsVisible & requiredVisible))
            .SelectMany(window => window.Parent.Slots);
    }

    private class StackSlot
    {
        public StackSlot(Stackable stack, Slot slot)
        {
            Stack = stack;
            Slot = slot;
        }

        public Stackable Stack { get; }
        public Slot Slot { get; }
    }
}
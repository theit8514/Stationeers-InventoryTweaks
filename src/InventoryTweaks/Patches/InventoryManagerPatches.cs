using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using HarmonyLib;
using InventoryTweaks.Helpers;
using System;
using System.Collections;
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

    private static readonly Dictionary<long, Slot> OriginalSlots = new();
    private static readonly Traverse FindFreeSlotOpenWindowsSlotPriority;
    private static readonly Traverse PerformHiddenSlotMoveToAnimation;

    static InventoryManagerPatches()
    {
        var traverse = Traverse.Create(typeof(InventoryManager));
        FindFreeSlotOpenWindowsSlotPriority =
            traverse.Method("FindFreeSlotOpenWindowsSlotPriority", new[] { typeof(Slot.Class), typeof(bool) });
        PerformHiddenSlotMoveToAnimation =
            traverse.Method("PerformHiddenSlotMoveToAnimation",
                new[] { typeof(Slot), typeof(Slot), typeof(DynamicThing) });
    }

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
        if (selectedSlot == null || selectedSlot.Occupant == null)
            return false;
        try
        {
            // If the item is stackable, run our stackable code.
            if (selectedSlot.Occupant is Stackable stack && !DoubleClickMoveToHand_Stackable(selectedSlot, stack))
                return false;

            // Replace the base game DoubleClickMoveToHand with our own.
            DoubleClickMoveToHand_Normal(selectedSlot);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Exception encountered on DoubleClickMoveToHand: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
        }

        return false;
    }

    /// <summary>
    ///     Prefix the PlayerMoveToSlot function to add to our OriginalSlots dictionary.
    /// </summary>
    /// <param name="__instance">The slot that the item is being moved to</param>
    /// <param name="thingToMove">The thing being moved</param>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Slot), nameof(Slot.PlayerMoveToSlot))]
    // ReSharper disable once InconsistentNaming
    public static void PlayerMoveToSlot_Prefix(Slot __instance, DynamicThing thingToMove)
    {
        // When we move an item to the left/right hand slot, store the original slot reference.
        // Except if the source slot is a hand (prevents weird overwrites of original slot data).
        if (thingToMove.ParentSlot != null && SlotHelper.IsHandSlot(__instance) &&
            !SlotHelper.IsHandSlot(thingToMove.ParentSlot))
            OriginalSlots[thingToMove.ReferenceId] = thingToMove.ParentSlot;
    }

    /// <summary>
    ///     Handles a stackable item that was double clicked. This will attempt to find items of the
    ///     same stackable type and fill those slots.
    /// </summary>
    /// <param name="selectedSlot"></param>
    /// <param name="stack"></param>
    /// <returns>
    ///     <see langword="true" /> if the stack still remains, or <see langword="false" /> if the slot was processed
    ///     completely.
    /// </returns>
    private static bool DoubleClickMoveToHand_Stackable(Slot selectedSlot, Stackable stack)
    {
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
                var target = $"slot {SlotHelper.GetSlotDisplayName(slot)}";
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
                               "Continuing to main item handler.");
            return true;
        }

        // Try to fill hand slots with this item
        return FillHandSlot(InventoryManager.LeftHandSlot, selectedSlot, stack) &&
               FillHandSlot(InventoryManager.RightHandSlot, selectedSlot, stack);
    }

    /// <summary>
    ///     Handles a normal item being moved using double click. Looks up where the item was originally
    ///     stored and tries to place it in that slot. Otherwise, continue normal slot processing.
    /// </summary>
    /// <param name="selectedSlot"></param>
    private static void DoubleClickMoveToHand_Normal(Slot selectedSlot)
    {
        if (InventoryManager.LeftHandSlot.Occupant == selectedSlot.Occupant ||
            InventoryManager.RightHandSlot.Occupant == selectedSlot.Occupant)
        {
            Slot targetSlot = null;
            // Find this Thing reference in the original slots dictionary.
            if (OriginalSlots.TryGetValue(selectedSlot.Occupant.ReferenceId, out var originalSlot))
            {
                Plugin.Log.LogInfo(
                    $"Returning {selectedSlot.Occupant.DisplayName} to original slot {SlotHelper.GetSlotDisplayName(originalSlot)}");
                // Checks to ensure that we can move this item back to this slot:
                if (originalSlot.Parent?.RootParentHuman == null)
                    Plugin.Log.LogWarning("Original slot was not attached to a human");
                else if (originalSlot.Parent.RootParentHuman != InventoryManager.ParentHuman)
                    Plugin.Log.LogWarning("Original slot was not our human");
                else if (originalSlot.Occupant != null)
                    Plugin.Log.LogWarning("Original slot is already filled");
                else if (!(originalSlot.Type == Slot.Class.None || // And the slot is type None
                           originalSlot.Type == selectedSlot.Occupant.SlotType))
                    Plugin.Log.LogWarning("Original slot is not a valid type for this item");
                else
                    // Set the target slot to the original slot
                    targetSlot = originalSlot;

                // Always clear the slot data so that we don't leave the reference around
                OriginalSlots.Remove(selectedSlot.Occupant.ReferenceId);
            }

            // This code is largely unchanged from the base code, except for the ordering of operations.
            // If we didn't find the original slot, find a free slot of this type or a slot from the open inventory.
            targetSlot ??= InventoryManager.ParentHuman.GetFreeSlot(selectedSlot.Occupant.SlotType, ExcludeHandSlots) ??
                           FindFreeSlotOpenWindowsSlotPriority.GetValue<Slot>(selectedSlot.Occupant.SlotType, true);
            if (targetSlot == null)
            {
                // If the slot was not found, find the next available slot in the main slots.
                foreach (var slot in InventoryManager.ParentHuman.Slots)
                {
                    if (slot == null || ExcludeHandSlots.Contains(slot.Action) || slot.Occupant == null)
                        continue;

                    var freeSlot = slot.Occupant.GetFreeSlot(selectedSlot.Occupant.SlotType);
                    if (freeSlot == null)
                        continue;

                    targetSlot = freeSlot;
                    InventoryManager.Instance.StartCoroutine(
                        PerformHiddenSlotMoveToAnimation.GetValue<IEnumerator>(slot, selectedSlot,
                            selectedSlot.Occupant));
                    break;
                }
            }

            if (targetSlot == null)
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
            else
            {
                OnServer.MoveToSlot(selectedSlot.Occupant, targetSlot);
                UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
            }
        }
        else if (InventoryManager.ActiveHandSlot != null && InventoryManager.ActiveHandSlot.Occupant == null)
        {
            OriginalSlots[selectedSlot.Occupant.ReferenceId] = selectedSlot;
            OnServer.MoveToSlot(selectedSlot.Occupant, InventoryManager.ActiveHandSlot);
            UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
        }
        else if (InventoryManager.Instance.InactiveHand?.Slot != null &&
                 InventoryManager.Instance.InactiveHand.Slot.Occupant == null)
        {
            OriginalSlots[selectedSlot.Occupant.ReferenceId] = selectedSlot;
            OnServer.MoveToSlot(selectedSlot.Occupant, InventoryManager.Instance.InactiveHand.Slot);
            UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
        }
        else
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
    }

    private static bool FillHandSlot(Slot targetSlot, Slot selectedSlot, Stackable stack)
    {
        Plugin.Log.LogDebug(
            $"Hand slot {SlotHelper.GetSlotDisplayName(targetSlot)} occupant: {targetSlot.Occupant?.DisplayName}");
        if (targetSlot.Occupant == null ||
            targetSlot.Occupant == selectedSlot.Occupant)
            return true;

        var targetStack = targetSlot.Occupant as Stackable;
        if (targetStack == null || !targetStack.CanStack(stack))
            return true;

        // Merge the items into the target stack.
        Plugin.Log.LogInfo(
            $"Merging {stack.Quantity} items into {SlotHelper.GetSlotDisplayName(targetSlot)} which has {targetStack.Quantity} items.");
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
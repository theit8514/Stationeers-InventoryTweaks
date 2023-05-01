using Assets.Scripts;
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

namespace InventoryTweaks;

public static class NewInventoryManager
{
    public static readonly InventoryTweaksData Data = new();

    private static readonly List<InteractableType> ExcludeHandSlots = new()
    {
        InteractableType.Slot1,
        InteractableType.Slot2
    };

    private static readonly Dictionary<long, Slot> OriginalSlots = new();
    private static readonly Traverse FindFreeSlotOpenWindowsSlotPriority;
    private static readonly Traverse PerformHiddenSlotMoveToAnimation;

    static NewInventoryManager()
    {
        var traverse = Traverse.Create(typeof(InventoryManager));
        FindFreeSlotOpenWindowsSlotPriority =
            traverse.Method("FindFreeSlotOpenWindowsSlotPriority", new[] { typeof(Slot.Class), typeof(bool) });
        PerformHiddenSlotMoveToAnimation =
            traverse.Method("PerformHiddenSlotMoveToAnimation",
                new[] { typeof(Slot), typeof(Slot), typeof(DynamicThing) });
    }

    /// <summary>
    ///     Replacement function for DoubleClickMoveToHand
    /// </summary>
    /// <param name="selectedSlot"></param>
    public static void DoubleClickMove(Slot selectedSlot)
    {
        if (selectedSlot == null || selectedSlot.Occupant == null)
            return;
        try
        {
            var targetSlots = GetTargetSlotsOrdered(selectedSlot.Occupant)
                .ToArray();

            // If the item is stackable, run our stackable code.
            if (selectedSlot.Occupant is Stackable stack && !DoubleClickMoveStackable(selectedSlot, stack, targetSlots))
                return;

            if (InventoryManager.LeftHandSlot.Occupant == selectedSlot.Occupant ||
                InventoryManager.RightHandSlot.Occupant == selectedSlot.Occupant)
                DoubleClickMoveToInventory(selectedSlot, targetSlots);
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
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Exception encountered on DoubleClickMoveToHand: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
        }
    }

    /// <summary>
    ///     Run before the PlayerMoveToSlot method to add to our OriginalSlots dictionary.
    /// </summary>
    /// <param name="targetSlot">The slot that the item is being moved to</param>
    /// <param name="thingToMove">The thing being moved</param>
    public static bool BeforePlayerMoveToSlot(Slot targetSlot, DynamicThing thingToMove)
    {
        // Check to see if this slot can accept this item.
        if (!Data.CanPlaceInSlot(thingToMove, targetSlot))
        {
            ConsoleWindow.PrintAction("That slot is locked and cannot store this item.");
            return false;
        }

        // When we move an item to the left/right hand slot, store the original slot reference.
        // Except if the source slot is a hand (prevents weird overwrites of original slot data).
        if (thingToMove.ParentSlot != null && SlotHelper.IsHandSlot(targetSlot) &&
            !SlotHelper.IsHandSlot(thingToMove.ParentSlot))
            OriginalSlots[thingToMove.ReferenceId] = thingToMove.ParentSlot;

        return true;
    }

    /// <summary>
    ///     Handles a stackable item that was double clicked. This will attempt to find items of the
    ///     same stackable type and fill those slots.
    /// </summary>
    /// <param name="selectedSlot"></param>
    /// <param name="stack"></param>
    /// <param name="targetSlots"></param>
    /// <returns>
    ///     <see langword="true" /> if the stack still remains, or <see langword="false" /> if the slot was processed
    ///     completely.
    /// </returns>
    private static bool DoubleClickMoveStackable(Slot selectedSlot, Stackable stack, IEnumerable<SlotData> targetSlots)
    {
        // If the selected item is in our hand, try to fill the inventory first
        if (InventoryManager.LeftHandSlot.Occupant == selectedSlot.Occupant ||
            InventoryManager.RightHandSlot.Occupant == selectedSlot.Occupant)
        {
            Plugin.Log.LogInfo($"Finding slot in inventory to place {stack.Quantity} of {stack.DisplayName}");
            // Search for valid slots in the inventory
            // Sorted by quantity descending (fill larger stacks first)
            var slots = targetSlots
                .Where(x => x.IsStackable &&
                            x.Stackable.CanStack(stack) &&
                            !x.Stackable.IsStackFull)
                .OrderByDescending(slot => slot.Stackable.Quantity)
                .ToArray();
            Plugin.Log.LogDebug($"Found {slots.Length} slots to place the item");
            foreach (var stackSlot in slots)
            {
                var slot = stackSlot.Slot;
                var targetStack = stackSlot.Stackable;

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
    /// <param name="targetSlots"></param>
    private static void DoubleClickMoveToInventory(Slot selectedSlot, IEnumerable<SlotData> targetSlots)
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

        if (targetSlot == null)
        {
            Plugin.Log.LogInfo($"Finding slot for {selectedSlot.Occupant.DisplayName}");
            // Find slot that is not occupied.
            foreach (var slot in targetSlots.Where(x => x.IsOccupied == false))
            {
                Plugin.Log.LogInfo(
                    $"Slot {SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}");
                if (slot.IsLocked && slot.LockedToPrefabHash != selectedSlot.Occupant.PrefabHash)
                    continue;

                targetSlot = slot.Slot;
                break;
            }
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

    private static IEnumerable<SlotData> GetTargetSlotsOrdered(DynamicThing thing)
    {
        var prefabHash = thing.GetPrefabHash();
        var slotLookup = Data.ToLookup();
        var humanInventorySlots = FindSlotsOfHuman(ExcludeHandSlots);
        var allHumanSlots = humanInventorySlots
            .SelectMany(RecurseSlots)
            .Select(slot => BuildSlotData(slot, slotLookup))
            .ToArray();
        var sortedSlots = allHumanSlots
            .Where(x => x.IsOfSlotTypeOrNoneType(thing.SlotType)) // Only allow slots of this type or none type
            .Where(x => x.IsLockedToOrNotLocked(prefabHash)) // Only allow non-locked slots or slots locked to this type
            .OrderByDescending(x => x.IsVisible) // Sort first by visible windows.
            .ThenByDescending(x =>
                x.IsOccupied && x.OccupantPrefabHash == prefabHash) // Then by occupied slots (for stacking)
            .ThenByDescending(x => x.IsLocked && x.LockedToPrefabHash == prefabHash) // Then by locked slots
            .ThenByDescending(x => x.IsOfSlotType(thing.SlotType)) // Then by slots of this type
            .ThenBy(x => x.IsOccupied == false) // Then by non-occupied slots
            .ToArray();
        Plugin.Log.LogInfo("Slots: " + string.Join("\r\n",
            sortedSlots.Select(slot =>
                $"Slot {SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}")));
        return sortedSlots;
    }

    private static SlotData BuildSlotData(Slot slot, Lookup<long, int, ILockedSlot> lookup)
    {
        var lockedSlotTuple = lookup[slot.Parent.ReferenceId, slot.SlotId].FirstOrDefault();
        return new SlotData(slot, lockedSlotTuple);
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
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);

        return false;
    }

    private static IEnumerable<Slot> FindSlotsOfHuman(ICollection<InteractableType> excludeTypes)
    {
        return InventoryManager.ParentHuman.Slots // get base inventory slots
            .Where(slot => !excludeTypes.Contains(slot.Action)); // except slots from the excludeTypes
    }

    private static IEnumerable<Slot> RecurseSlots(Slot slot)
    {
        yield return slot;
        if (slot.Occupant == null || !slot.Occupant.HasSlots)
            yield break;
        foreach (var childSlot in slot.Occupant.Slots.SelectMany(RecurseSlots))
        {
            yield return childSlot;
        }
    }

    public static void LockSlotAction()
    {
        // Get the current hovered slot.
        var currentSlot = SlotDisplayButton.CurrentSlot;
        // If no slot hovered or the hand slot, stop processing.
        if (currentSlot == null || SlotHelper.IsHandSlot(currentSlot.Slot))
            return;

        var parentReferenceId = currentSlot.Slot.Parent.ReferenceId;
        var slotId = currentSlot.Slot.SlotId;
        if (currentSlot.Slot.Occupant == null)
        {
            Data.UnlockSlot(parentReferenceId, slotId);
            ConsoleWindow.Print("Slot is now unlocked and may accept any item.", aged: false);
        }
        else
        {
            Data.LockSlot(parentReferenceId, slotId, currentSlot.Slot.Occupant);
            ConsoleWindow.Print($"Slot is now locked to '{currentSlot.Slot.Occupant.DisplayName}'.", aged: false);
        }
    }

    public static bool? AllowMove(DynamicThing thing, Slot destinationSlot)
    {
        if (Data.TryGetLock(destinationSlot.Parent.ReferenceId, destinationSlot.SlotId, out var destinationLock))
        {
            if (thing.GetPrefabHash() != destinationLock.PrefabHash)
                return false;
        }

        return null;
    }

    public static bool? AllowSwap(Slot sourceSlot, Slot destinationSlot)
    {
        if (Data.TryGetLock(sourceSlot.Parent.ReferenceId, sourceSlot.SlotId, out var sourceLock))
        {
            if (destinationSlot.Occupant.GetPrefabHash() != sourceLock.PrefabHash)
                return false;
        }

        if (Data.TryGetLock(destinationSlot.Parent.ReferenceId, destinationSlot.SlotId, out var destinationLock))
        {
            if (sourceSlot.Occupant.GetPrefabHash() != destinationLock.PrefabHash)
                return false;
        }

        return null;
    }

    public static bool? AllowSwap(Slot sourceSlot, DynamicThing destination)
    {
        if (Data.TryGetLock(sourceSlot.Parent.ReferenceId, sourceSlot.SlotId, out var sourceLock))
        {
            if (destination.GetPrefabHash() != sourceLock.PrefabHash)
                return false;
        }

        return null;
    }

    private class SlotData
    {
        private readonly ILockedSlot _lockedSlot;

        public SlotData(Slot slot, ILockedSlot lockedSlotTuple)
        {
            Slot = slot;
            _lockedSlot = lockedSlotTuple;
        }

        public Slot Slot { get; }
        public InventoryWindow Window => Slot.Display?.SlotWindow;
        public bool IsVisible => Window?.IsVisible ?? false;
        public DynamicThing Occupant => Slot.Occupant;
        public bool IsOccupied => Occupant != null;
        public int OccupantPrefabHash => Occupant.PrefabHash;
        public Stackable Stackable => Occupant as Stackable;
        public bool IsStackable => Stackable != null;
        public bool IsLocked => _lockedSlot != null;
        public int LockedToPrefabHash => _lockedSlot.PrefabHash;

        public bool IsOfSlotTypeOrNoneType(Slot.Class occupantType)
        {
            return IsOfSlotType(occupantType) ||
                   Slot.Type == Slot.Class.None;
        }

        public bool IsOfSlotType(Slot.Class occupantType)
        {
            return Slot.Type == occupantType;
        }

        public bool IsLockedToOrNotLocked(int prefabHash)
        {
            return !IsLocked || LockedToPrefabHash == prefabHash;
        }
    }
}
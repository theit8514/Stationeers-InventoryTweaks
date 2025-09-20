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
    ///     Replacement function for SmartStow
    /// </summary>
    /// <param name="selectedSlot"></param>
    public static void SmartStow(Slot selectedSlot)
    {
        if (selectedSlot == null || selectedSlot.Get() == null)
            return;
        try
        {
            Plugin.Log.LogDebug($"Handling SmartStow request for {selectedSlot}");
            var targetSlots = GetTargetSlotsOrdered(selectedSlot.Get())
                .ToArray();

            // If the item is stackable, run our stackable code.
            if (selectedSlot.Get() is Stackable stack && !DoubleClickMoveStackable(selectedSlot, stack, targetSlots))
                return;

            if (InventoryManager.LeftHandSlot.Get() == selectedSlot.Get() ||
                InventoryManager.RightHandSlot.Get() == selectedSlot.Get())
                DoubleClickMoveToInventory(selectedSlot, targetSlots);
            else if (InventoryManager.ActiveHandSlot != null && InventoryManager.ActiveHandSlot.Get() == null)
            {
                OriginalSlots[selectedSlot.Get().ReferenceId] = selectedSlot;
                OnServer.MoveToSlot(selectedSlot.Get(), InventoryManager.ActiveHandSlot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
            }
            else if (InventoryManager.Instance.InactiveHand?.Slot != null &&
                     InventoryManager.Instance.InactiveHand.Slot.Get() == null)
            {
                OriginalSlots[selectedSlot.Get().ReferenceId] = selectedSlot;
                OnServer.MoveToSlot(selectedSlot.Get(), InventoryManager.Instance.InactiveHand.Slot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
            }
            else
                UIAudioManager.Play(UIAudioManager.ActionFailHash);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Exception encountered on SmartStow: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
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
        if (InventoryManager.LeftHandSlot.Get() == selectedSlot.Get() ||
            InventoryManager.RightHandSlot.Get() == selectedSlot.Get())
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
    private static void DoubleClickMoveToInventory(Slot selectedSlot, SlotData[] targetSlots)
    {
        var thing = selectedSlot.Get();
        Slot targetSlot = null;
        // Find this Thing reference in the original slots dictionary.
        if (OriginalSlots.TryGetValue(thing.ReferenceId, out var originalSlot))
        {
            Plugin.Log.LogInfo(
                $"Returning {thing.DisplayName} to original slot {SlotHelper.GetSlotDisplayName(originalSlot)}");
            // Checks to ensure that we can move this item back to this slot:
            if (originalSlot.Parent?.RootParentHuman == null)
                Plugin.Log.LogWarning("Original slot was not attached to a human");
            else if (originalSlot.Parent.RootParentHuman != InventoryManager.ParentHuman)
                Plugin.Log.LogWarning("Original slot was not our human");
            else if (originalSlot.Get() != null)
                Plugin.Log.LogWarning("Original slot is already filled");
            else if (!(originalSlot.Type == Slot.Class.None || // And the slot is type None
                       originalSlot.Type == thing.SlotType))
                Plugin.Log.LogWarning("Original slot is not a valid type for this item");
            else if (targetSlots.All(x => !ReferenceEquals(x.Slot, originalSlot)))
                Plugin.Log.LogWarning("Original slot is not a valid target");
            else
                // Set the target slot to the original slot
                targetSlot = originalSlot;

            // Always clear the slot data so that we don't leave the reference around
            OriginalSlots.Remove(thing.ReferenceId);
        }

        if (targetSlot == null)
        {
            Plugin.Log.LogInfo($"Finding slot for {thing.DisplayName}");
            // Find slot that is not occupied.
            foreach (var slot in targetSlots.Where(x => x.IsOccupied == false))
            {
                Plugin.Log.LogInfo(
                    $"Slot {SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}");
                if (slot.IsLocked && slot.LockedToPrefabHash != thing.PrefabHash)
                    continue;

                targetSlot = slot.Slot;
                break;
            }
        }

        if (targetSlot == null && targetSlots.All(x => x.IsOccupied || x.IsLocked))
        {
            ConsoleWindow.Print($"Can't place '{thing.DisplayName}' in inventory, all slots are occupied or locked", aged: false);
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
            return;
        }

        // This code is largely unchanged from the base code, except for the ordering of operations.
        // If we didn't find the original slot, find a free slot of this type or a slot from the open inventory.
        targetSlot ??= InventoryManager.ParentHuman.GetFreeSlot(thing.SlotType, ExcludeHandSlots) ??
                       FindFreeSlotOpenWindowsSlotPriority.GetValue<Slot>(thing.SlotType, true);
        if (targetSlot == null)
        {
            // If the slot was not found, find the next available slot in the main slots.
            foreach (var slot in InventoryManager.ParentHuman.Slots)
            {
                if (slot == null || ExcludeHandSlots.Contains(slot.Action) || slot.Get() == null)
                    continue;

                var freeSlot = slot.Get().GetFreeSlot(thing.SlotType);
                if (freeSlot == null)
                    continue;

                targetSlot = freeSlot;
                InventoryManager.Instance.StartCoroutine(
                    PerformHiddenSlotMoveToAnimation.GetValue<IEnumerator>(slot, selectedSlot,
                        thing));
                break;
            }
        }

        if (targetSlot == null)
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
        else
        {
            OnServer.MoveToSlot(selectedSlot.Get(), targetSlot);
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
        Plugin.Log.LogInfo("Slots: \r\n" + string.Join("\r\n",
            sortedSlots.Select(slot =>
                $"Slot {SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}")));
        return sortedSlots;
    }

    private static SlotData BuildSlotData(Slot slot, Lookup<long, int, ILockedSlot> lookup)
    {
        var lockedSlotTuple = lookup[slot.Parent.ReferenceId, slot.SlotIndex].FirstOrDefault();
        return new SlotData(slot, lockedSlotTuple);
    }

    private static bool FillHandSlot(Slot targetSlot, Slot selectedSlot, Stackable stack)
    {
        Plugin.Log.LogDebug(
            $"Hand slot {SlotHelper.GetSlotDisplayName(targetSlot)} occupant: {targetSlot.Get()?.DisplayName}");
        if (targetSlot.Get() == null ||
            targetSlot.Get() == selectedSlot.Get())
            return true;

        var targetStack = targetSlot.Get() as Stackable;
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
        if (slot.Get() == null || !slot.Get().HasSlots)
            yield break;
        foreach (var childSlot in slot.Get().Slots.SelectMany(RecurseSlots))
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
        var slotIndex = currentSlot.Slot.SlotIndex;
        if (currentSlot.Slot.Get() == null)
        {
            Data.UnlockSlot(parentReferenceId, slotIndex);
            ConsoleWindow.Print("Slot is now unlocked and may accept any item.", aged: false);
        }
        else
        {
            Data.LockSlot(parentReferenceId, slotIndex, currentSlot.Slot.Get());
            ConsoleWindow.Print($"Slot is now locked to '{currentSlot.Slot.Get().DisplayName}'.", aged: false);
        }
    }

    public static bool? AllowMove(DynamicThing thing, Slot destinationSlot)
    {
        if (Data.TryGetLock(destinationSlot.Parent.ReferenceId, destinationSlot.SlotIndex, out var destinationLock))
        {
            if (thing.GetPrefabHash() != destinationLock.PrefabHash)
                return false;
        }

        return null;
    }

    public static bool? AllowSwap(Slot sourceSlot, Slot destinationSlot)
    {
        if (Data.TryGetLock(sourceSlot.Parent.ReferenceId, sourceSlot.SlotIndex, out var sourceLock))
        {
            if (destinationSlot.Get().GetPrefabHash() != sourceLock.PrefabHash)
                return false;
        }

        if (Data.TryGetLock(destinationSlot.Parent.ReferenceId, destinationSlot.SlotIndex, out var destinationLock))
        {
            if (sourceSlot.Get().GetPrefabHash() != destinationLock.PrefabHash)
                return false;
        }

        return null;
    }

    public static bool? AllowSwap(Slot sourceSlot, DynamicThing destination)
    {
        if (Data.TryGetLock(sourceSlot.Parent.ReferenceId, sourceSlot.SlotIndex, out var sourceLock))
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
        public DynamicThing Occupant => Slot.Get();
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
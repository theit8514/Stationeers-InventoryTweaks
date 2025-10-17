using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;
using HarmonyLib;
using InventoryTweaks.Helpers;

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
    private static readonly Traverse IsValid;

    static NewInventoryManager()
    {
        var traverse = Traverse.Create(typeof(InventoryManager));
        FindFreeSlotOpenWindowsSlotPriority =
            traverse.Method("FindFreeSlotOpenWindowsSlotPriority", new[] { typeof(Slot.Class), typeof(bool) });
        PerformHiddenSlotMoveToAnimation =
            traverse.Method("PerformHiddenSlotMoveToAnimation",
                new[] { typeof(Slot), typeof(Slot), typeof(DynamicThing) });

        var traverse2 = Traverse.Create(typeof(InventoryWindowManager));
        IsValid = traverse2.Method("IsValid");
    }

    /// <summary>
    ///     Replacement function for SmartStow that intelligently handles item placement.
    ///     Attempts to move items to appropriate slots based on type, stackability, and locked slot rules.
    /// </summary>
    /// <param name="selectedSlot">The slot containing the item to be stowed</param>
    /// <returns>True if the item was successfully stowed, false otherwise</returns>
    public static bool SmartStow(Slot selectedSlot)
    {
        if (selectedSlot == null || selectedSlot.Get() == null)
            return false;
        try
        {
            Plugin.Log.LogDebug($"Handling SmartStow request for {SlotHelper.GetSlotDisplayName(selectedSlot)}");
            var targetSlots = GetTargetSlotsOrdered(selectedSlot.Get())
                .ToArray();

            // If the item is stackable, run our stackable code.
            if (selectedSlot.Get() is Stackable stack && !DoubleClickMoveStackable(selectedSlot, stack, targetSlots))
                return false;

            if (InventoryManager.LeftHandSlot.Get() == selectedSlot.Get() ||
                InventoryManager.RightHandSlot.Get() == selectedSlot.Get())
            {
                Plugin.Log.LogDebug("Moving hand to inventory");
                return DoubleClickMoveToInventory(selectedSlot, targetSlots);
            }

            var activeHand = InventoryManager.ActiveHandSlot;
            if (activeHand != null && activeHand.Get() == null)
            {
                Plugin.Log.LogDebug("Moving inventory to active hand");
                OriginalSlots[selectedSlot.Get().ReferenceId] = selectedSlot;
                InventoryManager.Instance.CheckCancelMultiConstructor();
                OnServer.MoveToSlot(selectedSlot.Get(), InventoryManager.ActiveHandSlot);
                InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(InventoryManager.ActiveHandSlot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                return true;
            }

            var inactiveHand = InventoryManager.Instance.InactiveHand?.Slot;
            if (inactiveHand != null && inactiveHand.Get() == null)
            {
                Plugin.Log.LogDebug("Moving inventory to active hand");
                OriginalSlots[selectedSlot.Get().ReferenceId] = selectedSlot;
                InventoryManager.Instance.CheckCancelMultiConstructor();
                OnServer.MoveToSlot(selectedSlot.Get(), InventoryManager.Instance.InactiveHand.Slot);
                InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(InventoryManager.Instance.InactiveHand
                    .Slot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                return true;
            }

            UIAudioManager.Play(UIAudioManager.ActionFailHash);
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError(
                $"Exception encountered on SmartStow: {ex.Message}{Environment.NewLine}{ex.StackTrace}");
            return false;
        }
    }


    /// <summary>
    ///     Handles single press interactions with inventory slots, providing enhanced behavior
    ///     when the inventory select override is enabled.
    /// </summary>
    /// <param name="currentScrollButton">The currently selected scroll button in the inventory</param>
    /// <returns>True if the interaction was handled by this method, false to use default behavior</returns>
    public static bool BeforeSinglePressInteraction(SlotDisplayButton currentScrollButton)
    {
        // If config option is disabled, use default handling
        if (!ConfigHelper.General.EnableOverrideInventorySelect)
            return false;

        // Fall-through cases for scroll not initialized, buttons and non-slot items
        if (!currentScrollButton.IsVisible || currentScrollButton.Interactable != null ||
            currentScrollButton.Slot == null)
            return false;

        var valid = IsValid.GetValue<KeyResult>();
        switch (valid)
        {
            case KeyResult.Invalid:
                // This is a special case where the game thinks we can't swap, but we should be good to stow+swap
                var stowResult = SmartStow(InventoryManager.ActiveHandSlot);
                valid = IsValid.GetValue<KeyResult>();
                // If the smart stow failed, try regular swapping.
                if (!stowResult)
                {
                    ConsoleWindow.PrintAction("Can't store this item");
                    // Retry with default logic if it's still going to try swap.
                    return valid != KeyResult.Swap;
                }

                // The smart stow succeeded, now move the item to the active hand.
                var thing = currentScrollButton.Slot.Get();
                // If this slot was empty, then Invalid was due to slot type differences. Don't attempt to move
                // an empty item.
                if (thing != null)
                {
                    // Move the thing to the active hand.
                    InventoryWindowManager.ActiveHand.PlayerMoveToSlot(thing);
                }

                break;
            case KeyResult.Merge:
                // Slot and hand contain same item type, merge
                // TODO: Try to empty hand by filling all slots with this item in inventory
                return false;
            case KeyResult.Swap:
                // Slot and hand contain different items, with compatible slot types
                // Do not override game logic here, as stow action will cause item to not appear in selected slot.
                // TODO: Possibly detect locked slots of the given item and store item there instead of selected slot.
                return false;
            case KeyResult.HandToSlot:
                // Full hand and empty slot detected, move to that slot handled by default logic.
                return false;
            case KeyResult.SlotToHand:
                // Full slot and empty hand detected, move to hand handled by default logic. 
                return false;
            case KeyResult.None:
            default:
                Plugin.Log.LogWarning($"Unexpected KeyResult value {valid}, skipping.");
                return false;
        }

        return true;
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
                Plugin.Log.LogInfo(
                    $"Merging {stack.Quantity} items into {SlotHelper.GetSlotDisplayName(slot)} which has {targetStack.Quantity} items.");
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
    private static bool DoubleClickMoveToInventory(Slot selectedSlot, SlotData[] targetSlots)
    {
        var thing = selectedSlot.Get();
        Slot targetSlot = null;
        // Find this Thing reference in the original slots dictionary.
        if (OriginalSlots.TryGetValue(thing.ReferenceId, out var originalSlot))
        {
            Plugin.Log.LogInfo(
                $"Returning {thing.DisplayName} to original {SlotHelper.GetSlotDisplayName(originalSlot)}");
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
            foreach (var slot in targetSlots.Where(x => !x.IsOccupied))
            {
                Plugin.Log.LogInfo(
                    $"{SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}");
                if (slot.IsLocked && slot.LockedToPrefabHash != thing.PrefabHash)
                    continue;

                targetSlot = slot.Slot;
                break;
            }
        }

        if (targetSlot == null && targetSlots.All(x => x.IsOccupied || x.IsLocked))
        {
            ConsoleWindow.Print($"Can't place '{thing.DisplayName}' in inventory, all slots are occupied or locked",
                aged: false);
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
            return false;
        }

        // This code is largely unchanged from the base code, except for the ordering of operations.
        // If we didn't find the original slot, find a free slot of this type or a slot from the open inventory.
        targetSlot ??= InventoryManager.ParentHuman.GetFreeSlot(thing.SlotType, ExcludeHandSlots) ??
                       FindFreeSlotOpenWindowsSlotPriority.GetValue<Slot>(thing.SlotType, true);
        if (targetSlot == null)
            // If the slot was not found, find the next available slot in the main slots.
        {
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
        {
            UIAudioManager.Play(UIAudioManager.ActionFailHash);
            return false;
        }

        InventoryManager.Instance.CheckCancelMultiConstructor();
        OnServer.MoveToSlot(selectedSlot.Get(), targetSlot);
        InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(targetSlot);
        UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
        return true;
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
            .Where(x => thing.CanEnter(x.Slot)
                .Result) // Only if this thing can enter this slot (depends on thing prefab)
            .OrderByDescending(x => x.IsVisible) // Sort first by visible windows.
            .ThenByDescending(x =>
                x.IsOccupied && x.OccupantPrefabHash == prefabHash) // Then by occupied slots (for stacking)
            .ThenByDescending(x => x.IsLocked && x.LockedToPrefabHash == prefabHash) // Then by locked slots
            .ThenByDescending(x => x.IsOfSlotType(thing.SlotType)) // Then by slots of this type
            .ThenBy(x => !x.IsOccupied) // Then by non-occupied slots
            .ToArray();
        Plugin.Log.LogInfo("Slots: \r\n" + string.Join("\r\n",
            sortedSlots.Select(slot =>
                $"{SlotHelper.GetSlotDisplayName(slot.Slot)} {slot.IsLocked} {slot.IsOccupied} {slot.IsVisible}")));
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
            $"Hand {SlotHelper.GetSlotDisplayName(targetSlot)} occupant: {targetSlot.Get()?.DisplayName}");
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
            yield return childSlot;
    }

    /// <summary>
    ///     Toggles the lock state of the currently hovered slot.
    ///     If the slot is empty, it unlocks the slot. If occupied, it locks the slot to that item type.
    /// </summary>
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

    /// <summary>
    ///     Determines if a move operation is allowed based on slot lock rules.
    /// </summary>
    /// <param name="thing">The item being moved</param>
    /// <param name="destinationSlot">The destination slot</param>
    /// <returns>True if move is allowed, false if blocked, null to use default behavior</returns>
    public static bool? AllowMove(DynamicThing thing, Slot destinationSlot)
    {
        if (Data.IsSlotLockedFor(destinationSlot, thing))
            return false;

        return null;
    }

    /// <summary>
    ///     Determines if a swap operation between two slots is allowed based on slot lock rules.
    /// </summary>
    /// <param name="sourceSlot">The source slot</param>
    /// <param name="destinationSlot">The destination slot</param>
    /// <returns>True if swap is allowed, false if blocked, null to use default behavior</returns>
    public static bool? AllowSwap(Slot sourceSlot, Slot destinationSlot)
    {
        if (Data.IsSlotLockedFor(sourceSlot, destinationSlot.Get()))
            return false;

        if (Data.IsSlotLockedFor(destinationSlot, sourceSlot.Get()))
            return false;

        return null;
    }

    /// <summary>
    ///     Determines if a swap operation between a slot and a dynamic thing is allowed based on slot lock rules.
    /// </summary>
    /// <param name="sourceSlot">The source slot</param>
    /// <param name="destination">The destination dynamic thing</param>
    /// <returns>True if swap is allowed, false if blocked, null to use default behavior</returns>
    public static bool? AllowSwap(Slot sourceSlot, DynamicThing destination)
    {
        if (Data.IsSlotLockedFor(sourceSlot, destination))
            return false;
        return null;
    }

    /// <summary>
    ///     Validates whether a thing can enter a destination slot based on slot lock rules.
    ///     Handles both direct entry and swap operations by checking if locked slots would be violated.
    /// </summary>
    /// <param name="thing">The thing attempting to enter the destination slot</param>
    /// <param name="destinationSlot">The slot the thing is trying to enter</param>
    /// <returns>
    ///     A <see cref="CanEnterResult" /> indicating failure if slot locks would be violated,
    ///     or <see langword="null" /> if the operation should proceed with default game logic
    /// </returns>
    public static CanEnterResult? BeforeCanEnter(Thing thing, Slot destinationSlot)
    {
        var sourceDynamicThing = thing as DynamicThing;
        var destinationDynamicThing = destinationSlot.Get();
        var sourceSlot = sourceDynamicThing?.ParentSlot;
        // If the thing is a DynamicThing and the destination already has a Thing and the source Thing comes from a slot
        if (sourceDynamicThing != null && destinationDynamicThing != null && sourceSlot != null &&
            // Check if the destination item cannot enter the source slot (where the incoming item came from)
            Data.IsSlotLockedFor(sourceSlot, destinationDynamicThing, out var sourceLock))
        {
            var title = destinationDynamicThing.GetPassiveTooltip(null).Title;
            Plugin.Log.LogInfo(
                $"CanEnter failed: destination item {title} cannot swap back to {SlotHelper.GetSlotDisplayName(sourceSlot)}, it's locked to {sourceLock ?? "{Unknown}"}");
            return CanEnterResult.Fail(CustomGameStrings.SourceLockedSlot.AsString(sourceLock ?? "{Unknown}", title));
        }

        // Check if the incoming item can enter the destination slot
        if (!Data.IsSlotLockedFor(destinationSlot, thing, out var displayNameTarget))
            return null;

        // The slot is blocked for this item, show the destination blocked message.
        Plugin.Log.LogInfo(
            $"CanEnter failed: source item {thing.GetPassiveTooltip(null).Title} cannot move to {SlotHelper.GetSlotDisplayName(destinationSlot)}");
        return CanEnterResult.Fail(CustomGameStrings.DestinationLockedSlot.AsString(displayNameTarget ?? "{Unknown}"));
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
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
using InventoryTweaks.Data;
using InventoryTweaks.Helpers;
using InventoryTweaks.Localization;
using InventoryTweaks.Utilities;
using Reagents;

namespace InventoryTweaks.Core;

public static class CustomInventoryManager
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

    static CustomInventoryManager()
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
        var selectedThing = selectedSlot?.Get();
        if (selectedThing == null)
            return false;
        try
        {
            Plugin.Log.LogDebug($"Handling SmartStow request for {SlotHelper.GetSlotDisplayName(selectedSlot)}");
            var targetSlots = GetTargetSlotsOrdered(selectedThing)
                .ToArray();

            switch (selectedThing)
            {
                // If the item is Slag (e.g. Reagent Mix), only merge to matching reagent mixes.
                case Slag slag:
                {
                    var reagentMergeResult = MergeSlag(slag, targetSlots);
                    if (!reagentMergeResult)
                    {
                        // Reagent mix was merged successfully.
                        return true;
                    }

                    Plugin.Log.LogInfo(
                        $"Still have {slag.Quantity} Reagent Mix remaining, falling through to normal hand logic");
                    break;
                }
                // If the item is stackable, run our stackable code.
                case Stackable stack:
                {
                    var stackableResult = DoubleClickMoveStackable(selectedSlot, stack, targetSlots);
                    if (!stackableResult)
                    {
                        // All stackable items were processed successfully
                        return true;
                    }

                    // If there are remaining stackable items, fall through to normal hand logic
                    Plugin.Log.LogInfo(
                        $"Still have {stack.Quantity} stackable items remaining, falling through to normal hand logic");
                    break;
                }
            }

            // If this is the hand slot, move to inventory only
            if (InventoryManager.LeftHandSlot.Get() == selectedThing ||
                InventoryManager.RightHandSlot.Get() == selectedThing)
            {
                Plugin.Log.LogDebug("Moving hand to inventory");
                return DoubleClickMoveToInventory(selectedSlot, targetSlots
                    .Where(x => selectedThing.CanEnter(x.Slot)
                        .Result) // Only if this thing can enter this slot (depends on thing prefab)
                    .ToArray()
                );
            }

            // If the active hand is empty, move to that hand.
            var activeHand = InventoryManager.ActiveHandSlot;
            var activeHandThing = activeHand?.Get();
            if (activeHand != null && activeHandThing == null)
            {
                Plugin.Log.LogDebug("Moving inventory to active hand");
                OriginalSlots[selectedThing.ReferenceId] = selectedSlot;
                InventoryManager.Instance.CheckCancelMultiConstructor();
                OnServer.MoveToSlot(selectedThing, InventoryManager.ActiveHandSlot);
                InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(InventoryManager.ActiveHandSlot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                return true;
            }

            // If the inactive hand is empty, move to that hand and select it.
            var inactiveHand = InventoryManager.Instance.InactiveHand?.Slot;
            var inactiveHandThing = inactiveHand?.Get();
            if (inactiveHand != null && inactiveHandThing == null)
            {
                Plugin.Log.LogDebug("Moving inventory to active hand");
                OriginalSlots[selectedThing.ReferenceId] = selectedSlot;
                InventoryManager.Instance.CheckCancelMultiConstructor();
                OnServer.MoveToSlot(selectedThing, InventoryManager.Instance.InactiveHand.Slot);
                InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(InventoryManager.Instance.InactiveHand
                    .Slot);
                UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                return true;
            }

            // If both hands are full, stow the active hand and swap the item to it.
            if (activeHand != null && activeHandThing != null &&
                inactiveHand != null && inactiveHandThing != null &&
                // Skip this for stackable items which should not be stow+swapped after stacking
                !(selectedThing is Stackable))
            {
                Plugin.Log.LogDebug("Swap+Stow to active hand");
                var stowResult = StowAndSwap(selectedSlot, activeHand);
                if (stowResult)
                {
                    UIAudioManager.Play(UIAudioManager.ObjectIntoHandHash);
                    return true;
                }
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

    private static bool StowAndSwap(Slot sourceSlot, Slot targetSlot)
    {
        var stowResult = SmartStow(targetSlot);
        if (!stowResult)
        {
            ConsoleWindow.PrintAction("Can't store this item");
            return false;
        }

        // The smart stow succeeded, now move the item to the active hand.
        var sourceThing = sourceSlot.Get();
        // If this slot was empty, then Invalid was due to slot type differences. Don't attempt to move
        // an empty item.
        if (sourceThing != null)
        {
            // Move the thing to the active hand.
            targetSlot.PlayerMoveToSlot(sourceThing);
        }

        return true;
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
                var stowResult = StowAndSwap(currentScrollButton.Slot, InventoryManager.ActiveHandSlot);
                valid = IsValid.GetValue<KeyResult>();
                // If the smart stow failed, try regular swapping.
                if (!stowResult)
                {
                    // Retry with default logic if it's still going to try swap.
                    return valid != KeyResult.Swap;
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
    /// <param name="selectedSlot">The slot containing the stackable item to be moved</param>
    /// <param name="stack">The stackable item being moved</param>
    /// <param name="targetSlots">Available target slots for stacking</param>
    /// <returns>
    ///     <see langword="true" /> if there are remaining items that need further processing,
    ///     or <see langword="false" /> if all items have been successfully processed.
    /// </returns>
    private static bool DoubleClickMoveStackable(Slot selectedSlot,
        Stackable stack,
        IEnumerable<SlotWrapper> targetSlots)
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
        // Try both hands regardless of individual results, as FillHandSlot returns false on success
        FillHandSlot(InventoryManager.LeftHandSlot, selectedSlot, stack);
        if (stack.Quantity <= 0)
            return false; // All items processed successfully, stop processing
        Plugin.Log.LogInfo($"Still have {stack.Quantity} items remaining after filling left hand, trying right hand");
        FillHandSlot(InventoryManager.RightHandSlot, selectedSlot, stack);

        // If we still have items remaining after trying both hands, return true to continue processing
        if (stack.Quantity > 0)
        {
            Plugin.Log.LogInfo(
                $"Still have {stack.Quantity} items remaining after trying both hands, continuing with other logic");
            return true; // Return true to continue processing
        }

        return false; // All items processed successfully, stop processing
    }

    /// <summary>
    ///     Handles a normal item being moved using double click. Looks up where the item was originally
    ///     stored and tries to place it in that slot. Otherwise, continue normal slot processing.
    /// </summary>
    /// <param name="selectedSlot"></param>
    /// <param name="targetSlots"></param>
    private static bool DoubleClickMoveToInventory(Slot selectedSlot, SlotWrapper[] targetSlots)
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

    /// <summary>
    ///     Merge a Reagent Mix to any slot with matching ingredients
    /// </summary>
    /// <param name="slag">The Reagent Mix to be merged</param>
    /// <param name="targetSlots">The available target slots for merging</param>
    /// <returns>
    ///     <see langword="true" /> if there are remaining items that need further processing,
    ///     or <see langword="false" /> if all items have been successfully processed.
    /// </returns>
    private static bool MergeSlag(Slag slag, SlotWrapper[] targetSlots)
    {
        var ingredients = slag.CreatedReagentMixture.ToIngredientList().Select(x => x.ReagentName);
        Plugin.Log.LogInfo($"Attempting to merge Reagent Mix with ingredients {string.Join(", ", ingredients)}");
        // Get a recipe based on the slag mix
        var recipe = new Recipe(slag.CreatedReagentMixture, 30, 30);
        // Get slots that contain slag.
        var targetSlagSlots = (from slot in targetSlots
            let targetSlag = slot.Occupant as Slag
            where targetSlag != null && targetSlag.CreatedReagentMixture.Equals(recipe)
            select new { slot.Slot, Target = targetSlag }).ToArray();
        Plugin.Log.LogDebug($"Found {targetSlagSlots.Length} slots that contain matching Reagent Mix");
        if (targetSlagSlots.Length == 0)
            return true;

        foreach (var targetSlagSlot in targetSlagSlots)
        {
            var targetSlag = targetSlagSlot.Target;
            Plugin.Log.LogDebug($"Merging with slot {SlotHelper.GetSlotDisplayName(targetSlagSlot.Slot)}");
            // Merge the slag into the target slag.
            OnServer.Merge(targetSlag, slag);
            if (slag.Quantity <= 0)
                return false;
        }

        return true;
    }

    private static IEnumerable<SlotWrapper> GetTargetSlotsOrdered(DynamicThing thing)
    {
        var prefabHash = thing.GetPrefabHash();
        var slotLookup = Data.ToLookup();
        var humanInventorySlots = FindSlotsOfHuman(ExcludeHandSlots);
        var allHumanSlots = humanInventorySlots
            .SelectMany(RecurseSlots)
            .Select(slot => BuildSlotWrapper(slot, slotLookup))
            .ToArray();
        var sortedSlots = allHumanSlots
            .Where(x => x.IsOfSlotTypeOrNoneType(thing.SlotType)) // Only allow slots of this type or none type
            .Where(x => x.IsLockedToOrNotLocked(prefabHash)) // Only allow non-locked slots or slots locked to this type
            .Where(x => !SlotHelper.IsSlotExcludedForItem(x.Slot, thing)) // Exclude slots based on configuration
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

    private static SlotWrapper BuildSlotWrapper(Slot slot, Lookup<long, int, ILockedSlot> lookup)
    {
        var lockedSlotTuple = lookup[slot.Parent.ReferenceId, slot.SlotIndex].FirstOrDefault();
        return new SlotWrapper(slot, lockedSlotTuple);
    }

    private static void FillHandSlot(Slot targetSlot, Slot selectedSlot, Stackable stack)
    {
        Plugin.Log.LogDebug(
            $"Hand {SlotHelper.GetSlotDisplayName(targetSlot)} occupant: {targetSlot.Get()?.DisplayName}");
        if (targetSlot.Get() == null ||
            targetSlot.Get() == selectedSlot.Get())
            return;

        var targetStack = targetSlot.Get() as Stackable;
        if (targetStack == null || !targetStack.CanStack(stack))
            return;

        // Merge the items into the target stack.
        Plugin.Log.LogInfo(
            $"Merging {stack.Quantity} items into {SlotHelper.GetSlotDisplayName(targetSlot)} which has {targetStack.Quantity} items.");
        OnServer.Merge(targetStack, stack);
        // The source stack will now contain the remaining quantity or zero.
        Plugin.Log.LogDebug($"Target quantity: {targetStack.Quantity} Source quantity: {stack.Quantity}");

        if (stack.Quantity <= 0)
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
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
            Plugin.Log.LogDebug(
                $"CanEnter failed: destination item {title} cannot swap back to {SlotHelper.GetSlotDisplayName(sourceSlot)}, it's locked to {sourceLock ?? "{Unknown}"}");
            return CanEnterResult.Fail(CustomGameStrings.SourceLockedSlot.AsString(sourceLock ?? "{Unknown}", title));
        }

        // Check if the incoming item can enter the destination slot
        if (!Data.IsSlotLockedFor(destinationSlot, thing, out var displayNameTarget))
            return null;

        // The slot is blocked for this item, show the destination blocked message.
        Plugin.Log.LogDebug(
            $"CanEnter failed: source item {thing.GetPassiveTooltip(null).Title} cannot move to {SlotHelper.GetSlotDisplayName(destinationSlot)}");
        return CanEnterResult.Fail(CustomGameStrings.DestinationLockedSlot.AsString(displayNameTarget ?? "{Unknown}"));
    }
}
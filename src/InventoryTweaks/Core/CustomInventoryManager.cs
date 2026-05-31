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
            Plugin.Log.LogInfo(
                $"Smart Stow: Processing item '{selectedThing.DisplayName}' ({selectedThing.PrefabName})");
            Plugin.Log.LogDebug($"Handling SmartStow request for {SlotHelper.GetSlotDisplayName(selectedSlot)}");
            var targetSlots = GetTargetSlotsOrdered(selectedThing)
                .ToArray();
            Plugin.Log.LogInfo($"Target slots list ({targetSlots.Length} slots):");
            foreach (var slot in targetSlots)
            {
                Plugin.Log.LogInfo(
                    $"  - {SlotHelper.GetSlotDisplayName(slot.Slot)} (Locked: {slot.IsLocked}, Occupied: {slot.IsOccupied}, Visible: {slot.IsVisible})");
            }

            // Whether the post-switch fall-through is allowed to land the source on a slot whose
            // occupant could merge with it. Slag (Reagent Mix) opts in via configuration so users
            // can choose between "co-mingle on placement" and "keep recipes strictly separated".
            var allowMerging = true;

            switch (selectedThing)
            {
                // If the item is Slag (e.g. Reagent Mix), only merge to matching reagent mixes.
                case Slag slag:
                {
                    var slagRemaining = MergeSlag(slag, targetSlots);
                    if (slagRemaining <= 0)
                    {
                        // Reagent mix was merged successfully.
                        return true;
                    }

                    allowMerging = ConfigHelper.SmartStow.AllowReagentMerging;
                    var hasEmptyPlacementTarget = targetSlots.Any(x =>
                        !x.IsOccupied &&
                        selectedThing.CanEnter(x.Slot).Result);
                    if (allowMerging && !hasEmptyPlacementTarget)
                    {
                        Plugin.Log.LogInfo(
                            "No empty slots available for Reagent Mix placement; trying fallback stack merge");
                        slagRemaining = DoubleClickMoveStackable(selectedSlot, slag, targetSlots);
                        if (slagRemaining <= 0)
                            return true;
                    }

                    Plugin.Log.LogInfo(
                        $"Still have {slagRemaining} Reagent Mix remaining, falling through to normal hand logic " +
                        $"(allowMerging={allowMerging})");
                    break;
                }
                // If the item is stackable, run our stackable code.
                case Stackable stack:
                {
                    var stackableRemaining = DoubleClickMoveStackable(selectedSlot, stack, targetSlots);
                    if (stackableRemaining <= 0)
                    {
                        // All stackable items were processed successfully
                        return true;
                    }

                    // If there are remaining stackable items, fall through to normal hand logic
                    Plugin.Log.LogInfo(
                        $"Still have {stackableRemaining} stackable items remaining, falling through to normal hand logic");
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
                        .ToArray(),
                    allowMerging
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
    ///     The expected remaining source quantity after issuing merge requests. <c>0</c> means
    ///     all items have been queued for processing; a positive value means some items remain
    ///     and the caller should fall through to additional placement logic.
    /// </returns>
    /// <remarks>
    ///     <see cref="Thing.Merge" /> dispatches asynchronously to the server on multiplayer
    ///     clients, so <c>stack.Quantity</c> cannot be read mid-loop to decide when the source is
    ///     exhausted. Instead we mirror the server-side transfer math
    ///     (<c>min(remaining, target.MaxQuantity - target.Quantity)</c>) against a snapshot of
    ///     <c>targetSlots</c> taken before any merges are issued.
    /// </remarks>
    private static int DoubleClickMoveStackable(Slot selectedSlot,
        Stackable stack,
        IEnumerable<SlotWrapper> targetSlots)
    {
        var remaining = stack.Quantity;

        if (InventoryManager.LeftHandSlot.Get() == selectedSlot.Get() ||
            InventoryManager.RightHandSlot.Get() == selectedSlot.Get())
        {
            Plugin.Log.LogInfo($"Finding slot in inventory to place {remaining} of {stack.DisplayName}");
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

                var transfer = Math.Min(remaining, targetStack.MaxQuantity - targetStack.Quantity);
                if (transfer <= 0)
                    continue;

                Plugin.Log.LogInfo(
                    $"Merging {transfer} items into {SlotHelper.GetSlotDisplayName(slot)} which has {targetStack.Quantity} items.");
                Thing.Merge(targetStack, stack);
                remaining -= transfer;
                Plugin.Log.LogDebug($"Expected remaining source quantity: {remaining}");

                if (remaining <= 0)
                    break;
            }

            if (remaining <= 0)
            {
                UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                return 0;
            }

            Plugin.Log.LogInfo("Remaining items have no partial stacks to fill..." +
                               " Continuing to main item handler.");
            return remaining;
        }

        remaining = FillHandSlot(InventoryManager.LeftHandSlot, selectedSlot, stack, remaining);
        if (remaining <= 0)
            return 0;
        Plugin.Log.LogInfo($"Still have {remaining} items remaining after filling left hand, trying right hand");
        remaining = FillHandSlot(InventoryManager.RightHandSlot, selectedSlot, stack, remaining);

        if (remaining > 0)
        {
            Plugin.Log.LogInfo(
                $"Still have {remaining} items remaining after trying both hands, continuing with other logic");
            return remaining;
        }

        return 0;
    }

    /// <summary>
    ///     Finds a suitable target slot for an item, trying multiple strategies in order.
    /// </summary>
    /// <param name="thing">The item to find a slot for</param>
    /// <param name="targetSlots">The list of potential target slots</param>
    /// <param name="allowMerging">
    ///     When <see langword="false" />, candidates whose occupant could merge with
    ///     <paramref name="thing" /> are rejected even if a strategy would otherwise pick them
    ///     (defensive against state lag where a wrapper still believes the slot is empty).
    /// </param>
    /// <returns>The selected target slot, or null if no suitable slot was found</returns>
    private static Slot FindTargetSlot(DynamicThing thing, SlotWrapper[] targetSlots, bool allowMerging = true)
    {
        // Strategy 1: Try to return to original slot
        if (OriginalSlots.TryGetValue(thing.ReferenceId, out var originalSlot))
        {
            // Always clear the slot data so that we don't leave the reference around
            OriginalSlots.Remove(thing.ReferenceId);
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
            else if (!IsAllowedTarget(originalSlot, thing, allowMerging))
            {
                Plugin.Log.LogWarning(
                    "Original slot would auto-merge with current occupant; skipping (allowMerging=false)");
            }
            else
            {
                Plugin.Log.LogInfo($"Selected target slot (original) - {SlotHelper.GetSlotDisplayName(originalSlot)}");
                Plugin.Log.LogInfo($"Target slot location - {SlotHelper.GetSlotLocationPath(originalSlot)}");
                return originalSlot;
            }
        }

        // Strategy 2: Find an unoccupied slot from the target slots list
        foreach (var slot in targetSlots.Where(x => !x.IsOccupied))
        {
            if (slot.IsLocked && slot.LockedToPrefabHash != thing.PrefabHash)
                continue;
            if (!IsAllowedTarget(slot.Slot, thing, allowMerging))
                continue;

            Plugin.Log.LogInfo($"Selected target slot - {SlotHelper.GetSlotDisplayName(slot.Slot)}");
            Plugin.Log.LogInfo($"Target slot location - {SlotHelper.GetSlotLocationPath(slot.Slot)}");
            return slot.Slot;
        }

        // Strategy 3: Find a free slot from the human or open inventory windows
        var freeSlot = InventoryManager.ParentHuman.GetFreeSlot(thing.SlotType, ExcludeHandSlots) ??
                       FindFreeSlotOpenWindowsSlotPriority.GetValue<Slot>(thing.SlotType, true);
        if (freeSlot != null && IsAllowedTarget(freeSlot, thing, allowMerging))
        {
            Plugin.Log.LogInfo($"Selected target slot (free slot) - {SlotHelper.GetSlotDisplayName(freeSlot)}");
            Plugin.Log.LogInfo($"Target slot location - {SlotHelper.GetSlotLocationPath(freeSlot)}");
            return freeSlot;
        }

        // Strategy 4: Find a nested slot in items within the human's inventory
        foreach (var slot in InventoryManager.ParentHuman.Slots)
        {
            if (slot == null || ExcludeHandSlots.Contains(slot.Action) || slot.Get() == null)
                continue;

            var nestedFreeSlot = slot.Get().GetFreeSlot(thing.SlotType);
            if (nestedFreeSlot == null)
                continue;
            if (!IsAllowedTarget(nestedFreeSlot, thing, allowMerging))
                continue;

            Plugin.Log.LogInfo($"Selected target slot (nested) - {SlotHelper.GetSlotDisplayName(nestedFreeSlot)}");
            Plugin.Log.LogInfo($"Target slot location - {SlotHelper.GetSlotLocationPath(nestedFreeSlot)}");

            // Only animate if the nested slot is not visible (hidden inside a closed inventory window)
            // This provides visual feedback on the parent slot when moving to a hidden nested slot
            if (nestedFreeSlot.Display?.SlotWindow?.IsVisible != true)
            {
                InventoryManager.Instance.StartCoroutine(
                    PerformHiddenSlotMoveToAnimation.GetValue<IEnumerator>(slot, thing.ParentSlot,
                        thing));
            }

            return nestedFreeSlot;
        }

        return null;
    }

    /// <summary>
    ///     Returns <see langword="false" /> when <paramref name="allowMerging" /> is disabled and
    ///     the slot's current (live) occupant is a <see cref="Stackable" /> that the game would
    ///     auto-merge with <paramref name="thing" />. All other cases return <see langword="true" />.
    /// </summary>
    /// <remarks>
    ///     Strategy 2 already filters by <see cref="SlotWrapper.IsOccupied" /> at snapshot time, but
    ///     on multiplayer clients the live occupant can differ. This guard re-checks at decision
    ///     time so we never queue a placement that would silently merge into a different stack
    ///     (e.g. mismatched Reagent Mixes).
    /// </remarks>
    private static bool IsAllowedTarget(Slot slot, DynamicThing thing, bool allowMerging)
    {
        if (allowMerging)
            return true;
        if (!(slot.Get() is Stackable existing))
            return true;
        if (!(thing is Stackable incoming))
            return true;
        return !existing.CanStack(incoming) || existing.IsStackFull;
    }

    /// <summary>
    ///     Handles a normal item being moved using double click. Looks up where the item was originally
    ///     stored and tries to place it in that slot. Otherwise, continue normal slot processing.
    /// </summary>
    /// <param name="selectedSlot">The slot containing the item being moved.</param>
    /// <param name="targetSlots">The list of potential destination slots.</param>
    /// <param name="allowMerging">
    ///     When <see langword="false" />, candidate slots whose occupant could merge with the moved
    ///     item are rejected (forwarded to <see cref="FindTargetSlot" />). Set this to
    ///     <see langword="false" /> for items where same-prefab merges are unsafe (e.g. Reagent Mix
    ///     Slag, where two stacks may share a prefab but carry different reagent recipes).
    /// </param>
    private static bool DoubleClickMoveToInventory(Slot selectedSlot, SlotWrapper[] targetSlots,
        bool allowMerging = true)
    {
        var thing = selectedSlot.Get();
        var targetSlot = FindTargetSlot(thing, targetSlots, allowMerging);

        switch (targetSlot)
        {
            case null when targetSlots.All(x => x.IsOccupied || x.IsLocked):
                Plugin.Log.LogWarning(
                    $"Cannot place '{thing.DisplayName}' ({thing.PrefabName}) - all target slots are occupied or locked");
                ConsoleWindow.Print($"Can't place '{thing.DisplayName}' in inventory, all slots are occupied or locked",
                    aged: false);
                break;
            case null:
                Plugin.Log.LogWarning(
                    $"Cannot place '{thing.DisplayName}' ({thing.PrefabName}) - no suitable target slot found after searching all available options");
                break;
            default:
                InventoryManager.Instance.CheckCancelMultiConstructor();
                OnServer.MoveToSlot(selectedSlot.Get(), targetSlot);
                InventoryWindowManager.Instance.TryUpdateSelectedInventorySlot(targetSlot);
                UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
                return true;
        }

        UIAudioManager.Play(UIAudioManager.ActionFailHash);
        return false;
    }

    /// <summary>
    ///     Merge a Reagent Mix to any slot with matching ingredients
    /// </summary>
    /// <param name="slag">The Reagent Mix to be merged</param>
    /// <param name="targetSlots">The available target slots for merging</param>
    /// <returns>
    ///     The expected remaining source quantity after issuing merge requests. <c>0</c> means
    ///     all items have been queued for processing; a positive value means some items remain
    ///     and the caller should fall through to additional placement logic.
    /// </returns>
    /// <remarks>
    ///     See <see cref="DoubleClickMoveStackable" /> for why we track <c>remaining</c> locally
    ///     instead of reading <c>slag.Quantity</c> after each <see cref="Thing.Merge" /> call.
    ///     <para>
    ///         On multiplayer clients (<c>!GameManager.RunSimulation</c>), <c>Slag.CreatedReagentMixture</c>
    ///         is not synchronised from the server, so every slag observable to the client appears
    ///         empty and <see cref="ReagentMixture.Equals(Recipe)" /> would falsely match different
    ///         recipes against each other. We refuse to issue any merge messages in that case and
    ///         let the caller fall through to placement (with <c>allowMerging=false</c>).
    ///     </para>
    /// </remarks>
    private static int MergeSlag(Slag slag, SlotWrapper[] targetSlots)
    {
        Plugin.Log.LogInfo(
            $"Source Reagent Mix [{FormatReagentMixture(slag.CreatedReagentMixture)}] (Qty: {slag.Quantity}/{slag.MaxQuantity})");

        if (!GameManager.RunSimulation)
        {
            Plugin.Log.LogWarning(
                "Skipping Reagent Mix merge on multiplayer client: Slag.CreatedReagentMixture is " +
                "not synchronised from the server, so we cannot reliably distinguish recipes. " +
                "Source will fall through to placement only.");
            return slag.Quantity;
        }

        var recipe = new Recipe(slag.CreatedReagentMixture, 30, 30);

        // Materialize all Slag-occupied target candidates so we can log them up front (matching the
        // "Target slots list" pattern in SmartStow) before applying the recipe-equality filter.
        var slagCandidates = targetSlots
            .Select(x => new { Wrapper = x, Slag = x.Occupant as Slag })
            .Where(x => x.Slag != null)
            .ToArray();
        Plugin.Log.LogInfo($"Reagent Mix candidate slots ({slagCandidates.Length} slots):");
        foreach (var candidate in slagCandidates)
        {
            var matches = candidate.Slag.CreatedReagentMixture.Equals(recipe);
            Plugin.Log.LogInfo(
                $"  - {SlotHelper.GetSlotDisplayName(candidate.Wrapper.Slot)} " +
                $"[{FormatReagentMixture(candidate.Slag.CreatedReagentMixture)}] " +
                $"(Match: {matches}, Qty: {candidate.Slag.Quantity}/{candidate.Slag.MaxQuantity})");
        }

        var targetSlagSlots = slagCandidates
            .Where(x => x.Slag.CreatedReagentMixture.Equals(recipe))
            .Select(x => new { x.Wrapper.Slot, Target = x.Slag })
            .ToArray();
        Plugin.Log.LogDebug($"Found {targetSlagSlots.Length} slots that contain matching Reagent Mix");
        if (targetSlagSlots.Length == 0)
            return slag.Quantity;

        var remaining = slag.Quantity;
        foreach (var targetSlagSlot in targetSlagSlots)
        {
            var targetSlag = targetSlagSlot.Target;

            var transfer = Math.Min(remaining, targetSlag.MaxQuantity - targetSlag.Quantity);
            if (transfer <= 0)
                continue;

            Plugin.Log.LogDebug(
                $"Merging {transfer} into slot {SlotHelper.GetSlotDisplayName(targetSlagSlot.Slot)}");
            Thing.Merge(targetSlag, slag);
            remaining -= transfer;
            if (remaining <= 0)
                return 0;
        }

        return remaining;
    }

    /// <summary>
    ///     Formats the non-zero reagents of a <see cref="ReagentMixture" /> for logging.
    /// </summary>
    /// <param name="mixture">The mixture to format. May be <see langword="null" />.</param>
    /// <returns>
    ///     A comma-separated <c>Name=Quantity</c> list, <c>(empty)</c> if no reagents are present,
    ///     or <c>(null)</c> if the mixture itself is missing.
    /// </returns>
    private static string FormatReagentMixture(ReagentMixture mixture)
    {
        if (mixture == null)
            return "(null)";
        var ingredients = mixture.ToIngredientList();
        if (ingredients.Count == 0)
            return "(empty)";
        return string.Join(", ", ingredients.Select(x => $"{TrimReagentName(x.ReagentName)}={x.Quantity:0.######}"));
    }

    /// <summary>
    ///     Strips the <c>Reagents.</c> namespace prefix that <c>ReagentMixIngredientSaveData.ReagentName</c>
    ///     emits, so log lines read <c>Silver=0.5</c> instead of <c>Reagents.Silver=0.5</c>.
    /// </summary>
    private static string TrimReagentName(string reagentName)
    {
        if (string.IsNullOrEmpty(reagentName))
            return reagentName;
        const string prefix = "Reagents.";
        return reagentName.StartsWith(prefix) ? reagentName.Substring(prefix.Length) : reagentName;
    }

    /// <summary>
    ///     Collects and orders candidate inventory slots for Smart Stow.
    ///     Sort priority is configured via <see cref="ConfigHelper.SmartStow.OrderedCriteria" />.
    /// </summary>
    private static IEnumerable<SlotWrapper> GetTargetSlotsOrdered(DynamicThing thing)
    {
        var prefabHash = thing.GetPrefabHash();
        var slotLookup = Data.ToLookup();
        var humanInventorySlots = FindSlotsOfHuman(ExcludeHandSlots);
        var allHumanSlots = humanInventorySlots
            .SelectMany(RecurseSlots)
            .Select(slot => BuildSlotWrapper(slot, slotLookup))
            .ToArray();
        var filtered = allHumanSlots
            .Where(x => x.IsOfSlotTypeOrNoneType(thing.SlotType)) // Only allow slots of this type or none type
            .Where(x => x.IsLockedToOrNotLocked(prefabHash)) // Only allow non-locked slots or slots locked to this type
            .Where(x => !SlotHelper.IsSlotExcludedForItem(x.Slot, thing)) // Exclude slots based on configuration
            .Where(x => !ConfigHelper.SmartStow.OnlyVisibleWindows ||
                        !x.IsInHiddenWindow); // Optionally exclude slots in hidden inventory windows

        var sorted =
            ConfigHelper.SmartStow.OrderedCriteria.Aggregate<SmartStowSortCriterion, IOrderedEnumerable<SlotWrapper>>(
                null, (current, criterion) => current == null
                    ? filtered.OrderByDescending(x => x.IsHigherPriorityFor(criterion, prefabHash, thing.SlotType))
                    : current.ThenByDescending(x => x.IsHigherPriorityFor(criterion, prefabHash, thing.SlotType)));

        return sorted ?? filtered;
    }

    private static SlotWrapper BuildSlotWrapper(Slot slot, Lookup<long, int, ILockedSlot> lookup)
    {
        var lockedSlotTuple = lookup[slot.Parent.ReferenceId, slot.SlotIndex].FirstOrDefault();
        return new SlotWrapper(slot, lockedSlotTuple);
    }

    /// <summary>
    ///     Attempts to merge <paramref name="stack" /> into the stackable currently occupying
    ///     <paramref name="targetSlot" />.
    /// </summary>
    /// <param name="targetSlot">The destination hand slot.</param>
    /// <param name="selectedSlot">The slot containing the source stack (used to skip self-merges).</param>
    /// <param name="stack">The source stackable being merged.</param>
    /// <param name="remainingQuantity">Expected remaining source quantity prior to this call.</param>
    /// <returns>The expected remaining source quantity after this call.</returns>
    private static int FillHandSlot(Slot targetSlot, Slot selectedSlot, Stackable stack, int remainingQuantity)
    {
        Plugin.Log.LogDebug(
            $"Hand {SlotHelper.GetSlotDisplayName(targetSlot)} occupant: {targetSlot.Get()?.DisplayName}");
        if (targetSlot.Get() == null ||
            targetSlot.Get() == selectedSlot.Get())
            return remainingQuantity;

        var targetStack = targetSlot.Get() as Stackable;
        if (targetStack == null || !targetStack.CanStack(stack))
            return remainingQuantity;

        var transfer = Math.Min(remainingQuantity, targetStack.MaxQuantity - targetStack.Quantity);
        if (transfer <= 0)
            return remainingQuantity;

        Plugin.Log.LogInfo(
            $"Merging {transfer} items into {SlotHelper.GetSlotDisplayName(targetSlot)} which has {targetStack.Quantity} items.");
        Thing.Merge(targetStack, stack);
        var newRemaining = remainingQuantity - transfer;
        Plugin.Log.LogDebug($"Expected remaining source quantity: {newRemaining}");

        if (newRemaining <= 0)
            UIAudioManager.Play(UIAudioManager.AddToInventoryHash);
        return newRemaining;
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
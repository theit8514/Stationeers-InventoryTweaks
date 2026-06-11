using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.UI;

namespace InventoryTweaks.Data;

/// <summary>
///     Represents data about a slot including its state, occupant, and lock information.
/// </summary>
public class SlotWrapper
{
    private readonly ILockedSlot _lockedSlot;

    public SlotWrapper(Slot slot, ILockedSlot lockedSlotTuple)
    {
        Slot = slot;
        _lockedSlot = lockedSlotTuple;
    }

    public Slot Slot { get; }
    private InventoryWindow Window => Slot.Display?.ParentWindow;

    /// <summary>
    ///     True when this slot belongs directly to the player's human (its body or hand slots).
    ///     These slots live on the HUD and never receive a <see cref="InventoryWindow" />, unlike
    ///     nested container slots whose display button is assigned a parent window.
    /// </summary>
    public bool IsRootHumanSlot => Slot.Parent == InventoryManager.ParentHuman;

    /// <summary>
    ///     True when this slot is currently visible to the player. Root human (body/hand) slots
    ///     live directly on the HUD and are always on-screen, so they are always visible. Nested
    ///     slots are visible only when their owning inventory window exists and is shown; a slot
    ///     whose window has not been created yet (or has been created and closed) is not visible.
    /// </summary>
    public bool IsVisible => IsRootHumanSlot || (Window?.IsVisible ?? false);

    /// <summary>
    ///     True when this slot belongs to an inventory window that exists but is not currently visible.
    ///     Root human (body/hand) slots have no window and are never considered hidden.
    /// </summary>
    public bool IsInHiddenWindow => !IsRootHumanSlot && Window != null && !Window.IsVisible;

    public DynamicThing Occupant => Slot.Get();
    public bool IsOccupied => Occupant != null;
    private int OccupantPrefabHash => Occupant.PrefabHash;
    public Stackable Stackable => Occupant as Stackable;
    public bool IsStackable => Stackable != null;
    public bool IsLocked => _lockedSlot != null;
    public int LockedToPrefabHash => _lockedSlot.PrefabHash;

    public bool IsOfSlotTypeOrNoneType(Slot.Class occupantType)
    {
        return IsOfSlotType(occupantType) ||
               Slot.Type == Slot.Class.None;
    }

    private bool IsOfSlotType(Slot.Class occupantType)
    {
        return Slot.Type == occupantType;
    }

    public bool IsLockedToOrNotLocked(int prefabHash)
    {
        return !IsLocked || LockedToPrefabHash == prefabHash;
    }

    /// <summary>
    ///     Returns whether this slot should be ordered ahead of non-matching slots for the given Smart Stow criterion.
    /// </summary>
    /// <param name="criterion">The sort criterion to evaluate.</param>
    /// <param name="prefabHash">Prefab hash of the item being stowed.</param>
    /// <param name="slotType">Slot type of the item being stowed.</param>
    /// <returns>
    ///     <see langword="true" /> if the slot satisfies the criterion and should sort first; otherwise
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     Designed to be consumed by <c>OrderByDescending</c> / <c>ThenByDescending</c>, so a result of
    ///     <see langword="true" /> ranks the slot higher than non-matching slots for that criterion.
    /// </remarks>
    public bool IsHigherPriorityFor(SmartStowSortCriterion criterion, int prefabHash, Slot.Class slotType)
    {
        return criterion switch
        {
            SmartStowSortCriterion.ExistingStack => IsOccupied
                                                    && OccupantPrefabHash == prefabHash
                                                    && IsStackable
                                                    && !Stackable.IsStackFull,
            SmartStowSortCriterion.LockedSlot => !IsOccupied
                                                 && IsLocked
                                                 && LockedToPrefabHash == prefabHash,
            SmartStowSortCriterion.TypedSlot => !IsOccupied && IsOfSlotType(slotType),
            SmartStowSortCriterion.EmptyRegularSlot => !IsOccupied && !IsLocked,
            SmartStowSortCriterion.BodySlot => !IsOccupied && IsRootHumanSlot,
            SmartStowSortCriterion.VisibleWindow => IsVisible,
            _ => false
        };
    }
}
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
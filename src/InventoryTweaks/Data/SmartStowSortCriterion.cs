namespace InventoryTweaks.Data;

/// <summary>
///     Sort conditions used when ordering candidate slots for Smart Stow.
///     Priority order is configured via <see cref="Helpers.ConfigHelper.SmartStow" />.
/// </summary>
public enum SmartStowSortCriterion
{
    /// <summary>
    ///     Occupied slot containing a stackable item of the same prefab with room to merge.
    /// </summary>
    ExistingStack,

    /// <summary>
    ///     Empty slot locked to the item being stowed.
    /// </summary>
    LockedSlot,

    /// <summary>
    ///     Empty slot whose type matches the item slot type (e.g. tool slot for tools).
    /// </summary>
    TypedSlot,

    /// <summary>
    ///     Empty slot that is neither locked nor typed for the item.
    /// </summary>
    EmptyRegularSlot,

    /// <summary>
    ///     Slot belongs to a visible inventory window.
    /// </summary>
    VisibleWindow
}
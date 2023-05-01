namespace InventoryTweaks;

public interface ILockedSlot
{
    public long ContainerId { get; }
    public int SlotId { get; }
    public int PrefabHash { get; }
    public string PrefabName { get; }
    public string DisplayName { get; }
}
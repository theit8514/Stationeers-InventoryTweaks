using UnityEngine;

namespace InventoryTweaks.Data;

/// <summary>
///     Represents a locked slot with its container and item information.
/// </summary>
public sealed class LockedSlot : ILockedSlot
{
    public LockedSlot(long containerId, int slotIndex, string prefabName, string displayName)
    {
        ContainerId = containerId;
        PrefabName = prefabName;
        PrefabHash = Animator.StringToHash(prefabName);
        SlotIndex = slotIndex;
        DisplayName = displayName;
    }

    /// <inheritdoc />
    public long ContainerId { get; }

    /// <inheritdoc />
    public int SlotIndex { get; }

    /// <inheritdoc />
    public int PrefabHash { get; }

    /// <inheritdoc />
    public string PrefabName { get; }

    /// <inheritdoc />
    public string DisplayName { get; }
}
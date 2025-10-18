using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects;
using InventoryTweaks.Data;
using InventoryTweaks.Data.Serialized;
using InventoryTweaks.Utilities;

namespace InventoryTweaks.Core;

public class InventoryTweaksData
{
    private readonly Dictionary<(long, int), ILockedSlot> _lockedSlots = new();

    public void Clear()
    {
        _lockedSlots.Clear();
    }

    public void Load(InventoryTweaksSaveData saveData)
    {
        Clear();
        foreach (var container in saveData.LockedSlotsData)
        {
            foreach (var slot in container.LockedSlots)
            {
                _lockedSlots.Add((container.ContainerId, slot.SlotIndex),
                    new LockedSlot(container.ContainerId, slot.SlotIndex, slot.PrefabName,
                        Prefab.Find(slot.PrefabName)?.DisplayName));
            }
        }
    }

    public InventoryTweaksSaveData Save()
    {
        var saveData = new InventoryTweaksSaveData();
        foreach (var kvp in _lockedSlots.GroupBy(x => x.Key.Item1))
        {
            saveData.LockedSlotsData.Add(new LockedContainerData
            {
                ContainerId = kvp.Key,
                LockedSlots = kvp.Select(x => new LockedSlotData
                {
                    SlotIndex = x.Key.Item2,
                    PrefabName = x.Value.PrefabName
                }).ToList()
            });
        }

        return saveData;
    }

    public void UnlockSlot(long containerId, int slotIndex)
    {
        _lockedSlots.Remove((containerId, slotIndex));
    }

    public void LockSlot(long containerId, int slotIndex, Thing thing)
    {
        _lockedSlots[(containerId, slotIndex)] =
            new LockedSlot(containerId, slotIndex, thing.GetPrefabName(), thing.DisplayName);
    }

    /// <summary>
    ///     Checks if a slot is locked for a specific thing type.
    /// </summary>
    /// <param name="slot">The slot to check</param>
    /// <param name="thing">The thing to check against the slot restriction</param>
    /// <returns>
    ///     <see langword="true" /> if the slot is locked for this thing type,
    ///     <see langword="false" /> if the slot allows this thing type
    /// </returns>
    public bool IsSlotLockedFor(Slot slot, Thing thing)
    {
        return IsSlotLockedFor(slot, thing, out _);
    }

    /// <summary>
    ///     Checks if a slot is locked for a specific thing type and provides the restriction display name.
    /// </summary>
    /// <param name="slot">The slot to check</param>
    /// <param name="thing">The thing to check against the slot restriction</param>
    /// <param name="displayName">The display name of the item type the slot is restricted to, if locked</param>
    /// <returns>
    ///     <see langword="true" /> if the slot is locked for this thing type,
    ///     <see langword="false" /> if the slot allows this thing type
    /// </returns>
    public bool IsSlotLockedFor(Slot slot, Thing thing, out string displayName)
    {
        displayName = null;

        // If slot has no parent, it's blocked
        if (slot.Parent == null)
            return true;

        if (!_lockedSlots.TryGetValue((slot.Parent.ReferenceId, slot.SlotIndex),
                out var lockedSlot))
            return false; // No lock means slot allows this thing

        displayName = lockedSlot.DisplayName;
        return thing.GetPrefabHash() != lockedSlot.PrefabHash;
    }

    /// <summary>
    ///     Checks if a thing can be placed in a slot.
    /// </summary>
    /// <param name="thing">The thing to place</param>
    /// <param name="slot">The slot to check</param>
    /// <returns>
    ///     <see langword="true" /> if the thing can be placed in the slot,
    ///     <see langword="false" /> if the slot is blocked for this thing
    /// </returns>
    public bool CanPlaceInSlot(DynamicThing thing, Slot slot)
    {
        // If slot is occupied or has no parent, it's blocked
        if (slot.Get() != null || slot.Parent == null)
            return false;

        // Check if slot is locked to a different item type
        return !IsSlotLockedFor(slot, thing);
    }

    public Lookup<long, int, ILockedSlot> ToLookup()
    {
        return new Lookup<long, int, ILockedSlot>(_lockedSlots.Values,
            data => (data.ContainerId, data.SlotIndex));
    }
}
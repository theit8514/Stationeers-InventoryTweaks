using System.Collections.Generic;
using System.Linq;
using Assets.Scripts.Objects;
using InventoryTweaks.Data;
using UnityEngine;

namespace InventoryTweaks;

public class InventoryTweaksData
{
    private readonly Dictionary<Tuple<long, int>, ILockedSlot> _lockedSlots = new();

    public void Load(InventoryTweaksSaveData saveData)
    {
        _lockedSlots.Clear();
        foreach (var container in saveData.LockedSlotsData)
        {
            foreach (var slot in container.LockedSlots)
            {
                _lockedSlots.Add(new Tuple<long, int>(container.ContainerId, slot.SlotIndex),
                    new LockedSlotTuple(container.ContainerId, slot.SlotIndex, slot.PrefabName,
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
        _lockedSlots.Remove(new Tuple<long, int>(containerId, slotIndex));
    }

    public void LockSlot(long containerId, int slotIndex, Thing thing)
    {
        _lockedSlots[new Tuple<long, int>(containerId, slotIndex)] =
            new LockedSlotTuple(containerId, slotIndex, thing.GetPrefabName(), thing.DisplayName);
    }

    public bool CanPlaceInSlot(DynamicThing thing, Slot slot)
    {
        if (slot.Get() != null || slot.Parent == null)
            return false;
        if (!_lockedSlots.TryGetValue(new Tuple<long, int>(slot.Parent.ReferenceId, slot.SlotIndex),
                out var lockedSlot))
            return true;
        return lockedSlot.PrefabHash == thing.GetPrefabHash();
    }

    public Lookup<long, int, ILockedSlot> ToLookup()
    {
        return new Lookup<long, int, ILockedSlot>(_lockedSlots.Values,
            data => Tuple<long, int>.Create(data.ContainerId, data.SlotIndex));
    }

    public bool TryGetLock(long containerId, int slotIndex, out ILockedSlot lockedSlot)
    {
        return _lockedSlots.TryGetValue(new Tuple<long, int>(containerId, slotIndex), out lockedSlot);
    }

    private sealed class LockedSlotTuple : ILockedSlot
    {
        public LockedSlotTuple(long containerId, int slotIndex, string prefabName, string displayName)
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
}
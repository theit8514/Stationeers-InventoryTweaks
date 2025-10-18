using System.Collections.Generic;
using InventoryTweaks.Core;
using UnityEngine;

namespace InventoryTweaks.KeyBinding.Handlers;

public class LockSlotKeyPressHandler : KeyPressHandler
{
    /// <inheritdoc />
    public override string Name => "LockSlot";

    /// <inheritdoc />
    public override KeyCode DefaultKey => KeyCode.Mouse2;

    /// <inheritdoc />
    public override IEnumerable<string> GetIgnoredConflicts()
    {
        return new[]
        {
            Constants.KeyConflicts.PingHighlight,
            Constants.KeyConflicts.ZoopAddWaypoint
        };
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetKeyConflicts()
    {
        // LockSlot conflicts with PingHighlight
        return new[]
        {
            Constants.KeyConflicts.PingHighlight
        };
    }

    protected override void Execute()
    {
        CustomInventoryManager.LockSlotAction();
    }
}
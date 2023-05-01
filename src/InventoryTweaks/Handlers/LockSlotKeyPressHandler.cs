using UnityEngine;

namespace InventoryTweaks.Handlers;

public class LockSlotKeyPressHandler : KeyPressHandler
{
    /// <inheritdoc />
    public override string Name => "LockSlot";

    /// <inheritdoc />
    public override KeyCode DefaultKey => KeyCode.Mouse2;

    protected override void Execute()
    {
        NewInventoryManager.LockSlotAction();
    }
}
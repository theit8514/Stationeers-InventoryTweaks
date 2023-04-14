using Assets.Scripts;
using Assets.Scripts.Inventory;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Entities;
using InventoryTweaks.Helpers;
using System.Linq;
using UnityEngine;

namespace InventoryTweaks.Handlers;

public class HeldItemNextKeyPressHandler : KeyPressHandler
{
    private static readonly int NextHashKey = Animator.StringToHash("Next");
    private static readonly int PrevHashKey = Animator.StringToHash("Prev");

    /// <inheritdoc />
    public override string Name => "HeldItemNext";

    /// <inheritdoc />
    public override KeyCode DefaultKey => KeyCode.Z;

    protected override void Execute()
    {
        if (!Human.LocalHuman)
            return;

        var item = InventoryManager.Instance.ActiveHand?.Slot?.Occupant;
        if (item == null)
            return;

        var buttonName = !KeyManager.GetButton(KeyCode.LeftShift)
            ? GetLocalizedNextDisplayName()
            : GetLocalizedPrevDisplayName();
        var button = item.Interactables.FirstOrDefault(x => x.DisplayName == buttonName);
        if (button == null)
        {
            Plugin.Log.LogDebug($"No interactable found on {item.DisplayName} with DisplayName '{buttonName}'");
            return;
        }

        var interaction = new Interaction(InventoryManager.Parent, InventoryManager.ActiveHandSlot,
            CursorManager.CursorThing, KeyManager.GetButton(KeyMap.QuantityModifier));
        item.InteractWith(button, interaction);
    }

    private static string GetLocalizedNextDisplayName()
    {
        return LocalizationHelper.Interactable.GetLocalizedDisplayName(NextHashKey, "Next");
    }

    private static string GetLocalizedPrevDisplayName()
    {
        return LocalizationHelper.Interactable.GetLocalizedDisplayName(PrevHashKey, "Prev");
    }
}
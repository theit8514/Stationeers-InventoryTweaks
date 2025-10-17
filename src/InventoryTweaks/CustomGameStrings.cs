using Assets.Scripts.Localization2;

namespace InventoryTweaks;

public static class CustomGameStrings
{
    public static GameString SourceLockedSlot = GameString.Create(nameof(SourceLockedSlot), "Source slot locked to <color=green>{LOCAL:Name1}</color>, not <color=yellow>{LOCAL:Name2}</color>", "Name1", "Name2");
    public static GameString DestinationLockedSlot = GameString.Create(nameof (DestinationLockedSlot), "Destination slot locked to <color=green>{LOCAL:Name}</color>", "Name");
}
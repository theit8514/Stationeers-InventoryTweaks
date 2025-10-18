using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using InventoryTweaks.Helpers;
using InventoryTweaks.KeyBinding;
using InventoryTweaks.KeyBinding.Handlers;
using InventoryTweaks.Patches;
using JetBrains.Annotations;

namespace InventoryTweaks;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInProcess("rocketstation.exe")]
[PublicAPI]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log { get; private set; }

#pragma warning disable IDE0051
    private void Awake()
#pragma warning restore IDE0051
    {
        Log = Logger;
        if (Harmony.HasAnyPatches(Constants.HarmonyIds.InventoryTweaksPatches))
        {
            Log.LogWarning($"Plugin {PluginInfo.PLUGIN_GUID} is already loaded!");
            return;
        }

        ConfigHelper.LoadConfig(Config);
        Log.LogMessage($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        ModKeyManager.RegisterKeyPressHandlers += (_, arguments) =>
        {
            var controlsGroup = arguments.AddControlsGroup(Constants.ControlsGroupName);
            arguments.AddKey(new HeldItemNextKeyPressHandler(), controlsGroup);
            arguments.AddKey(new DebugWindowsKeyPressHandler(), controlsGroup);
            arguments.AddKey(new LockSlotKeyPressHandler(), controlsGroup);
        };

        var instance = new Harmony(Constants.HarmonyIds.InventoryTweaksPatches);
        instance.PatchAll(typeof(ModKeyManager));

        instance.PatchAll(typeof(KeyBindHelpers));
        instance.PatchAll(typeof(InventoryManagerPatches));
        instance.PatchAll(typeof(InventoryWindowManagerPatches));
        if (ConfigHelper.General.EnableRewriteOpenSlots)
            instance.PatchAll(typeof(RewriteOpenSlotsInSavePatches));
        if (ConfigHelper.General.EnableSaveLockedSlots)
            instance.PatchAll(typeof(SaveLockedSlotsPatches));
    }
}
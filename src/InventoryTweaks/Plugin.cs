using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using InventoryTweaks.Handlers;
using InventoryTweaks.Helpers;
using InventoryTweaks.Patches;
using JetBrains.Annotations;

namespace InventoryTweaks;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInProcess("rocketstation.exe")]
[PublicAPI]
public class Plugin : BaseUnityPlugin
{
    private const string HarmonyID = "InventoryTweaksPatches";
    
    internal static ManualLogSource Log { get; private set; }

#pragma warning disable IDE0051
    private void Awake()
#pragma warning restore IDE0051
    {
        Log = Logger;
        if (Harmony.HasAnyPatches(HarmonyID))
        {
            Log.LogWarning($"Plugin {PluginInfo.PLUGIN_GUID} is already loaded!");
            return;
        }

        ConfigHelper.LoadConfig(Config);
        Log.LogMessage($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        ModKeyManager.RegisterKeyPressHandlers += (_, arguments) =>
        {
            var controlsGroup = arguments.AddControlsGroup("InventoryTweaks");
            arguments.AddKey(new HeldItemNextKeyPressHandler(), controlsGroup);
            arguments.AddKey(new DebugWindowsKeyPressHandler(), controlsGroup);
            arguments.AddKey(new LockSlotKeyPressHandler(), controlsGroup);
        };

        var instance = new Harmony(HarmonyID);
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
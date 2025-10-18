using System.Linq;
using InventoryTweaks.KeyBinding.Handlers;

namespace InventoryTweaks.KeyBinding;

/// <summary>
///     Arguments for registering key press handlers with the mod's key management system.
/// </summary>
public class RegisterKeyPressHandlersArguments
{
    public void AddKey(KeyPressHandler handler, ControlsGroup controlsGroup)
    {
        handler.AddKey(controlsGroup);
        ModKeyManager.Keys.Add(handler);
    }

    public ControlsGroup AddControlsGroup(string name)
    {
        var controlsGroup = ControlsGroup.AllControlGroups.FirstOrDefault(x => x.Name == name);
        if (controlsGroup != null)
            return controlsGroup;

        Plugin.Log.LogInfo($"Adding new controls group {name}");
        return new ControlsGroup(name);
    }
}
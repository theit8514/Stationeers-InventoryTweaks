using System.Collections.Generic;
using InputSystem;
using InventoryTweaks.Helpers;
using UnityEngine;

namespace InventoryTweaks.KeyBinding.Handlers;

public abstract class KeyPressHandler
{
    protected readonly KeyInputState InputState;
    protected readonly InputPhase Phase;
    public readonly KeyWrap Wrapper = new();

    protected KeyPressHandler()
    {
        Phase = InputPhase.Down;
        InputState = KeyInputState.Game;
    }

    protected KeyPressHandler(InputPhase phase, KeyInputState inputState)
    {
        Phase = phase;
        InputState = inputState;
    }

    public abstract string Name { get; }
    public abstract KeyCode DefaultKey { get; }

    /// <summary>
    ///     Gets the list of key names that should be ignored when this handler's key conflicts with them.
    ///     Override this method to specify ignored conflicts for specific handlers.
    /// </summary>
    /// <returns>A list of key names to ignore conflicts with, or null/empty if no conflicts should be ignored.</returns>
    public virtual IEnumerable<string> GetIgnoredConflicts()
    {
        return null;
    }

    /// <summary>
    ///     Gets the list of key names that can conflict with this handler's key.
    ///     Override this method to register conflicts where this mod key should ignore other keys.
    ///     This method is called after the handler is registered.
    /// </summary>
    /// <returns>A list of key names that can conflict with this handler's key.</returns>
    public virtual IEnumerable<string> GetKeyConflicts()
    {
        return null;
    }

    public virtual void Bind()
    {
        KeyBindHelpers.Bind(Wrapper, Phase, Execute, InputState);
    }

    public void AddKey(ControlsGroup controlsGroup)
    {
        KeyBindHelpers.AddKey(Name, DefaultKey, controlsGroup);
    }

    public void AssignKey()
    {
        var keyCode = KeyManager.GetKey(Name);
        Plugin.Log.LogDebug($"Setting key {Name} to {keyCode}");
        Wrapper.AssignKey(keyCode);
    }

    protected abstract void Execute();
}
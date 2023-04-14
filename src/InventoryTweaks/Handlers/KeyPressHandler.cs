using InputSystem;
using InventoryTweaks.Helpers;
using UnityEngine;

namespace InventoryTweaks.Handlers;

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
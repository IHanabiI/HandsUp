using System.Collections.Generic;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;

namespace HandsUp.HandsUpCode.Services;

public static class RaiseHandHotkeyInputMapService
{
    private static readonly IReadOnlyDictionary<RaiseHandActionKind, StringName> ActionNames =
        new Dictionary<RaiseHandActionKind, StringName>
        {
            [RaiseHandActionKind.Restart] = "handsup_restart",
            [RaiseHandActionKind.SoftRestart] = "handsup_soft_restart",
            [RaiseHandActionKind.PreviousStep] = "handsup_previous_step",
            [RaiseHandActionKind.PreviousFloor] = "handsup_previous_floor"
        };

    public static StringName GetInputActionName(RaiseHandActionKind actionKind)
    {
        return ActionNames[actionKind];
    }

    public static void RefreshBindings()
    {
        foreach (var binding in RaiseHandHotkeySettingsService.GetBindingsSnapshot())
        {
            EnsureActionRegistered(binding.Key);
            InputMap.ActionEraseEvents(ActionNames[binding.Key]);

            if (binding.Value == Key.None)
                continue;

            InputMap.ActionAddEvent(ActionNames[binding.Key], CreateKeyboardEvent(binding.Value));
        }
    }

    private static void EnsureActionRegistered(RaiseHandActionKind actionKind)
    {
        var actionName = ActionNames[actionKind];
        if (!InputMap.HasAction(actionName))
            InputMap.AddAction(actionName, 0.5f);
    }

    private static InputEventKey CreateKeyboardEvent(Key key)
    {
        return new InputEventKey
        {
            Keycode = key
        };
    }
}

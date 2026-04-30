using System;
using System.Collections.Generic;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;

namespace HandsUp.HandsUpCode.Services;

public static class RaiseHandHotkeyRuntimeService
{
    private static readonly IReadOnlyDictionary<RaiseHandActionKind, Action> PressedBindings =
        new Dictionary<RaiseHandActionKind, Action>
        {
            [RaiseHandActionKind.Restart] = () => OnHotkeyPressed(RaiseHandActionKind.Restart),
            [RaiseHandActionKind.SoftRestart] = () => OnHotkeyPressed(RaiseHandActionKind.SoftRestart),
            [RaiseHandActionKind.PreviousStep] = () => OnHotkeyPressed(RaiseHandActionKind.PreviousStep),
            [RaiseHandActionKind.PreviousFloor] = () => OnHotkeyPressed(RaiseHandActionKind.PreviousFloor)
        };

    private static ulong _registeredHotkeyManagerId;

    public static void EnsureRegistered()
    {
        try
        {
            RaiseHandHotkeyInputMapService.RefreshBindings();

            var hotkeyManager = NHotkeyManager.Instance;
            if (hotkeyManager == null)
            {
                MainFile.Logger.Warn("Skipped HandsUp hotkey registration because NHotkeyManager.Instance is null.");
                return;
            }

            var managerId = hotkeyManager.GetInstanceId();
            if (_registeredHotkeyManagerId == managerId)
                return;

            foreach (var binding in PressedBindings)
            {
                var hotkey = RaiseHandHotkeyInputMapService.GetInputActionName(binding.Key).ToString();
                hotkeyManager.PushHotkeyPressedBinding(hotkey, binding.Value);
            }

            _registeredHotkeyManagerId = managerId;
            MainFile.Logger.Info("Registered HandsUp hotkeys with NHotkeyManager.");
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to register HandsUp hotkeys: {e}");
        }
    }

    private static void OnHotkeyPressed(RaiseHandActionKind actionKind)
    {
        if (ShouldBlockHotkeyHandling())
            return;

        RaiseHandActionService.BeginActionFromShortcut(actionKind);
    }

    private static bool ShouldBlockHotkeyHandling()
    {
        if (NGame.Instance == null || NRun.Instance == null)
            return true;

        if (NDevConsole.Instance?.Visible == true)
            return true;

        if (NGame.Instance.Transition?.InTransition == true)
            return true;

        if (NModalContainer.Instance?.OpenModal != null)
            return true;

        if (NCapstoneContainer.Instance?.InUse == true)
            return true;

        var focusedControl = NGame.Instance.GetViewport()?.GuiGetFocusOwner();
        if (focusedControl is LineEdit lineEdit && lineEdit.IsEditing())
            return true;

        if (focusedControl is NMegaTextEdit megaTextEdit && megaTextEdit.IsEditing())
            return true;

        return false;
    }
}

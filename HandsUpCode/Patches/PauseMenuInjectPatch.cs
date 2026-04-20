using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.PauseMenu;
using HandsUp.HandsUpCode.UI;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NRunSubmenuStack), nameof(NRunSubmenuStack.GetSubmenuType), typeof(Type))]
public static class InjectRaiseHandSubmenuPatch
{
    private static readonly BaseLib.Utils.SpireField<NRunSubmenuStack, RaiseHandSubmenu> SubmenuField =
        new(CreateSubmenu);

    private static RaiseHandSubmenu CreateSubmenu(NRunSubmenuStack stack)
    {
        var menu = new RaiseHandSubmenu();
        menu.Visible = false;
        stack.AddChildSafely(menu);
        return menu;
    }

    public static bool Prefix(NRunSubmenuStack __instance, Type type, ref NSubmenu __result)
    {
        if (type != typeof(RaiseHandSubmenu))
            return true;

        __result = SubmenuField.Get(__instance)!;
        return false;
    }
}

[HarmonyPatch(typeof(NPauseMenu), nameof(NPauseMenu._Ready))]
public static class InjectRaiseHandButtonPatch
{
    private const string RaiseHandButtonName = "RaiseHandButton";
    private const string RaiseHandLabel = "\u4e3e\u624b\u624b";

    public static void Postfix(NPauseMenu __instance)
    {
        try
        {
            InjectButton(__instance);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to inject Raise Hand button into pause menu: {e}");
        }
    }

    private static void InjectButton(NPauseMenu pauseMenu)
    {
        var buttonContainer = pauseMenu.GetNodeOrNull<Control>("%ButtonContainer");
        if (buttonContainer == null)
            return;

        if (buttonContainer.GetNodeOrNull<NPauseMenuButton>(RaiseHandButtonName) != null)
            return;

        var settingsButton = buttonContainer.GetNodeOrNull<NPauseMenuButton>("Settings");
        if (settingsButton == null)
            return;

        var duplicateFlags =
            (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts | Node.DuplicateFlags.UseInstantiation);
        var raiseHandButton = (NPauseMenuButton)settingsButton.Duplicate(duplicateFlags);
        raiseHandButton.Name = RaiseHandButtonName;

        settingsButton.AddSibling(raiseHandButton);
        raiseHandButton.GetNodeOrNull<Label>("Label")?.Set("text", RaiseHandLabel);
        raiseHandButton.GetNodeOrNull<Control>("Label")?.Set("theme_override_colors/font_color", StsColors.cream);

        raiseHandButton.Connect(NClickableControl.SignalName.Released,
            Callable.From<NButton>(_ => OpenRaiseHandSubmenu(pauseMenu)));

        RewireFocus(buttonContainer);
    }

    private static void OpenRaiseHandSubmenu(NPauseMenu pauseMenu)
    {
        var stack = pauseMenu.GetParentOrNull<NRunSubmenuStack>();
        if (stack == null)
        {
            MainFile.Logger.Warn("Raise Hand button was pressed, but no NRunSubmenuStack was found.");
            return;
        }

        var submenu = (RaiseHandSubmenu)stack.GetSubmenuType(typeof(RaiseHandSubmenu));
        submenu.InitializeFromPauseMenu(pauseMenu);
        stack.Push(submenu);
    }

    private static void RewireFocus(Control buttonContainer)
    {
        var buttons = new List<NPauseMenuButton>();
        foreach (var child in buttonContainer.GetChildren())
        {
            if (child is NPauseMenuButton button && button.Visible)
                buttons.Add(button);
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            var current = buttons[i];
            current.FocusNeighborLeft = current.GetPath();
            current.FocusNeighborRight = current.GetPath();
            current.FocusNeighborTop = (i > 0 ? buttons[i - 1] : current).GetPath();
            current.FocusNeighborBottom = (i < buttons.Count - 1 ? buttons[i + 1] : current).GetPath();
        }
    }
}

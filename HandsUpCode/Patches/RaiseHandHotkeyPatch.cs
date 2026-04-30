using System;
using System.Linq;
using Godot;
using HarmonyLib;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using HandsUp.HandsUpCode.UI;

namespace HandsUp.HandsUpCode.Patches;

[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
public static class RaiseHandHotkeyRegistrationPatch
{
    public static void Postfix()
    {
        try
        {
            RaiseHandHotkeyRuntimeService.EnsureRegistered();
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to register RaiseHand hotkeys: {e}");
        }
    }
}

[HarmonyPatch(typeof(NInputSettingsPanel), nameof(NInputSettingsPanel._Ready))]
public static class RaiseHandHotkeySettingsPatch
{
    private const string SectionName = "RaiseHandHotkeySettingsSection";

    public static void Postfix(NInputSettingsPanel __instance)
    {
        try
        {
            InjectSettingsSection(__instance);
        }
        catch (Exception e)
        {
            MainFile.Logger.Error($"Failed to inject RaiseHand hotkey settings UI: {e}");
        }
    }

    private static void InjectSettingsSection(NInputSettingsPanel inputPanel)
    {
        if (inputPanel.Content == null)
            return;

        if (inputPanel.Content.GetNodeOrNull<RaiseHandHotkeySettingsPanel>(SectionName) != null)
            return;

        var rowTemplate = inputPanel.Content?.GetChildren(false).OfType<NInputSettingsEntry>().FirstOrDefault();
        var section = new RaiseHandHotkeySettingsPanel
        {
            Name = SectionName
        };
        section.Initialize(rowTemplate);
        inputPanel.Content.AddChild(section);
        inputPanel.Content.MoveChild(section, inputPanel.Content.GetChildCount() - 1);

        RewireInputPanelFocus(inputPanel);
    }

    private static void RewireInputPanelFocus(NInputSettingsPanel inputPanel)
    {
        var options = new System.Collections.Generic.List<Control>();
        CollectFocusableOptions(inputPanel.Content, options);
        for (var i = 0; i < options.Count; i++)
        {
            var current = options[i];
            current.FocusNeighborLeft = current.GetPath();
            current.FocusNeighborRight = current.GetPath();
            current.FocusNeighborTop = (i > 0 ? options[i - 1] : current).GetPath();
            current.FocusNeighborBottom = (i < options.Count - 1 ? options[i + 1] : current).GetPath();
        }
    }

    private static void CollectFocusableOptions(Control parent, System.Collections.Generic.List<Control> results)
    {
        foreach (var control in parent.GetChildren(false).OfType<Control>())
        {
            if (control.FocusMode == Control.FocusModeEnum.All && control.Visible)
            {
                results.Add(control);
                continue;
            }

            CollectFocusableOptions(control, results);
        }
    }
}

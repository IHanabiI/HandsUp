using BaseLib.Config;
using Godot;
using HandsUp.HandsUpCode.UI;

namespace HandsUp.HandsUpCode.Config;

public sealed class HandsUpModConfig : ModConfig
{
    // BaseLib only lists configs with at least one visible property; the real hotkey settings stay profile-scoped.
    public static bool ShowInModSettings { get; set; } = true;

    public override void SetupConfigUI(Control optionContainer)
    {
        var panel = new RaiseHandHotkeySettingsPanel
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin
        };

        panel.Initialize(null);
        optionContainer.AddChild(panel);
    }
}

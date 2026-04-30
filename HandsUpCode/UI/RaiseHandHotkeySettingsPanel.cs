using System.Collections.Generic;
using System.Linq;
using Godot;
using HandsUp.HandsUpCode.Multiplayer;
using HandsUp.HandsUpCode.Services;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace HandsUp.HandsUpCode.UI;

public partial class RaiseHandHotkeySettingsPanel : NSettingsPanel
{
    private const string TitleText = "\u5feb\u6377\u952e\u8bbe\u7f6e";
    private const string SubtitleText = "\u53ea\u5728\u5c40\u5185\u751f\u6548\uff0c\u6548\u679c\u4e0e ESC -> \u4e3e\u624b\u624b \u5b8c\u5168\u4e00\u81f4\u3002";
    private const string CommandHeaderText = "\u529f\u80fd";
    private const string KeyboardHeaderText = "\u5feb\u6377\u952e";
    private const string ToggleLabelText = "\u5feb\u6377\u952e\u5f39\u7a97";
    private const string AnnouncementLabelText = "\u5f00\u53d1\u8005\u7533\u660e";
    private const string AnnouncementValueText = "\u67e5\u770b";
    private const string ToggleOnText = "\u5f00\u542f";
    private const string ToggleOffText = "\u5173\u95ed";
    private const string ResetLabelText = "\u6062\u590d\u9ed8\u8ba4\u5feb\u6377\u952e";
    private const string ResetValueText = "\u91cd\u7f6e";
    private const string ListeningText = "\u6309\u4e0b\u6309\u952e...";
    private const string HelperText =
        "\u5220\u9664 / \u9000\u683c\u53ef\u6e05\u9664\u7ed1\u5b9a\uff0cEsc \u53ef\u53d6\u6d88\u5f53\u524d\u8bbe\u7f6e\u3002" +
        "\u5173\u95ed\u201c\u5feb\u6377\u952e\u5f39\u7a97\u201d\u540e\uff0c\u53ea\u6709\u901a\u8fc7\u5feb\u6377\u952e\u89e6\u53d1\u65f6\u624d\u4f1a\u8df3\u8fc7\u786e\u8ba4\u5f39\u7a97\uff0cESC \u83dc\u5355\u4e0d\u53d7\u5f71\u54cd\u3002";
    private const float MinPanelWidth = 960f;
    private const float MinRowHeight = 62f;
    private readonly Dictionary<RaiseHandActionKind, HotkeyRowHandle> _actionRows = [];
    private readonly List<Button> _focusButtons = [];

    private VBoxContainer? _contentRoot;
    private Control? _rowTemplate;
    private HotkeyRowHandle? _toggleRow;
    private HotkeyRowHandle? _announcementRow;
    private HotkeyRowHandle? _resetRow;
    private RaiseHandActionKind? _listeningAction;
    private Tween? _visibilityTween;

    public void Initialize(Control? rowTemplate)
    {
        _rowTemplate = rowTemplate;
    }

    public override void _Ready()
    {
        SetProcessUnhandledKeyInput(true);
        MouseFilter = MouseFilterEnum.Pass;
        RaiseHandHotkeyInputMapService.RefreshBindings();

        BuildUi();
        RefreshAll();
        RefreshPanelSize();
        RewireFocus();

        Connect(CanvasItem.SignalName.VisibilityChanged, Callable.From(OnPanelVisibilityChanged));
        GetViewport().Connect(Viewport.SignalName.SizeChanged, Callable.From(RefreshPanelSize));
    }

    public override void _ExitTree()
    {
        base._ExitTree();
        var viewport = GetViewport();
        if (viewport != null)
            viewport.Disconnect(Viewport.SignalName.SizeChanged, Callable.From(RefreshPanelSize));

        Disconnect(CanvasItem.SignalName.VisibilityChanged, Callable.From(OnPanelVisibilityChanged));
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (!Visible || _listeningAction == null)
            return;

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
            return;

        var viewport = GetViewport();
        viewport?.SetInputAsHandled();

        if (keyEvent.Keycode == Key.Escape)
        {
            CancelListening();
            return;
        }

        if (keyEvent.Keycode is Key.Backspace or Key.Delete)
        {
            RaiseHandHotkeySettingsService.ClearBinding(_listeningAction.Value);
            CancelListening(refreshAfterCancel: false);
            RefreshAll();
            return;
        }

        if (keyEvent.CtrlPressed || keyEvent.AltPressed || keyEvent.MetaPressed)
            return;

        if (IsModifierOnlyKey(keyEvent.Keycode))
            return;

        RaiseHandHotkeySettingsService.SetBinding(_listeningAction.Value, keyEvent.Keycode);
        CancelListening(refreshAfterCancel: false);
        RefreshAll();
    }

    private void BuildUi()
    {
        _contentRoot = new VBoxContainer
        {
            Name = "VBoxContainer",
            CustomMinimumSize = new Vector2(MinPanelWidth, 0f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _contentRoot.AddThemeConstantOverride("separation", 16);
        AddChild(_contentRoot);

        _contentRoot.AddChild(CreateTitleLabel());
        _contentRoot.AddChild(CreateSubtitleLabel());
        _contentRoot.AddChild(CreateHeaderRow());

        foreach (var actionKind in RaiseHandActionService.OrderedActionKinds)
        {
            var row = CreateRow(
                RaiseHandActionService.GetActionLabel(actionKind),
                () => OnActionRowPressed(actionKind));
            _actionRows[actionKind] = row;
            _contentRoot.AddChild(row.Button);
            _focusButtons.Add(row.Button);
        }

        _toggleRow = CreateRow(ToggleLabelText, OnToggleRowPressed);
        _contentRoot.AddChild(_toggleRow.Button);
        _focusButtons.Add(_toggleRow.Button);

        _announcementRow = CreateRow(AnnouncementLabelText, OnAnnouncementRowPressed);
        _contentRoot.AddChild(_announcementRow.Button);
        _focusButtons.Add(_announcementRow.Button);

        _resetRow = CreateRow(ResetLabelText, OnResetRowPressed);
        _contentRoot.AddChild(_resetRow.Button);
        _focusButtons.Add(_resetRow.Button);

        _contentRoot.AddChild(CreateHelperLabel());
        _contentRoot.Connect(CanvasItem.SignalName.ItemRectChanged, Callable.From(RefreshPanelSize));
    }

    private Label CreateTitleLabel()
    {
        var label = new Label
        {
            Text = TitleText,
            HorizontalAlignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(MinPanelWidth, 44f)
        };
        label.AddThemeFontSizeOverride("font_size", 34);
        label.AddThemeColorOverride("font_color", StsColors.gold);
        label.AddThemeFontOverride("font", PreloadManager.Cache.GetAsset<Font>("res://themes/kreon_regular_glyph_space_one.tres"));
        return label;
    }

    private static Label CreateSubtitleLabel()
    {
        var label = new Label
        {
            Text = SubtitleText,
            HorizontalAlignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(MinPanelWidth, 54f)
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", StsColors.cream);
        return label;
    }

    private static Label CreateHelperLabel()
    {
        var label = new Label
        {
            Text = HelperText,
            HorizontalAlignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(MinPanelWidth, 72f)
        };
        label.AddThemeFontSizeOverride("font_size", 16);
        label.AddThemeColorOverride("font_color", StsColors.gray);
        return label;
    }

    private static HBoxContainer CreateHeaderRow()
    {
        var row = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(MinPanelWidth, 32f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        row.AddThemeConstantOverride("separation", 16);

        row.AddChild(CreateHeaderLabel(CommandHeaderText, HorizontalAlignment.Left, SizeFlags.ExpandFill));
        row.AddChild(CreateHeaderLabel(KeyboardHeaderText, HorizontalAlignment.Right, SizeFlags.Fill));
        return row;
    }

    private static Label CreateHeaderLabel(string text, HorizontalAlignment alignment, SizeFlags sizeFlags)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = alignment,
            SizeFlagsHorizontal = sizeFlags
        };
        label.AddThemeFontSizeOverride("font_size", 18);
        label.AddThemeColorOverride("font_color", StsColors.gray);
        return label;
    }

    private HotkeyRowHandle CreateRow(string title, Action onPressed)
    {
        var visual = CreateRowVisual();
        var titleNode = FindRowNode(visual, "InputLabel") ?? FindRowNode(visual, "TitleLabel");
        var valueNode = FindRowNode(visual, "KeyBindingInputLabel") ?? FindRowNode(visual, "ValueLabel");
        var background = FindRowNode(visual, "Bg") as Control;
        var controllerIcon = FindRowNode(visual, "ControllerBindingIcon") as CanvasItem;
        if (controllerIcon != null)
            controllerIcon.Visible = false;

        SetNodeText(titleNode, title);
        SetNodeText(valueNode, string.Empty);
        if (background != null)
            background.Visible = false;

        var button = CreateOverlayButton(visual);
        button.Connect(Button.SignalName.Pressed, Callable.From(onPressed));

        var row = new HotkeyRowHandle(button, visual, background, titleNode, valueNode);
        button.Connect(Control.SignalName.FocusEntered, Callable.From(() =>
        {
            row.IsFocused = true;
            ApplyRowHighlight(row);
        }));
        button.Connect(Control.SignalName.FocusExited, Callable.From(() =>
        {
            row.IsFocused = false;
            ApplyRowHighlight(row);
        }));
        button.Connect(Control.SignalName.MouseEntered, Callable.From(() =>
        {
            row.IsHovered = true;
            ApplyRowHighlight(row);
        }));
        button.Connect(Control.SignalName.MouseExited, Callable.From(() =>
        {
            row.IsHovered = false;
            ApplyRowHighlight(row);
        }));

        return row;
    }

    private static Button CreateOverlayButton(Control visual)
    {
        var button = new Button
        {
            Flat = true,
            FocusMode = FocusModeEnum.All,
            Text = string.Empty,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(Mathf.Max(MinPanelWidth, visual.GetMinimumSize().X), Mathf.Max(MinRowHeight, visual.GetMinimumSize().Y))
        };

        var emptyStyle = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", emptyStyle);
        button.AddThemeStyleboxOverride("hover", emptyStyle);
        button.AddThemeStyleboxOverride("pressed", emptyStyle);
        button.AddThemeStyleboxOverride("focus", emptyStyle);
        button.AddThemeStyleboxOverride("disabled", emptyStyle);

        visual.MouseFilter = MouseFilterEnum.Ignore;
        SetMouseFilterRecursive(visual, MouseFilterEnum.Ignore);
        visual.Position = Vector2.Zero;
        button.AddChild(visual);
        return button;
    }

    private Control CreateRowVisual()
    {
        if (_rowTemplate == null)
            return CreateFallbackRowVisual();

        try
        {
            var duplicateFlags = (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.UseInstantiation);
            var visual = (Control)_rowTemplate.Duplicate(duplicateFlags);
            visual.SetScript(default(Variant));
            visual.CustomMinimumSize = new Vector2(Mathf.Max(MinPanelWidth, visual.GetMinimumSize().X), Mathf.Max(MinRowHeight, visual.GetMinimumSize().Y));
            return visual;
        }
        catch
        {
            return CreateFallbackRowVisual();
        }
    }

    private static Control CreateFallbackRowVisual()
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(MinPanelWidth, MinRowHeight)
        };

        var background = new ColorRect
        {
            Name = "Bg",
            Color = new Color(1f, 1f, 1f, 0.09f),
            Visible = false,
            MouseFilter = MouseFilterEnum.Ignore
        };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        root.AddChild(background);

        var container = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(MinPanelWidth, MinRowHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        container.AddThemeConstantOverride("separation", 24);
        root.AddChild(container);

        var title = new Label
        {
            Name = "TitleLabel",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", StsColors.cream);
        container.AddChild(title);

        var value = new Label
        {
            Name = "ValueLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(220f, 0f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        value.AddThemeFontSizeOverride("font_size", 22);
        value.AddThemeColorOverride("font_color", StsColors.cream);
        container.AddChild(value);

        return root;
    }

    private void OnActionRowPressed(RaiseHandActionKind actionKind)
    {
        _listeningAction = _listeningAction == actionKind ? null : actionKind;
        RefreshAll();
    }

    private void OnToggleRowPressed()
    {
        CancelListening();
        var nextValue = !RaiseHandHotkeySettingsService.ShouldShowShortcutConfirmPopup();
        RaiseHandHotkeySettingsService.SetShowShortcutConfirmPopup(nextValue);
        RefreshAll();
    }

    private void OnAnnouncementRowPressed()
    {
        CancelListening();
        RaiseHandAnnouncementService.ShowAnnouncementPopup();
    }

    private void OnResetRowPressed()
    {
        CancelListening();
        RaiseHandHotkeySettingsService.ResetToDefaults();
        RefreshAll();
    }

    private void CancelListening(bool refreshAfterCancel = true)
    {
        if (_listeningAction == null)
            return;

        _listeningAction = null;
        if (refreshAfterCancel)
            RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var (actionKind, row) in _actionRows)
        {
            var valueText = _listeningAction == actionKind
                ? ListeningText
                : RaiseHandHotkeySettingsService.GetBindingDisplayText(actionKind);
            SetNodeText(row.ValueNode, valueText);
            SetNodeModulate(row.ValueNode, _listeningAction == actionKind ? StsColors.gold : Colors.White);
            ApplyRowHighlight(row);
        }

        if (_toggleRow != null)
        {
            var toggleValue = RaiseHandHotkeySettingsService.ShouldShowShortcutConfirmPopup()
                ? ToggleOnText
                : ToggleOffText;
            SetNodeText(_toggleRow.ValueNode, toggleValue);
            SetNodeModulate(_toggleRow.ValueNode, Colors.White);
            ApplyRowHighlight(_toggleRow);
        }

        if (_announcementRow != null)
        {
            SetNodeText(_announcementRow.ValueNode, AnnouncementValueText);
            SetNodeModulate(_announcementRow.ValueNode, StsColors.gold);
            ApplyRowHighlight(_announcementRow);
        }

        if (_resetRow != null)
        {
            SetNodeText(_resetRow.ValueNode, ResetValueText);
            SetNodeModulate(_resetRow.ValueNode, StsColors.gold);
            ApplyRowHighlight(_resetRow);
        }
    }

    private void ApplyRowHighlight(HotkeyRowHandle row)
    {
        if (row.Background != null)
            row.Background.Visible = row.IsFocused || row.IsHovered;
    }

    private void RewireFocus()
    {
        for (var i = 0; i < _focusButtons.Count; i++)
        {
            var current = _focusButtons[i];
            current.FocusNeighborLeft = current.GetPath();
            current.FocusNeighborRight = current.GetPath();
            current.FocusNeighborTop = (i > 0 ? _focusButtons[i - 1] : current).GetPath();
            current.FocusNeighborBottom = (i < _focusButtons.Count - 1 ? _focusButtons[i + 1] : current).GetPath();
        }

        _firstControl = _focusButtons.FirstOrDefault();
    }

    private void RefreshPanelSize()
    {
        if (_contentRoot == null)
            return;

        var minSize = _contentRoot.GetCombinedMinimumSize();
        CustomMinimumSize = new Vector2(Mathf.Max(MinPanelWidth, minSize.X), minSize.Y);
        Size = CustomMinimumSize;
    }

    private void OnPanelVisibilityChanged()
    {
        if (!Visible)
        {
            CancelListening();
            return;
        }

        _visibilityTween?.Kill();
        Modulate = StsColors.transparentBlack;
        _visibilityTween = CreateTween().SetParallel(true);
        _visibilityTween.TweenProperty(this, "modulate", Colors.White, 0.5f)
            .SetEase(Tween.EaseType.Out)
            .SetTrans(Tween.TransitionType.Cubic);
    }

    private static void SetMouseFilterRecursive(Node node, MouseFilterEnum mouseFilter)
    {
        if (node is Control control)
            control.MouseFilter = mouseFilter;

        foreach (var child in node.GetChildren())
            SetMouseFilterRecursive(child, mouseFilter);
    }

    private static Node? FindRowNode(Node root, string name)
    {
        return root.FindChild(name, true, false);
    }

    private static void SetNodeText(Node? node, string text)
    {
        if (node == null)
            return;

        node.Set("text", text);
    }

    private static void SetNodeModulate(Node? node, Color color)
    {
        if (node is CanvasItem canvasItem)
            canvasItem.Modulate = color;
    }

    private static bool IsModifierOnlyKey(Key key)
    {
        return key is Key.Shift or Key.Ctrl or Key.Alt or Key.Meta;
    }

    private sealed class HotkeyRowHandle(
        Button button,
        Control visualRoot,
        Control? background,
        Node? titleNode,
        Node? valueNode)
    {
        public Button Button { get; } = button;
        public Control VisualRoot { get; } = visualRoot;
        public Control? Background { get; } = background;
        public Node? TitleNode { get; } = titleNode;
        public Node? ValueNode { get; } = valueNode;
        public bool IsFocused { get; set; }
        public bool IsHovered { get; set; }
    }
}

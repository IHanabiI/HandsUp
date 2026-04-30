using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace HandsUp.HandsUpCode.Services;

public static class RaiseHandAnnouncementService
{
    private const string NoticeTitleText = "\u5f00\u53d1\u8005\u8bf4\u660e";
    private const string NoticeBodyText =
        "[center]\u672c\u6a21\u7ec4\u4e3a\u514d\u8d39\u5206\u4eab[/center]\n" +
        "[center]\u4e0d\u5141\u8bb8\u7528\u4e8e\u4efb\u4f55\u76c8\u5229[/center]\n" +
        "[center]\u6709\u7591\u95ee\u53ef\u524d\u5f80\u6296\u97f3[/center]\n" +
        "[center]\u641c\u7d22\u4f5c\u8005\u52a0\u5165\u7fa4\u804a[/center]\n" +
        "[center]\u6709Bug\u8bf7\u5f55\u89c6\u9891\u53cd\u9988[/center]\n" +
        "[center]\u611f\u8c22\u5927\u5bb6\u7684\u52a0\u7fa4\u652f\u6301[/center]\n" +
        "\n[right]IHanabiI[/right]";
    private const string ConfirmButtonText = "\u786e\u5b9a";

    public static void ShowAnnouncementPopup()
    {
        if (NModalContainer.Instance == null)
        {
            MainFile.Logger.Warn("Developer announcement popup skipped because NModalContainer.Instance is null.");
            return;
        }

        if (NModalContainer.Instance.OpenModal != null)
        {
            MainFile.Logger.Info("Developer announcement popup is clearing the current modal before showing the message.");
            NModalContainer.Instance.Clear();
        }

        Callable.From(CreateAnnouncementPopup).CallDeferred();
    }

    private static void CreateAnnouncementPopup()
    {
        if (NModalContainer.Instance == null)
            return;

        var popup = NDisconnectConfirmPopup.Create();
        if (popup == null)
        {
            MainFile.Logger.Warn("Developer announcement popup skipped because popup creation returned null.");
            return;
        }

        NModalContainer.Instance.Add(popup, true);
        Callable.From(() => ConfigureAnnouncementPopup(popup)).CallDeferred();
    }

    private static void ConfigureAnnouncementPopup(NDisconnectConfirmPopup popup)
    {
        var verticalPopup = popup.GetNodeOrNull<NVerticalPopup>("VerticalPopup");
        if (verticalPopup == null)
        {
            MainFile.Logger.Warn("Developer announcement popup skipped because VerticalPopup node was not found.");
            NModalContainer.Instance?.Clear();
            return;
        }

        verticalPopup.DisconnectSignals();
        verticalPopup.DisconnectHotkeys();
        verticalPopup.SetText(NoticeTitleText, NoticeBodyText);
        verticalPopup.YesButton.IsYes = true;
        verticalPopup.YesButton.SetText(ConfirmButtonText);
        verticalPopup.NoButton.Visible = false;
        verticalPopup.NoButton.FocusMode = Control.FocusModeEnum.None;
        verticalPopup.InitYesButton(new LocString("main_menu_ui", "GENERIC_POPUP.confirm"), _ => { });
    }
}

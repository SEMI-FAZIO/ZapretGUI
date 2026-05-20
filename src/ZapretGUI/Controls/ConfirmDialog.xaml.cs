using System.Windows;
using ZapretGUI.Services;

namespace ZapretGUI.Controls;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static bool Ask(Window? owner, string titleKey, string bodyKey, string? confirmKey = null, string? cancelKey = null, ConfirmTone tone = ConfirmTone.Warning)
    {
        var d = new ConfirmDialog { Owner = owner ?? Application.Current.MainWindow };
        d.TitleText.Text = LocalizationService.Get(titleKey);
        d.BodyText.Text = LocalizationService.Get(bodyKey);
        d.ConfirmText.Text = LocalizationService.Get(confirmKey ?? "Common.Confirm");
        d.CancelText.Text = LocalizationService.Get(cancelKey ?? "Common.Cancel");
        d.ApplyTone(tone);
        return d.ShowDialog() == true;
    }

    private void ApplyTone(ConfirmTone tone)
    {
        switch (tone)
        {
            case ConfirmTone.Danger:
                IconText.Text = "";
                IconBox.Background = (System.Windows.Media.Brush)Application.Current.Resources["DangerSoftBrush"];
                IconText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["DangerBrush"];
                break;
            case ConfirmTone.Info:
                IconText.Text = "";
                IconBox.Background = (System.Windows.Media.Brush)Application.Current.Resources["InfoSoftBrush"];
                IconText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["InfoBrush"];
                break;
            default:
                IconText.Text = "";
                IconBox.Background = (System.Windows.Media.Brush)Application.Current.Resources["WarningSoftBrush"];
                IconText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["WarningBrush"];
                break;
        }
    }

    private void OnConfirm(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
    private void OnCancel(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}

public enum ConfirmTone { Warning, Danger, Info }

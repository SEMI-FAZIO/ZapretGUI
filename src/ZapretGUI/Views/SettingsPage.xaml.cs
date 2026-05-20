using System.Windows.Controls;
using ZapretGUI.Services;
using ZapretGUI.ViewModels;

namespace ZapretGUI.Views;

public partial class SettingsPage : UserControl
{
    public SettingsPage() => InitializeComponent();

    private SettingsViewModel? VM => DataContext as SettingsViewModel;

    private void OnThemeDark(object s, System.Windows.RoutedEventArgs e)   { if (VM is not null) VM.Theme = AppTheme.Dark; }
    private void OnThemeLight(object s, System.Windows.RoutedEventArgs e)  { if (VM is not null) VM.Theme = AppTheme.Light; }
    private void OnThemeSystem(object s, System.Windows.RoutedEventArgs e) { if (VM is not null) VM.Theme = AppTheme.System; }

    private void OnLangRu(object s, System.Windows.RoutedEventArgs e)     { if (VM is not null) VM.Language = AppLanguage.Ru; }
    private void OnLangEn(object s, System.Windows.RoutedEventArgs e)     { if (VM is not null) VM.Language = AppLanguage.En; }
    private void OnLangSystem(object s, System.Windows.RoutedEventArgs e) { if (VM is not null) VM.Language = AppLanguage.System; }
}

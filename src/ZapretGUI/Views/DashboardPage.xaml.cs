using System.Windows.Controls;
using ZapretGUI.Models;
using ZapretGUI.ViewModels;

namespace ZapretGUI.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage() => InitializeComponent();

    private DashboardViewModel? VM => DataContext as DashboardViewModel;

    private void OnGameFilterOff(object s, System.Windows.RoutedEventArgs e) => VM?.SetGameFilter(GameFilterMode.Disabled);
    private void OnGameFilterAll(object s, System.Windows.RoutedEventArgs e) => VM?.SetGameFilter(GameFilterMode.All);
    private void OnGameFilterTcp(object s, System.Windows.RoutedEventArgs e) => VM?.SetGameFilter(GameFilterMode.Tcp);
    private void OnGameFilterUdp(object s, System.Windows.RoutedEventArgs e) => VM?.SetGameFilter(GameFilterMode.Udp);
    private void OnIpsetNone(object s, System.Windows.RoutedEventArgs e) => VM?.SetIpset(IpsetMode.None);
    private void OnIpsetLoaded(object s, System.Windows.RoutedEventArgs e) => VM?.SetIpset(IpsetMode.Loaded);
    private void OnIpsetAny(object s, System.Windows.RoutedEventArgs e) => VM?.SetIpset(IpsetMode.Any);

    private void OnAutoUpdateToggle(object s, System.Windows.RoutedEventArgs e)
    {
        if (s is System.Windows.Controls.Primitives.ToggleButton tb && VM is not null)
            VM.SetAutoUpdate(tb.IsChecked ?? false);
    }
}

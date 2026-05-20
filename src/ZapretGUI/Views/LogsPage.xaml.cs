using System.Windows;
using System.Windows.Controls;
using ZapretGUI.Services;
using ZapretGUI.ViewModels;

namespace ZapretGUI.Views;

public partial class LogsPage : UserControl
{
    public LogsPage()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is LogsViewModel vm)
            {
                vm.NewEntryAdded += () => Dispatcher.BeginInvoke(() =>
                {
                    if (vm.AutoScroll) LogsScroll.ScrollToEnd();
                });
            }
        };
    }

    private void OnPauseToggle(object sender, RoutedEventArgs e)
    {
        if (DataContext is LogsViewModel vm)
        {
            vm.Paused = !vm.Paused;
            PauseText.Text = LocalizationService.Get(vm.Paused ? "Logs.Resume" : "Logs.Pause");
            PauseIcon.Text = vm.Paused ? "" : "";
        }
    }

    private void OnFilterAll(object sender, RoutedEventArgs e)     { if (DataContext is LogsViewModel vm) vm.FilterSource = null; }
    private void OnFilterWinws(object sender, RoutedEventArgs e)   { if (DataContext is LogsViewModel vm) vm.FilterSource = ZapretGUI.Services.LogSource.Winws; }
    private void OnFilterService(object sender, RoutedEventArgs e) { if (DataContext is LogsViewModel vm) vm.FilterSource = ZapretGUI.Services.LogSource.Service; }
}

using System.Windows;
using System.Windows.Input;
using ZapretGUI.Services;
using ZapretGUI.ViewModels;

namespace ZapretGUI;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Window dragging is handled automatically by WindowChrome.CaptionHeight=36.
        // A global MouseLeftButtonDown handler would steal click events from the title-bar
        // buttons; rely on the chrome instead.

        Loaded += OnLoaded;
        Closing += OnClosing;
        StateChanged += (_, _) => UpdateMaxIcon();

        SetupHotkeys();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        WindowEffects.ApplyMica(this);
        WindowStateService.Restore(this);
        UpdateMaxIcon();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        WindowStateService.Save(this);
    }

    private void UpdateMaxIcon()
    {
        if (MaxBtn is null) return;
        // E922 = maximize square, E923 = restore (two stacked squares)
        MaxBtn.Content = WindowState == WindowState.Maximized ? "" : "";
        MaxBtn.ToolTip = WindowState == WindowState.Maximized ? "Восстановить" : "Развернуть";
    }

    private void SetupHotkeys()
    {
        InputBindings.Add(new KeyBinding(new Helpers.RelayCommand(_ => RefreshAll()), Key.F5, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(new Helpers.RelayCommand(_ => WindowState = WindowState.Minimized),
            Key.M, ModifierKeys.Control));
        for (int i = 1; i <= 6; i++)
        {
            int idx = i - 1;
            InputBindings.Add(new KeyBinding(new Helpers.RelayCommand(_ => SelectNav(idx)),
                (Key)((int)Key.D1 + i - 1), ModifierKeys.Control));
        }
    }

    private void SelectNav(int index)
    {
        if (DataContext is MainViewModel vm && index >= 0 && index < vm.NavItems.Count)
            vm.SelectedNav = vm.NavItems[index];
    }

    private void RefreshAll()
    {
        if (DataContext is MainViewModel vm) vm.RefreshStatus();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

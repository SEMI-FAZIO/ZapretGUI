using System.Windows;

namespace ZapretGUI.Services;

public static class WindowStateService
{
    public static void Restore(Window window)
    {
        var s = SettingsService.Instance.Current;
        if (s.WindowWidth > 200 && s.WindowHeight > 200)
        {
            window.Width = s.WindowWidth;
            window.Height = s.WindowHeight;
        }
        if (s.WindowLeft > -10000 && s.WindowTop > -10000)
        {
            // Sanity-check against virtual screen so we don't restore onto a now-disconnected monitor.
            var screen = SystemParameters.VirtualScreenWidth;
            var screenH = SystemParameters.VirtualScreenHeight;
            if (s.WindowLeft < screen - 100 && s.WindowTop < screenH - 100)
            {
                window.Left = s.WindowLeft;
                window.Top = s.WindowTop;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }
        if (s.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    public static void Save(Window window)
    {
        SettingsService.Instance.Update(s =>
        {
            s.WindowMaximized = window.WindowState == WindowState.Maximized;
            if (window.WindowState == WindowState.Normal)
            {
                s.WindowLeft = window.Left;
                s.WindowTop = window.Top;
                s.WindowWidth = window.Width;
                s.WindowHeight = window.Height;
            }
        });
    }
}

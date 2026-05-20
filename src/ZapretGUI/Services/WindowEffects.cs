using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ZapretGUI.Services;

/// <summary>
/// Applies Windows 11 Mica / dark titlebar via DwmSetWindowAttribute.
/// Silently noops on Windows 10 or older where the attributes don't exist.
/// </summary>
public static class WindowEffects
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 2;  // Mica
    private const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
    private const int DWMSBT_TABBEDWINDOW = 4; // Tabbed Mica

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);

    public static void ApplyMica(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).EnsureHandle();
            if (hwnd == IntPtr.Zero) return;

            bool isDark = ThemeService.Instance.IsDark;
            int darkVal = isDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));

            // Mica works on Windows 11 22H2+; on older Windows the call returns an error code but doesn't throw.
            int backdrop = DWMSBT_MAINWINDOW;
            int res = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));

            if (res == 0)
            {
                // Mica was applied; let the system tint through by making the window background transparent.
                // We keep a faint colored overlay on top of the Mica surface for hierarchy.
                window.Background = Brushes.Transparent;
            }

            ThemeService.Instance.ThemeChanged += () =>
            {
                int dv = ThemeService.Instance.IsDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dv, sizeof(int));
            };
        }
        catch { /* not supported — ignore */ }
    }
}

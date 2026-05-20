using Microsoft.Win32;
using System.Windows;

namespace ZapretGUI.Services;

public sealed class ThemeService
{
    private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
    public static ThemeService Instance => _instance.Value;

    public event Action? ThemeChanged;
    public AppTheme Current { get; private set; }
    public bool IsDark { get; private set; }

    private ThemeService() { }

    public void Apply(AppTheme theme)
    {
        Current = theme;
        IsDark = Resolve(theme);

        string palette = IsDark ? "Palette.dark.xaml" : "Palette.light.xaml";
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{palette}", UriKind.Absolute),
        };

        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            string? source = dicts[i].Source?.OriginalString;
            if (source is not null && source.Contains("Palette.", StringComparison.OrdinalIgnoreCase))
                dicts.RemoveAt(i);
        }
        dicts.Insert(0, dict);

        ThemeChanged?.Invoke();
    }

    private static bool Resolve(AppTheme t) => t switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => IsSystemDark(),
    };

    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int i) return i == 0;
        }
        catch { }
        return true;
    }
}

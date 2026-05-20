using System.Globalization;
using System.Windows;

namespace ZapretGUI.Services;

public sealed class LocalizationService
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    public static LocalizationService Instance => _instance.Value;

    public event Action? LanguageChanged;

    public AppLanguage Current { get; private set; }
    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("ru-RU");

    private LocalizationService()
    {
        Apply(SettingsService.Instance.Current.Language);
    }

    public void Apply(AppLanguage lang)
    {
        Current = lang;
        string code = Resolve(lang);
        Culture = CultureInfo.GetCultureInfo(code);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;

        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/Strings.{code}.xaml", UriKind.Absolute),
        };

        var dicts = Application.Current.Resources.MergedDictionaries;
        for (int i = dicts.Count - 1; i >= 0; i--)
        {
            string? source = dicts[i].Source?.OriginalString;
            if (source is not null && source.Contains("Strings.", StringComparison.OrdinalIgnoreCase))
                dicts.RemoveAt(i);
        }
        dicts.Add(dict);

        LanguageChanged?.Invoke();
    }

    private static string Resolve(AppLanguage l) => l switch
    {
        AppLanguage.En => "en",
        AppLanguage.Ru => "ru",
        _ => CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en",
    };

    public string this[string key] => Application.Current.TryFindResource(key) as string ?? key;

    public static string Get(string key) => Instance[key];
}

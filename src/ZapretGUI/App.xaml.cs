using System.IO;
using System.Windows;
using ZapretGUI.Services;
using ZapretGUI.Views;

namespace ZapretGUI;

public partial class App : Application
{
    public static ZapretController Controller { get; private set; } = null!;
    public static SettingsService Settings => SettingsService.Instance;
    public static ThemeService Theme => ThemeService.Instance;
    public static LocalizationService Loc => LocalizationService.Instance;
    public static LogStreamService Logs { get; private set; } = null!;
    public static AutoStartService AutoStart { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "ZapretGUI_crash.log"),
                    $"[{DateTime.Now:O}] {ex.ExceptionObject}\n");
            }
            catch { }
        };

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.ToString(), "ZapretGUI",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // 0. Apply theme + language FIRST so the splash uses correct colors.
        _ = Settings.Current;
        Theme.Apply(Settings.Current.Theme);
        Loc.Apply(Settings.Current.Language);

        // 1. Splash
        var splash = new SplashWindow();
        splash.Show();

        // 2. Init controller in the background, swap to MainWindow when done.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                splash.SetStatus("Поиск zapret…");
                string root = ZapretController.ResolveInitialRoot();
                Controller = new ZapretController(root);

                splash.SetStatus("Подготовка сервисов…");
                Logs = new LogStreamService();
                AutoStart = new AutoStartService();
                AutoStart.Sync(Settings.Current.StartWithWindows);

                splash.SetStatus("Сборка интерфейса…");
                Window main;
                if (!Controller.IsZapretInstalled)
                {
                    main = new InstallWindow();
                }
                else
                {
                    Settings.Update(s => { s.ZapretRoot = root; s.FirstRunCompleted = true; });
                    main = new MainWindow();
                    if (Settings.Current.StartMinimized) main.WindowState = WindowState.Minimized;
                }

                splash.SetStatus("Готово");
                MainWindow = main;
                main.Show();
                splash.FadeAndClose();
            }
            catch (Exception ex)
            {
                splash.Close();
                MessageBox.Show("Startup failed: " + ex, "ZapretGUI", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }
}

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using ZapretGUI.Services;

namespace ZapretGUI.Views;

public partial class InstallWindow : Window
{
    public InstallWindow()
    {
        InitializeComponent();
        PathBox.Text = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
        };
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку для установки zapret",
            InitialDirectory = PathBox.Text,
        };
        if (dlg.ShowDialog(this) == true) PathBox.Text = dlg.FolderName;
    }

    private void OnOpenSite(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ZapretInstaller.RepoUrl) { UseShellExecute = true }); } catch { }
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        string target = PathBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(target))
        {
            MessageBox.Show("Укажите путь.");
            return;
        }

        InstallBtn.IsEnabled = false;
        ManualBtn.IsEnabled = false;
        ProgressCard.Visibility = Visibility.Visible;
        ProgressFillContainer.Width = 0;
        DoneText.Visibility = Visibility.Collapsed;

        var installer = new ZapretInstaller();
        installer.Status += msg => Dispatcher.BeginInvoke(() => StatusText.Text = msg);
        installer.Progress += pct => Dispatcher.BeginInvoke(() =>
        {
            double maxW = ((FrameworkElement)ProgressFillContainer.Parent).ActualWidth;
            ProgressFillContainer.Width = maxW * pct / 100.0;
        });

        try
        {
            string tag = await installer.InstallToAsync(target);
            App.Settings.Update(s => { s.ZapretRoot = target; s.FirstRunCompleted = true; });
            App.Controller.Rebind(target);

            DoneText.Text = $"{LocalizationService.Get("Install.Done")} ({tag})";
            DoneText.Visibility = Visibility.Visible;
            RestartBtn.Visibility = Visibility.Visible;
            InstallBtn.Visibility = Visibility.Collapsed;
            ManualBtn.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            DoneText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["DangerBrush"];
            DoneText.Text = $"{LocalizationService.Get("Install.Failed")} {ex.Message}";
            DoneText.Visibility = Visibility.Visible;
            InstallBtn.IsEnabled = true;
            ManualBtn.IsEnabled = true;
        }
    }

    private void OnManual(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog
        {
            Title = "Выберите папку с уже установленным zapret",
            InitialDirectory = PathBox.Text,
        };
        if (dlg.ShowDialog(this) != true) return;

        string picked = dlg.FolderName;
        if (!ZapretController.IsZapretRoot(picked))
        {
            MessageBox.Show("В указанной папке нет bin\\winws.exe и lists\\.\nВыберите корень установки zapret.");
            return;
        }

        App.Settings.Update(s => { s.ZapretRoot = picked; s.FirstRunCompleted = true; });
        App.Controller.Rebind(picked);
        SwitchToMain();
    }

    private void OnRestart(object sender, RoutedEventArgs e) => SwitchToMain();

    private void SwitchToMain()
    {
        var main = new MainWindow();
        Application.Current.MainWindow = main;
        main.Show();
        Close();
    }
}

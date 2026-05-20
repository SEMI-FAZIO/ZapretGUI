using Microsoft.Win32;

namespace ZapretGUI.Services;

public sealed class AutoStartService
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ZapretGUI";

    public bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
        }
        catch { return false; }
    }

    public void SetEnabled(bool enable)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                          ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (k is null) return;

            if (enable)
            {
                string path = Environment.ProcessPath ?? AppContext.BaseDirectory;
                string args = SettingsService.Instance.Current.StartMinimized ? " --minimized" : string.Empty;
                k.SetValue(ValueName, $"\"{path}\"{args}", RegistryValueKind.String);
            }
            else
            {
                if (k.GetValue(ValueName) is not null) k.DeleteValue(ValueName, false);
            }
        }
        catch { }
    }

    public void Sync(bool desired)
    {
        if (IsEnabled() != desired) SetEnabled(desired);
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ZapretGUI.Models;

namespace ZapretGUI.Helpers;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        bool v = value is bool b && b;
        if (invert) v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        bool isNull = value is null;
        bool show = invert ? isNull : !isNull;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();
    public object ConvertBack(object? value, Type t, object? parameter, CultureInfo c)
        => value is bool b && b ? Enum.Parse(t, parameter!.ToString()!) : Binding.DoNothing;
}

public sealed class RunStateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        if (value is not BypassRunState s) return "—";
        string key = s switch
        {
            BypassRunState.RunningAsService => "Status.Running.Service",
            BypassRunState.RunningStandalone => "Status.Running.Standalone",
            _ => "Status.Stopped",
        };
        return Application.Current?.TryFindResource(key) as string ?? key;
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class RunStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        bool active = value switch
        {
            BypassRunState s => s != BypassRunState.Stopped,
            bool b => b,
            _ => false,
        };
        var resources = Application.Current?.Resources;
        if (resources is null) return System.Windows.Media.Brushes.Gray;
        return resources[active ? "SuccessBrush" : "DangerBrush"];
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class IsRunningConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => value is BypassRunState s && s != BypassRunState.Stopped;
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class DiagnosticLevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        return value is DiagnosticLevel l ? l switch
        {
            DiagnosticLevel.Ok => Application.Current.Resources["SuccessBrush"],
            DiagnosticLevel.Warning => Application.Current.Resources["WarningBrush"],
            DiagnosticLevel.Error => Application.Current.Resources["DangerBrush"],
            _ => Application.Current.Resources["InfoBrush"],
        } : Application.Current.Resources["TextMutedBrush"];
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class DiagnosticLevelToBgConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        return value is DiagnosticLevel l ? l switch
        {
            DiagnosticLevel.Ok => Application.Current.Resources["SuccessSoftBrush"],
            DiagnosticLevel.Warning => Application.Current.Resources["WarningSoftBrush"],
            DiagnosticLevel.Error => Application.Current.Resources["DangerSoftBrush"],
            _ => Application.Current.Resources["InfoSoftBrush"],
        } : Application.Current.Resources["SurfaceElevatedBrush"];
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class DiagnosticLevelToIconConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        return value is DiagnosticLevel l ? l switch
        {
            DiagnosticLevel.Ok => "",
            DiagnosticLevel.Warning => "",
            DiagnosticLevel.Error => "",
            _ => "",
        } : "";
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class StringEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class DiffKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        string key = value switch
        {
            ZapretGUI.Services.DiffKind.Added => "SuccessBrush",
            ZapretGUI.Services.DiffKind.Removed => "DangerBrush",
            ZapretGUI.Services.DiffKind.Changed => "WarningBrush",
            _ => "TextMutedBrush",
        };
        return Application.Current?.Resources[key] ?? System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class DiffKindToTagConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        ZapretGUI.Services.DiffKind.Added => "+",
        ZapretGUI.Services.DiffKind.Removed => "−",
        ZapretGUI.Services.DiffKind.Changed => "Δ",
        _ => "·",
    };
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class ToastKindToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        string key = value switch
        {
            ZapretGUI.Services.ToastKind.Success => "SuccessBrush",
            ZapretGUI.Services.ToastKind.Warning => "WarningBrush",
            ZapretGUI.Services.ToastKind.Error => "DangerBrush",
            _ => "InfoBrush",
        };
        return Application.Current?.Resources[key] ?? System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class ToastKindToIconConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c) => value switch
    {
        ZapretGUI.Services.ToastKind.Success => "",
        ZapretGUI.Services.ToastKind.Warning => "",
        ZapretGUI.Services.ToastKind.Error => "",
        _ => "",
    };
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        int n = value is int v ? v : 0;
        bool wantEmpty = p is string s && s.Equals("empty", StringComparison.OrdinalIgnoreCase);
        bool isEmpty = n == 0;
        bool show = wantEmpty ? isEmpty : !isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

public sealed class BoolToTextConverter : IValueConverter
{
    public object Convert(object? value, Type t, object? p, CultureInfo c)
    {
        bool b = value is bool x && x;
        string param = p as string ?? "Активен|Неактивен";
        var parts = param.Split('|');
        return b ? parts[0] : (parts.Length > 1 ? parts[1] : parts[0]);
    }
    public object ConvertBack(object v, Type t, object? p, CultureInfo c) => throw new NotImplementedException();
}

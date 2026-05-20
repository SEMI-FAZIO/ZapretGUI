using System.Windows;
using System.Windows.Media.Animation;

namespace ZapretGUI.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetStatus(string text)
    {
        if (CheckAccess()) StatusText.Text = text;
        else Dispatcher.BeginInvoke(() => StatusText.Text = text);
    }

    public void FadeAndClose()
    {
        var anim = new DoubleAnimation
        {
            From = 1, To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
        };
        anim.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, anim);
    }
}

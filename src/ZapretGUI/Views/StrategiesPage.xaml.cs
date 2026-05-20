using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ZapretGUI.ViewModels;

namespace ZapretGUI.Views;

public partial class StrategiesPage : UserControl
{
    public StrategiesPage() => InitializeComponent();

    private void OnCategoryClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is StrategiesViewModel vm && sender is RadioButton rb)
            vm.FilterCategory = rb.Content?.ToString();
    }
}

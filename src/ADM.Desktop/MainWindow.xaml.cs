using System.Windows;

namespace ADM.Desktop;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Deactivated += (_, _) => DismissDropDowns(this);
    }

    private static void DismissDropDowns(System.Windows.DependencyObject parent)
    {
        if (parent is System.Windows.Controls.ComboBox combo) combo.IsDropDownOpen = false;
        for (int index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
            DismissDropDowns(System.Windows.Media.VisualTreeHelper.GetChild(parent, index));
    }

}

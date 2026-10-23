using System.Windows;
using NetPulse.App.ViewModels;

namespace NetPulse.App;

public partial class MainWindow : Window
{
    public MainWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAdaptersCommand.ExecuteAsync(null);
    }
}

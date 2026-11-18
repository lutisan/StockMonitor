using System.Windows;
using StockMonitor.ViewModels;

namespace StockMonitor;

/// <summary>
/// Main window hosting the 5 Syncfusion portfolio tabs. All commands and state are bound via <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and binds its <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    /// <param name="viewModel">The main portfolio view model.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

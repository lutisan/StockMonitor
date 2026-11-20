using System.ComponentModel;
using System.Windows;
using StockMonitor.ViewModels;

namespace StockMonitor;

/// <summary>
/// Main window hosting the Syncfusion <c>MenuAdv</c> bar and 5 Syncfusion portfolio tabs. All commands and state are bound via <see cref="MainViewModel"/>.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _isClosingConfirmed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class and binds its <see cref="FrameworkElement.DataContext"/>.
    /// </summary>
    /// <param name="viewModel">The main portfolio view model.</param>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isClosingConfirmed || !_viewModel.IsDirty)
        {
            return;
        }

        e.Cancel = true;
        _ = ConfirmAndCloseAsync();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel.ClearEncryptionKey();
    }

    private async Task ConfirmAndCloseAsync()
    {
        if (await _viewModel.ConfirmSaveIfDirtyAsync())
        {
            _isClosingConfirmed = true;
            Close();
        }
    }
}

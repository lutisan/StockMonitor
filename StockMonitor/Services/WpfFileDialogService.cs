using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace StockMonitor.Services;

/// <summary>
/// WPF implementation of <see cref="IFileDialogService"/>.
/// </summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    /// <inheritdoc />
    public string? ShowOpenXtbFileDialog()
    {
        OpenFileDialog dlg = new()
        {
            Title = "Import XTB Excel (.xlsx) or CSV Report",
            Filter = "XTB Excel & CSV Files (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|Excel Workbook (*.xlsx)|*.xlsx|CSV File (*.csv)|*.csv|All Files (*.*)|*.*"
        };

        Window? owner = Application.Current?.MainWindow;
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public string? ShowSaveExportFileDialog(string defaultFileName)
    {
        SaveFileDialog dlg = new()
        {
            Title = "Export Portfolio Report (.xlsx or .csv)",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx|CSV File (*.csv)|*.csv",
            FileName = defaultFileName
        };

        Window? owner = Application.Current?.MainWindow;
        return (owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog()) == true
            ? dlg.FileName
            : null;
    }

    /// <inheritdoc />
    public void ShowError(string title, string message)
    {
        Window? owner = Application.Current?.MainWindow;
        if (owner != null)
        {
            MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <inheritdoc />
    public void OpenUrlInExternalBrowser(string url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
    }
}

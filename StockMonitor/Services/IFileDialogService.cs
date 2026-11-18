namespace StockMonitor.Services;

/// <summary>
/// Abstraction for file dialogs, message prompts, and external process launching to keep ViewModels UI-decoupled.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Displays an open-file dialog for selecting an XTB Excel or CSV report.
    /// </summary>
    /// <returns>The selected file path, or <see langword="null"/> if cancelled.</returns>
    string? ShowOpenXtbFileDialog();

    /// <summary>
    /// Displays a save-file dialog for exporting the portfolio report to Excel or CSV.
    /// </summary>
    /// <param name="defaultFileName">Suggested default filename without extension.</param>
    /// <returns>The chosen output path, or <see langword="null"/> if cancelled.</returns>
    string? ShowSaveExportFileDialog(string defaultFileName);

    /// <summary>
    /// Displays an error message prompt to the user.
    /// </summary>
    /// <param name="title">Dialog window title.</param>
    /// <param name="message">Error description text.</param>
    void ShowError(string title, string message);

    /// <summary>
    /// Launches the specified URL in the operating system's default web browser.
    /// </summary>
    /// <param name="url">Target HTTP/HTTPS URL.</param>
    void OpenUrlInExternalBrowser(string url);
}

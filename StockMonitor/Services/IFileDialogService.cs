namespace StockMonitor.Services;

/// <summary>
/// User choice when prompted to save unsaved document changes before New, Open, or Exit.
/// </summary>
public enum UnsavedChangesChoice
{
    /// <summary>Save the document before continuing.</summary>
    Save,

    /// <summary>Discard unsaved changes and continue.</summary>
    Discard,

    /// <summary>Cancel the pending action and keep the current document open.</summary>
    Cancel
}

/// <summary>
/// User choice when importing an XTB Excel/CSV file into a non-empty portfolio document.
/// </summary>
public enum ImportModeChoice
{
    /// <summary>Remove all existing document content and load the imported file.</summary>
    ReplaceAll,

    /// <summary>Keep existing document content and add records from the imported file.</summary>
    Append,

    /// <summary>Cancel the import operation.</summary>
    Cancel
}

/// <summary>
/// Result returned from the Manage Document Password dialog.
/// </summary>
/// <param name="Confirmed">Whether the user confirmed a password change or removal.</param>
/// <param name="NewPassword">The new password to encrypt the document with, or <see langword="null"/> to remove encryption.</param>
public sealed record PasswordChangeResult(bool Confirmed, string? NewPassword);

/// <summary>
/// Abstraction for file dialogs, password prompts, message prompts, and external process launching to keep ViewModels UI-decoupled.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Displays an open-file dialog for selecting a StockMonitor portfolio JSON file.
    /// </summary>
    /// <param name="initialDirectory">Predefined starting folder for the dialog.</param>
    /// <returns>The selected file path, or <see langword="null"/> if cancelled.</returns>
    string? ShowOpenPortfolioFileDialog(string initialDirectory);

    /// <summary>
    /// Displays a save-file dialog for saving a StockMonitor portfolio JSON file.
    /// </summary>
    /// <param name="initialDirectory">Predefined starting folder for the dialog.</param>
    /// <param name="defaultFileName">Suggested default filename.</param>
    /// <returns>The chosen destination path, or <see langword="null"/> if cancelled.</returns>
    string? ShowSavePortfolioFileDialog(string initialDirectory, string defaultFileName);

    /// <summary>
    /// Prompts the user to save, discard, or cancel when unsaved changes exist.
    /// </summary>
    /// <param name="documentName">Display name of the current document.</param>
    UnsavedChangesChoice ShowUnsavedChangesPrompt(string documentName);

    /// <summary>
    /// Displays a modal dialog prompting for the password required to decrypt an encrypted portfolio file.
    /// </summary>
    /// <param name="fileName">Name of the encrypted file being opened.</param>
    /// <param name="isRetry">True when the previously entered password was incorrect.</param>
    /// <returns>The entered password, or <see langword="null"/> if the user cancelled.</returns>
    string? ShowEnterPasswordDialog(string fileName, bool isRetry = false);

    /// <summary>
    /// Displays a modal dialog allowing the user to set, change, or remove the encryption password for the current document.
    /// </summary>
    /// <param name="hasExistingPassword">True if the current document already has an encryption password set.</param>
    /// <param name="verifyCurrentPassword">Optional callback verifying the user's current password when <paramref name="hasExistingPassword"/> is true.</param>
    PasswordChangeResult ShowManagePasswordDialog(bool hasExistingPassword, Func<string, bool>? verifyCurrentPassword = null);

    /// <summary>
    /// Displays an open-file dialog for selecting an XTB Excel or CSV report.
    /// </summary>
    /// <returns>The selected file path, or <see langword="null"/> if cancelled.</returns>
    string? ShowOpenXtbFileDialog();

    /// <summary>
    /// Prompts the user to replace existing content, add to existing content, or cancel when importing into a non-empty document.
    /// </summary>
    /// <param name="fileName">Name of the selected import file.</param>
    ImportModeChoice ShowImportModeDialog(string fileName);

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

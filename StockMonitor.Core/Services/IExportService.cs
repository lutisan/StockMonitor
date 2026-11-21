using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Defines a pluggable portfolio export provider discovered at runtime from <c>*.export.dll</c>
/// assemblies or registered in dependency injection.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Gets a unique stable identifier for this export provider (for example, <c>"excel"</c> or <c>"csv"</c>).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the user-facing name shown under <c>File -&gt; Export</c>.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the WPF SaveFileDialog filter string (for example, <c>"Excel Workbook (*.xlsx)|*.xlsx"</c>).
    /// </summary>
    string FileDialogFilter { get; }

    /// <summary>
    /// Gets the default file extension including the leading dot (for example, <c>".xlsx"</c>).
    /// </summary>
    string DefaultExtension { get; }

    /// <summary>
    /// Exports the portfolio document and history snapshots to the specified output file path.
    /// </summary>
    /// <param name="doc">The portfolio document to export.</param>
    /// <param name="history">Ordered portfolio history snapshots.</param>
    /// <param name="filePath">Destination file path.</param>
    void ExportPortfolio(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath);
}

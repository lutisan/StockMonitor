using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Defines a pluggable broker/portfolio import provider discovered at runtime from <c>*.import.dll</c>
/// assemblies or registered in dependency injection.
/// </summary>
public interface IImportService
{
    /// <summary>
    /// Gets a unique stable identifier for this import provider (for example, <c>"xtb"</c>).
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the user-facing name shown under <c>File -&gt; Import</c>.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets the WPF OpenFileDialog filter string (for example, <c>"XTB Excel &amp; CSV Files (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv"</c>).
    /// </summary>
    string FileDialogFilter { get; }

    /// <summary>
    /// Imports positions, dividends, and cash flows from <paramref name="filePath"/> into <paramref name="document"/>.
    /// </summary>
    /// <param name="filePath">Path to the source broker report file.</param>
    /// <param name="document">Target portfolio document to populate or update.</param>
    /// <param name="replaceExistingBrokerData">When <see langword="true"/>, replaces previously imported broker records while preserving manual user entries and notes.</param>
    void ImportFile(string filePath, PortfolioDocument document, bool replaceExistingBrokerData = true);
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Stores user-configurable application settings including base currency, UI language, and visual theme.
/// Stored in a JSON file together with portfolio data and loaded at application startup.
/// </summary>
public partial class AppSettings : ObservableObject
{
    /// <summary>
    /// Backing field for <see cref="BaseCurrency"/> (e.g., "EUR", "USD", "CZK", "CHF", "GBP").
    /// </summary>
    [ObservableProperty]
    private string _baseCurrency = "EUR";

    /// <summary>
    /// Backing field for <see cref="Language"/> ("EN" for English, "SK" for Slovak).
    /// </summary>
    [ObservableProperty]
    private string _language = "EN";

    /// <summary>
    /// Backing field for <see cref="ThemeName"/> (Syncfusion skin name).
    /// </summary>
    [ObservableProperty]
    private string _themeName = "Windows11Dark";

    /// <summary>
    /// Backing field for <see cref="LastImportedXtbPath"/> storing the path of the most recently imported XTB report.
    /// </summary>
    [ObservableProperty]
    private string _lastImportedXtbPath = string.Empty;

    /// <summary>
    /// Backing field for <see cref="LastMarketSyncUtc"/> storing the UTC timestamp of the last market &amp; FX synchronization.
    /// </summary>
    [ObservableProperty]
    private DateTime? _lastMarketSyncUtc;
}

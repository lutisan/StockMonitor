using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Dynamic mapping entry for a broker ticker, persisted in JSON, auto-populated from XTB .xls/.xlsx/.csv imports,
/// and editable manually by the user.
/// </summary>
public partial class TickerMapping : ObservableObject
{
    /// <summary>
    /// Backing field for <see cref="Ticker"/> (broker ticker symbol, e.g. "AAPL.US", "LYPG.DE").
    /// </summary>
    [ObservableProperty]
    private string _ticker = string.Empty;

    /// <summary>
    /// Backing field for <see cref="CompanyName"/> (instrument or company display name).
    /// </summary>
    [ObservableProperty]
    private string _companyName = string.Empty;

    /// <summary>
    /// Backing field for <see cref="YahooSymbol"/> (Yahoo Finance symbol used for quotes and charts).
    /// </summary>
    [ObservableProperty]
    private string _yahooSymbol = string.Empty;

    /// <summary>
    /// Backing field for <see cref="CountryCode"/> (two-letter ISO country code of registration).
    /// </summary>
    [ObservableProperty]
    private string _countryCode = "US";

    /// <summary>
    /// Backing field for <see cref="Currency"/> (native trading currency of the instrument).
    /// </summary>
    [ObservableProperty]
    private string _currency = "USD";

    /// <summary>
    /// Backing field for <see cref="Category"/> ("STOCK" or "ETF").
    /// </summary>
    [ObservableProperty]
    private string _category = "STOCK";

    /// <summary>
    /// Backing field for <see cref="IsManualOverride"/> indicating whether manual edits are locked against import overwrites.
    /// </summary>
    [ObservableProperty]
    private bool _isManualOverride;
}

using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Defines the dividend withholding tax rule for a specific country of registration.
/// </summary>
public partial class CountryTaxRule : ObservableObject
{
    /// <summary>
    /// Backing field for <see cref="CountryCode"/> (ISO two-letter country code, e.g., "US", "DE").
    /// </summary>
    [ObservableProperty]
    private string _countryCode = string.Empty;

    /// <summary>
    /// Backing field for <see cref="CountryName"/> (full display name of the country).
    /// </summary>
    [ObservableProperty]
    private string _countryName = string.Empty;

    /// <summary>
    /// Backing field for <see cref="SampleTickers"/> (dynamically populated list of portfolio tickers for this country).
    /// </summary>
    [ObservableProperty]
    private string _sampleTickers = string.Empty;

    /// <summary>
    /// Backing field for <see cref="StandardRatePercent"/> (statutory withholding tax rate in percent).
    /// </summary>
    [ObservableProperty]
    private decimal _standardRatePercent;

    /// <summary>
    /// Backing field for <see cref="AppliedRatePercent"/> (effective/treaty withholding tax rate in percent).
    /// </summary>
    [ObservableProperty]
    private decimal _appliedRatePercent;

    /// <summary>
    /// Backing field for <see cref="Notes"/> (treaty details and tax notes).
    /// </summary>
    [ObservableProperty]
    private string _notes = string.Empty;
}

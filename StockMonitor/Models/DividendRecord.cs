using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Represents a dividend payment or spinoff cash distribution linked to a position lot or ticker.
/// </summary>
public partial class DividendRecord : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _externalCashOperationId = string.Empty;

    [ObservableProperty]
    private string _externalTaxOperationId = string.Empty;

    [ObservableProperty]
    private Guid? _positionLotId;

    [ObservableProperty]
    private string _externalPositionId = string.Empty;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _ticker = string.Empty;

    [ObservableProperty]
    private string _category = "STOCK";

    [ObservableProperty]
    private DateTime _payDate;

    [ObservableProperty]
    private decimal? _dividendPerShareNative;

    [ObservableProperty]
    private string _nativeCurrency = "USD";

    [ObservableProperty]
    private string _baseCurrency = "EUR";

    [ObservableProperty]
    private decimal _grossAmountEur;

    [ObservableProperty]
    private decimal _withholdingTaxEur;

    [ObservableProperty]
    private decimal _netAmountEur;

    [ObservableProperty]
    private decimal _grossAmountBase;

    [ObservableProperty]
    private decimal _withholdingTaxBase;

    [ObservableProperty]
    private decimal _netAmountBase;

    [ObservableProperty]
    private decimal _taxRatePercent = 15m;

    [ObservableProperty]
    private decimal _annualizedYieldPercent;

    [ObservableProperty]
    private bool _isSpinoff;

    [ObservableProperty]
    private bool _isTaxAdjustment;

    [ObservableProperty]
    private bool _isFromBrokerReport = true;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private bool _isPositionClosed;

    /// <summary>
    /// Gets a human-readable label indicating whether the dividend came from the broker report or Yahoo Finance API.
    /// </summary>
    public string SourceLabel => IsFromBrokerReport ? "XTB Report" : "Yahoo API";
}

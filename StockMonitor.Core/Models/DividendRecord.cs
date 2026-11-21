using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Represents a dividend payment or spinoff cash distribution linked to a position lot or ticker.
/// </summary>
public partial class DividendRecord : ObservableObject
{
    /// <summary>Gets or sets the unique internal identifier for this dividend record.</summary>
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    /// <summary>Gets or sets the broker cash operation identifier for the dividend credit.</summary>
    [ObservableProperty]
    private string _externalCashOperationId = string.Empty;

    /// <summary>Gets or sets the broker cash operation identifier for the paired withholding tax debit.</summary>
    [ObservableProperty]
    private string _externalTaxOperationId = string.Empty;

    /// <summary>Gets or sets the optional linked <see cref="PositionLot.Id"/> to which this dividend is allocated.</summary>
    [ObservableProperty]
    private Guid? _positionLotId;

    /// <summary>Gets or sets the broker external position ID of the linked lot.</summary>
    [ObservableProperty]
    private string _externalPositionId = string.Empty;

    /// <summary>Gets or sets the company or ETF display name.</summary>
    [ObservableProperty]
    private string _companyName = string.Empty;

    /// <summary>Gets or sets the broker ticker symbol (e.g. <c>AAPL.US</c>).</summary>
    [ObservableProperty]
    private string _ticker = string.Empty;

    /// <summary>Gets or sets the instrument category (<c>STOCK</c> or <c>ETF</c>).</summary>
    [ObservableProperty]
    private string _category = "STOCK";

    /// <summary>Gets or sets the UTC payment date of the dividend.</summary>
    [ObservableProperty]
    private DateTime _payDate;

    /// <summary>Gets or sets the gross dividend amount per share in the instrument's native currency.</summary>
    [ObservableProperty]
    private decimal? _dividendPerShareNative;

    /// <summary>Gets or sets the native currency in which the dividend was declared.</summary>
    [ObservableProperty]
    private string _nativeCurrency = "USD";

    /// <summary>Gets or sets the currently selected portfolio display/base currency.</summary>
    [ObservableProperty]
    private string _baseCurrency = "EUR";

    /// <summary>Gets or sets the gross dividend amount in EUR.</summary>
    [ObservableProperty]
    private decimal _grossAmountEur;

    /// <summary>Gets or sets the withholding tax amount in EUR.</summary>
    [ObservableProperty]
    private decimal _withholdingTaxEur;

    /// <summary>Gets or sets the net dividend amount received in EUR after withholding tax.</summary>
    [ObservableProperty]
    private decimal _netAmountEur;

    /// <summary>Gets or sets the gross dividend amount converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _grossAmountBase;

    /// <summary>Gets or sets the withholding tax amount converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _withholdingTaxBase;

    /// <summary>Gets or sets the net dividend amount converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _netAmountBase;

    /// <summary>Gets or sets the effective withholding tax rate percentage applied to this dividend.</summary>
    [ObservableProperty]
    private decimal _taxRatePercent = 15m;

    /// <summary>Gets or sets the estimated annualized dividend yield percentage relative to the linked lot cost.</summary>
    [ObservableProperty]
    private decimal _annualizedYieldPercent;

    /// <summary>Gets or sets a value indicating whether this record represents a corporate spinoff cash distribution.</summary>
    [ObservableProperty]
    private bool _isSpinoff;

    /// <summary>Gets or sets a value indicating whether this record is a standalone withholding tax adjustment entry.</summary>
    [ObservableProperty]
    private bool _isTaxAdjustment;

    /// <summary>Gets or sets a value indicating whether this dividend originated from an XTB broker report (<see langword="true"/>) or Yahoo Finance API (<see langword="false"/>).</summary>
    [ObservableProperty]
    private bool _isFromBrokerReport = true;

    /// <summary>Gets or sets the broker description or user comment for this dividend record.</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>Gets or sets a value indicating whether the associated position lot (or all lots for this ticker) is closed.</summary>
    [ObservableProperty]
    private bool _isPositionClosed;

    /// <summary>
    /// Gets a human-readable label indicating whether the dividend came from the broker report or Yahoo Finance API.
    /// </summary>
    public string SourceLabel => IsFromBrokerReport ? "XTB Report" : "Yahoo API";
}

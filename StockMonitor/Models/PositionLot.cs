using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Represents a single stock or ETF purchase lot (open or closed).
/// </summary>
public partial class PositionLot : ObservableObject
{
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    [ObservableProperty]
    private string _externalPositionId = string.Empty;

    [ObservableProperty]
    private List<string> _mergedPositionIds = [];

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _ticker = string.Empty;

    [ObservableProperty]
    private string _yahooSymbol = string.Empty;

    [ObservableProperty]
    private string _category = "STOCK";

    [ObservableProperty]
    private string _tradeType = "BUY";

    [ObservableProperty]
    private string _countryCode = "US";

    [ObservableProperty]
    private string _stockCurrency = "USD";

    [ObservableProperty]
    private string _baseCurrency = "EUR";

    [ObservableProperty]
    private decimal _quantity;

    [ObservableProperty]
    private DateTime _openDate = DateTime.UtcNow;

    [ObservableProperty]
    private DateTime? _closeDate;

    [ObservableProperty]
    private decimal _openPrice;

    [ObservableProperty]
    private bool _isOpenPriceManual;

    [ObservableProperty]
    private decimal? _openConversionRate;

    [ObservableProperty]
    private decimal? _brokerPurchaseValueEur;

    [ObservableProperty]
    private decimal? _closePrice;

    [ObservableProperty]
    private bool _isClosePriceManual;

    [ObservableProperty]
    private decimal? _closeConversionRate;

    [ObservableProperty]
    private decimal? _brokerSaleOrCurrentValueEur;

    [ObservableProperty]
    private decimal? _brokerNetProfitEur;

    [ObservableProperty]
    private decimal? _brokerGrossProfitEur;

    [ObservableProperty]
    private decimal _currentMarketPrice;

    [ObservableProperty]
    private decimal _openPriceBase;

    [ObservableProperty]
    private decimal _openCost;

    [ObservableProperty]
    private decimal _openCostBase;

    [ObservableProperty]
    private decimal _closeOrCurrentPrice;

    [ObservableProperty]
    private decimal _closeOrCurrentPriceBase;

    [ObservableProperty]
    private decimal _closeOrCurrentValue;

    [ObservableProperty]
    private decimal _closeOrCurrentValueBase;

    [ObservableProperty]
    private decimal _capitalProfitLoss;

    [ObservableProperty]
    private decimal _capitalProfitLossBase;

    [ObservableProperty]
    private decimal _capitalReturnPercent;

    [ObservableProperty]
    private decimal _grossDividends;

    [ObservableProperty]
    private decimal _withholdingTax;

    [ObservableProperty]
    private decimal _netDividends;

    [ObservableProperty]
    private decimal _netDividendsBase;

    [ObservableProperty]
    private decimal _spinoffProceedsBase;

    [ObservableProperty]
    private decimal _totalProfitBase;

    [ObservableProperty]
    private decimal _totalReturnPercent;

    [ObservableProperty]
    private decimal? _stopLoss;

    [ObservableProperty]
    private decimal? _takeProfit;

    [ObservableProperty]
    private decimal _openCommission;

    [ObservableProperty]
    private decimal _closeCommission;

    [ObservableProperty]
    private decimal _secFee;

    [ObservableProperty]
    private decimal _financialTransactionTax;

    [ObservableProperty]
    private decimal _swap;

    [ObservableProperty]
    private decimal _rollover;

    [ObservableProperty]
    private string _closeOrigin = string.Empty;

    [ObservableProperty]
    private string _product = "My Trades";

    [ObservableProperty]
    private decimal? _manualDividendTaxRate;

    [ObservableProperty]
    private decimal _effectiveDividendTaxRate = 15m;

    [ObservableProperty]
    private string _comment = string.Empty;

    [ObservableProperty]
    private DataSource _source = DataSource.XtbImport;

    /// <summary>Gets or sets the transient 12-point profit trend series rendered in the row sparkline column.</summary>
    [JsonIgnore]
    public List<SparklinePoint> SparklinePoints { get; set; } = [];

    /// <summary>Gets the computed status (<see cref="PositionStatus.Open"/> or <see cref="PositionStatus.Closed"/>).</summary>
    [JsonIgnore]
    public PositionStatus Status => CloseDate.HasValue ? PositionStatus.Closed : PositionStatus.Open;

    /// <summary>Gets the display status string (<c>"Open"</c> or <c>"Closed"</c>).</summary>
    [JsonIgnore]
    public string StatusText => CloseDate.HasValue ? "Closed" : "Open";

    /// <summary>Gets a value indicating whether this position lot is closed.</summary>
    [JsonIgnore]
    public bool IsClosed => CloseDate.HasValue;

    /// <summary>Gets a value indicating whether this position lot has a positive total profit in Base Currency.</summary>
    [JsonIgnore]
    public bool IsInProfit => TotalProfitBase > 0m;

    /// <summary>Gets a value indicating whether this position lot has a negative total profit in Base Currency.</summary>
    [JsonIgnore]
    public bool IsInLoss => TotalProfitBase < 0m;

    partial void OnCloseDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsClosed));
    }

    partial void OnTotalProfitBaseChanged(decimal value)
    {
        OnPropertyChanged(nameof(IsInProfit));
        OnPropertyChanged(nameof(IsInLoss));
    }
}

/// <summary>
/// Represents a single data point in the row sparkline chart.
/// </summary>
public sealed class SparklinePoint
{
    /// <summary>Gets or sets the checkpoint date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Gets or sets the estimated profit in Base Currency at <see cref="Date"/>.</summary>
    public double ProfitBase { get; set; }
}

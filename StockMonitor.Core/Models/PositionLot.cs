using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Models;

/// <summary>
/// Represents a single stock or ETF purchase lot (open or closed).
/// </summary>
public partial class PositionLot : ObservableObject
{
    /// <summary>Gets or sets the unique internal identifier for this position lot.</summary>
    [ObservableProperty]
    private Guid _id = Guid.NewGuid();

    /// <summary>Gets or sets the broker position ID (e.g. XTB Position ID or generated <c>MAN-*</c> ID).</summary>
    [ObservableProperty]
    private string _externalPositionId = string.Empty;

    /// <summary>Gets or sets the list of merged broker position IDs when partial fills were consolidated.</summary>
    [ObservableProperty]
    private List<string> _mergedPositionIds = [];

    /// <summary>Gets or sets the company or ETF display name.</summary>
    [ObservableProperty]
    private string _companyName = string.Empty;

    /// <summary>Gets or sets the broker ticker symbol (e.g. <c>AAPL.US</c>, <c>LYPG.DE</c>).</summary>
    [ObservableProperty]
    private string _ticker = string.Empty;

    /// <summary>Gets or sets the resolved Yahoo Finance ticker symbol (e.g. <c>AAPL</c>, <c>CEZ.PR</c>).</summary>
    [ObservableProperty]
    private string _yahooSymbol = string.Empty;

    /// <summary>Gets or sets the instrument category (<c>STOCK</c> or <c>ETF</c>).</summary>
    [ObservableProperty]
    private string _category = "STOCK";

    /// <summary>Gets or sets the trade side/direction (typically <c>BUY</c>).</summary>
    [ObservableProperty]
    private string _tradeType = "BUY";

    /// <summary>Gets or sets the two-letter ISO country code of issuer domicile used for dividend tax rules.</summary>
    [ObservableProperty]
    private string _countryCode = "US";

    /// <summary>Gets or sets the native trading currency of the instrument (e.g. <c>USD</c>, <c>EUR</c>, <c>CZK</c>).</summary>
    [ObservableProperty]
    private string _stockCurrency = "USD";

    /// <summary>Gets or sets the currently selected portfolio base/display currency.</summary>
    [ObservableProperty]
    private string _baseCurrency = "EUR";

    /// <summary>Gets or sets the number of shares or ETF units in this lot.</summary>
    [ObservableProperty]
    private decimal _quantity;

    /// <summary>Gets or sets the UTC date and time when this position lot was opened.</summary>
    [ObservableProperty]
    private DateTime _openDate = DateTime.UtcNow;

    /// <summary>Gets or sets the UTC close date and time, or <see langword="null"/> if the position is still open.</summary>
    [ObservableProperty]
    private DateTime? _closeDate;

    /// <summary>Gets or sets the purchase price per share in <see cref="StockCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openPrice;

    /// <summary>Gets or sets a value indicating whether <see cref="OpenPrice"/> was manually locked by the user.</summary>
    [ObservableProperty]
    private bool _isOpenPriceManual;

    /// <summary>Gets or sets the broker-reported FX conversion rate to EUR at open time, if available.</summary>
    [ObservableProperty]
    private decimal? _openConversionRate;

    /// <summary>Gets or sets the exact broker-reported purchase cost in EUR from an XTB report.</summary>
    [ObservableProperty]
    private decimal? _brokerPurchaseValueEur;

    /// <summary>Gets or sets the executed closing price per share in <see cref="StockCurrency"/> for closed lots.</summary>
    [ObservableProperty]
    private decimal? _closePrice;

    /// <summary>Gets or sets a value indicating whether the current/close price was manually locked by the user.</summary>
    [ObservableProperty]
    private bool _isClosePriceManual;

    /// <summary>Gets or sets the broker-reported FX conversion rate to EUR at close/report time, if available.</summary>
    [ObservableProperty]
    private decimal? _closeConversionRate;

    /// <summary>Gets or sets the broker-reported sale value (closed) or report-time market value (open) in EUR.</summary>
    [ObservableProperty]
    private decimal? _brokerSaleOrCurrentValueEur;

    /// <summary>Gets or sets the broker-reported net profit in EUR.</summary>
    [ObservableProperty]
    private decimal? _brokerNetProfitEur;

    /// <summary>Gets or sets the broker-reported gross profit in EUR.</summary>
    [ObservableProperty]
    private decimal? _brokerGrossProfitEur;

    /// <summary>Gets or sets the latest market price per share in <see cref="StockCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _currentMarketPrice;

    /// <summary>Gets or sets the open price per share converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openPriceBase;

    /// <summary>Gets or sets the total purchase cost in <see cref="StockCurrency"/> including open commission.</summary>
    [ObservableProperty]
    private decimal _openCost;

    /// <summary>Gets or sets the total purchase cost converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openCostBase;

    /// <summary>Gets or sets the active close price (if closed) or current market price (if open) in <see cref="StockCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _closeOrCurrentPrice;

    /// <summary>Gets or sets the active close or current market price per share converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _closeOrCurrentPriceBase;

    /// <summary>Gets or sets the total close or current market value in <see cref="StockCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _closeOrCurrentValue;

    /// <summary>Gets or sets the total close or current market value converted to <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _closeOrCurrentValueBase;

    /// <summary>Gets or sets the capital profit or loss in <see cref="StockCurrency"/> (excluding dividends).</summary>
    [ObservableProperty]
    private decimal _capitalProfitLoss;

    /// <summary>Gets or sets the capital profit or loss in <see cref="BaseCurrency"/> (excluding dividends).</summary>
    [ObservableProperty]
    private decimal _capitalProfitLossBase;

    /// <summary>Gets or sets the capital return percentage relative to <see cref="OpenCostBase"/>.</summary>
    [ObservableProperty]
    private decimal _capitalReturnPercent;

    /// <summary>Gets or sets the cumulative gross dividends allocated to this lot in <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _grossDividends;

    /// <summary>Gets or sets the cumulative dividend withholding tax allocated to this lot in <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _withholdingTax;

    /// <summary>Gets or sets the cumulative net dividends allocated to this lot in <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _netDividends;

    /// <summary>Gets or sets the cumulative net dividends in <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _netDividendsBase;

    /// <summary>Gets or sets any corporate spinoff cash proceeds allocated to this lot in EUR.</summary>
    [ObservableProperty]
    private decimal _spinoffProceedsBase;

    /// <summary>Gets or sets the total combined profit (capital P/L + net dividends + spinoff proceeds) in <see cref="BaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalProfitBase;

    /// <summary>Gets or sets the total combined return percentage relative to <see cref="OpenCostBase"/>.</summary>
    [ObservableProperty]
    private decimal _totalReturnPercent;

    /// <summary>Gets or sets the optional stop-loss trigger price.</summary>
    [ObservableProperty]
    private decimal? _stopLoss;

    /// <summary>Gets or sets the optional take-profit target price.</summary>
    [ObservableProperty]
    private decimal? _takeProfit;

    /// <summary>Gets or sets the broker commission paid when opening this lot.</summary>
    [ObservableProperty]
    private decimal _openCommission;

    /// <summary>Gets or sets the broker commission paid when closing this lot.</summary>
    [ObservableProperty]
    private decimal _closeCommission;

    /// <summary>Gets or sets any US SEC regulatory fee charged when selling this lot.</summary>
    [ObservableProperty]
    private decimal _secFee;

    /// <summary>Gets or sets any European financial transaction tax (FTT) charged when buying this lot.</summary>
    [ObservableProperty]
    private decimal _financialTransactionTax;

    /// <summary>Gets or sets accumulated overnight swap adjustments.</summary>
    [ObservableProperty]
    private decimal _swap;

    /// <summary>Gets or sets accumulated rollover adjustments.</summary>
    [ObservableProperty]
    private decimal _rollover;

    /// <summary>Gets or sets the broker platform/origin from which the position was closed (e.g. <c>xStation5</c>, <c>Android</c>).</summary>
    [ObservableProperty]
    private string _closeOrigin = string.Empty;

    /// <summary>Gets or sets the broker account product label (e.g. <c>My Trades</c>).</summary>
    [ObservableProperty]
    private string _product = "My Trades";

    /// <summary>Gets or sets an optional user-specified dividend withholding tax percentage override for this lot.</summary>
    [ObservableProperty]
    private decimal? _manualDividendTaxRate;

    /// <summary>Gets or sets the effective dividend withholding tax percentage applied to this lot.</summary>
    [ObservableProperty]
    private decimal _effectiveDividendTaxRate = 15m;

    /// <summary>Gets or sets user notes or broker comments stored with this lot.</summary>
    [ObservableProperty]
    private string _comment = string.Empty;

    /// <summary>Gets or sets the data origin of this lot (<see cref="DataSource.XtbImport"/> or <see cref="DataSource.ManualEntry"/>).</summary>
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

    /// <summary>
    /// Gets or sets the editable Open Price in the lot details sidebar.
    /// Entering a positive value locks <see cref="IsOpenPriceManual"/>; clearing it (null) unlocks it so online data is fetched.
    /// </summary>
    [JsonIgnore]
    public decimal? EditableOpenPrice
    {
        get => OpenPrice > 0m ? OpenPrice : null;
        set
        {
            if (value is > 0m)
            {
                if (OpenPrice != value.Value || !IsOpenPriceManual)
                {
                    OpenPrice = value.Value;
                    IsOpenPriceManual = true;
                    BrokerPurchaseValueEur = null;
                    OpenConversionRate = null;
                }
            }
            else
            {
                OpenPrice = 0m;
                IsOpenPriceManual = false;
                BrokerPurchaseValueEur = null;
                OpenConversionRate = null;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets the editable Current (if open) or Close (if closed) Price in the lot details sidebar.
    /// Entering a positive value locks <see cref="IsClosePriceManual"/>; clearing it (null) unlocks it so online data is fetched.
    /// </summary>
    [JsonIgnore]
    public decimal? EditableCloseOrCurrentPrice
    {
        get
        {
            decimal? active = CloseDate.HasValue ? ClosePrice : (CurrentMarketPrice > 0m ? CurrentMarketPrice : null);
            return active is > 0m ? active : null;
        }
        set
        {
            if (value is > 0m)
            {
                IsClosePriceManual = true;
                BrokerSaleOrCurrentValueEur = null;
                CloseConversionRate = null;
                CurrentMarketPrice = value.Value;
                if (CloseDate.HasValue)
                {
                    ClosePrice = value.Value;
                }

                CloseOrCurrentPrice = value.Value;
            }
            else
            {
                IsClosePriceManual = false;
                ClosePrice = null;
                CurrentMarketPrice = 0m;
                CloseOrCurrentPrice = 0m;
                BrokerSaleOrCurrentValueEur = null;
                CloseConversionRate = null;
            }

            OnPropertyChanged();
        }
    }

    partial void OnOpenPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(EditableOpenPrice));
    }

    partial void OnClosePriceChanged(decimal? value)
    {
        OnPropertyChanged(nameof(EditableCloseOrCurrentPrice));
    }

    partial void OnCurrentMarketPriceChanged(decimal value)
    {
        OnPropertyChanged(nameof(EditableCloseOrCurrentPrice));
    }

    partial void OnCloseDateChanged(DateTime? value)
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(IsClosed));
        OnPropertyChanged(nameof(EditableCloseOrCurrentPrice));
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

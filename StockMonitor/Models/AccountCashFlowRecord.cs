namespace StockMonitor.Models;

/// <summary>
/// Represents a broker cash operation (deposit, interest, tax, fee, purchase, sale).
/// </summary>
public sealed class AccountCashFlowRecord
{
    /// <summary>Gets or sets the broker cash operation identifier.</summary>
    public string OperationId { get; set; } = string.Empty;

    /// <summary>Gets or sets the categorized cash operation type.</summary>
    public CashOperationType OperationType { get; set; }

    /// <summary>Gets or sets the raw operation type string from the broker report.</summary>
    public string RawType { get; set; } = string.Empty;

    /// <summary>Gets or sets the associated company or ETF name.</summary>
    public string Instrument { get; set; } = string.Empty;

    /// <summary>Gets or sets the associated broker ticker symbol.</summary>
    public string Ticker { get; set; } = string.Empty;

    /// <summary>Gets or sets the instrument category (<c>STOCK</c> or <c>ETF</c>).</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC timestamp of the cash operation.</summary>
    public DateTime TimeUtc { get; set; }

    /// <summary>Gets or sets the operation amount in the broker account currency (EUR).</summary>
    public decimal AmountEur { get; set; }

    /// <summary>Gets or sets the operation amount converted to the selected Base Currency.</summary>
    public decimal AmountBase { get; set; }

    /// <summary>Gets or sets the selected Base Currency code for <see cref="AmountBase"/>.</summary>
    public string BaseCurrency { get; set; } = "EUR";

    /// <summary>Gets or sets the broker comment attached to the operation.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>Gets or sets the broker product name (e.g., <c>My Trades</c>).</summary>
    public string Product { get; set; } = string.Empty;

    /// <summary>Gets or sets the linked broker position identifier, if applicable.</summary>
    public string PositionId { get; set; } = string.Empty;
}

/// <summary>
/// Represents a historical snapshot row for the Portfolio History tab.
/// </summary>
public sealed class PortfolioHistoryPoint
{
    /// <summary>Gets or sets the snapshot date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Gets the formatted ISO date string (<c>yyyy-MM-dd</c>).</summary>
    public string PeriodLabel => Date.ToString("yyyy-MM-dd");

    /// <summary>Gets or sets cumulative cash deposits up to <see cref="Date"/> in Base Currency.</summary>
    public decimal CumulativeDepositsBase { get; set; }

    /// <summary>Gets or sets active invested cost of open lots on <see cref="Date"/> in Base Currency.</summary>
    public decimal CumulativeInvestedBase { get; set; }

    /// <summary>Gets or sets estimated market value of open holdings on <see cref="Date"/> in Base Currency.</summary>
    public decimal HoldingsMarketValueBase { get; set; }

    /// <summary>Gets or sets cumulative realized capital profit/loss from closed lots up to <see cref="Date"/> in Base Currency.</summary>
    public decimal RealizedCapitalProfitBase { get; set; }

    /// <summary>Gets or sets cumulative net dividends received up to <see cref="Date"/> in Base Currency.</summary>
    public decimal CumulativeNetDividendsBase { get; set; }

    /// <summary>Gets or sets cumulative net free-funds interest received up to <see cref="Date"/> in Base Currency.</summary>
    public decimal CumulativeNetInterestBase { get; set; }

    /// <summary>Gets or sets total portfolio valuation on <see cref="Date"/> in Base Currency.</summary>
    public decimal TotalPortfolioValueBase { get; set; }

    /// <summary>Gets or sets total profit on <see cref="Date"/> in Base Currency.</summary>
    public decimal TotalProfitBase { get; set; }

    /// <summary>Gets or sets the selected Base Currency code for this snapshot.</summary>
    public string BaseCurrency { get; set; } = "EUR";

    /// <summary>Gets the percentage return relative to <see cref="CumulativeInvestedBase"/>.</summary>
    public decimal TotalReturnPercent => CumulativeInvestedBase > 0
        ? Math.Round(TotalProfitBase / CumulativeInvestedBase * 100m, 2)
        : 0m;
}

/// <summary>
/// Represents a single OHLCV candle or daily price point for stock charts.
/// </summary>
public sealed class StockPricePoint
{
    /// <summary>Gets or sets the trading date.</summary>
    public DateTime Date { get; set; }

    /// <summary>Gets or sets the opening price.</summary>
    public double Open { get; set; }

    /// <summary>Gets or sets the intraday high price.</summary>
    public double High { get; set; }

    /// <summary>Gets or sets the intraday low price.</summary>
    public double Low { get; set; }

    /// <summary>Gets or sets the closing price.</summary>
    public double Close { get; set; }

    /// <summary>Gets or sets the lot purchase reference price.</summary>
    public double PurchaseReferencePrice { get; set; }

    /// <summary>Gets or sets the daily trading volume.</summary>
    public long Volume { get; set; }
}

/// <summary>
/// Represents a category allocation slice for dashboard doughnut and bar charts.
/// </summary>
public sealed class AllocationSlice
{
    /// <summary>Gets or sets the slice label (company, country, or year).</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Gets or sets the monetary value in Base Currency.</summary>
    public double ValueBase { get; set; }

    /// <summary>Gets or sets the percentage share or secondary series value.</summary>
    public double SharePercent { get; set; }
}

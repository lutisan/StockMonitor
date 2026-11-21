namespace StockMonitor.Models;

/// <summary>
/// Identifies the origin of a position lot or dividend record.
/// </summary>
public enum DataSource
{
    /// <summary>
    /// Imported from an XTB broker report (.xlsx, .xls, or .csv).
    /// </summary>
    XtbImport,

    /// <summary>
    /// Imported from a legacy Google Sheet workbook.
    /// </summary>
    GoogleSheetImport,

    /// <summary>
    /// Created or entered manually by the user.
    /// </summary>
    ManualEntry,

    /// <summary>
    /// Retrieved from the Yahoo Finance market data API.
    /// </summary>
    YahooFinanceApi
}

/// <summary>
/// Represents the status of a stock or ETF position lot.
/// </summary>
public enum PositionStatus
{
    /// <summary>
    /// The position lot is currently open and held in the portfolio.
    /// </summary>
    Open,

    /// <summary>
    /// The position lot has been sold and closed.
    /// </summary>
    Closed
}

/// <summary>
/// Filter mode for displaying position lots in the main grid.
/// </summary>
public enum PositionFilterMode
{
    /// <summary>
    /// Show both open and closed position lots.
    /// </summary>
    All,

    /// <summary>
    /// Show only currently open position lots.
    /// </summary>
    OpenOnly,

    /// <summary>
    /// Show only closed (sold) position lots.
    /// </summary>
    ClosedOnly
}

/// <summary>
/// Categorizes broker cash operations from the XTB Cash Operations report.
/// </summary>
public enum CashOperationType
{
    /// <summary>
    /// Cash deposit into the brokerage account.
    /// </summary>
    Deposit,

    /// <summary>
    /// Cash withdrawal from the brokerage account.
    /// </summary>
    Withdrawal,

    /// <summary>
    /// Interest credited on uninvested free funds.
    /// </summary>
    FreeFundsInterest,

    /// <summary>
    /// Withholding tax deducted from free-funds interest.
    /// </summary>
    FreeFundsInterestTax,

    /// <summary>
    /// Gross cash dividend payment credited to the account.
    /// </summary>
    Dividend,

    /// <summary>
    /// Withholding tax deducted at source from a dividend payment.
    /// </summary>
    WithholdingTax,

    /// <summary>
    /// Cash debit for purchasing stock or ETF shares.
    /// </summary>
    StockPurchase,

    /// <summary>
    /// Cash credit from selling stock or ETF shares.
    /// </summary>
    StockSale,

    /// <summary>
    /// Broker trading commission.
    /// </summary>
    Commission,

    /// <summary>
    /// Regulatory SEC transaction fee.
    /// </summary>
    SecFee,

    /// <summary>
    /// Financial Transaction Tax (such as French IFTT).
    /// </summary>
    FinancialTransactionTax,

    /// <summary>
    /// Cash proceeds from a corporate spinoff distribution.
    /// </summary>
    Spinoff,

    /// <summary>
    /// Uncategorized or miscellaneous cash operation.
    /// </summary>
    Other
}

namespace StockMonitor.Models;

/// <summary>
/// Root persisted JSON document for the StockMonitor application.
/// </summary>
public sealed class PortfolioDocument
{
    /// <summary>
    /// Gets or sets the broker account number.
    /// </summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC timestamp when the imported broker report was generated.
    /// </summary>
    public DateTime ReportGeneratedUtc { get; set; } = new(2026, 10, 22, 18, 51, 11, DateTimeKind.Utc);

    /// <summary>
    /// Gets or sets the persisted user settings (base currency, language, theme).
    /// </summary>
    public AppSettings Settings { get; set; } = new();

    /// <summary>
    /// Gets or sets the dynamic list of ticker-to-Yahoo/country/currency mappings.
    /// </summary>
    public List<TickerMapping> TickerMappings { get; set; } = [];

    /// <summary>
    /// Gets or sets the editable dividend withholding tax rules by country.
    /// </summary>
    public List<CountryTaxRule> TaxRules { get; set; } = [];

    /// <summary>
    /// Gets or sets all open and closed position lots.
    /// </summary>
    public List<PositionLot> Lots { get; set; } = [];

    /// <summary>
    /// Gets or sets all normalized dividend and withholding tax records.
    /// </summary>
    public List<DividendRecord> Dividends { get; set; } = [];

    /// <summary>
    /// Gets or sets all imported broker cash operations.
    /// </summary>
    public List<AccountCashFlowRecord> CashFlows { get; set; } = [];
}

using System.Net.Http;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="PortfolioCalculationService"/> verifying native and base-currency valuation,
/// XTB vs manual override precedence, dynamic Yahoo dividend tax/FX calculation, and portfolio history through today.
/// </summary>
public sealed class PortfolioCalculationServiceTests
{
    /// <summary>
    /// Helper that creates a <see cref="FrankfurterFxRateProvider"/> pre-loaded with deterministic EUR FX rates.
    /// </summary>
    private static async Task<FrankfurterFxRateProvider> CreateSeededFxProviderAsync(TempTestDirectory tempDir)
    {
        StubHttpMessageHandler handler = new(req =>
        {
            string url = req.RequestUri!.ToString();
            if (url.Contains("/v1/latest"))
            {
                return StubHttpMessageHandler.JsonOk("""
                {
                  "base": "EUR",
                  "date": "2025-11-18",
                  "rates": { "USD": 1.25, "CZK": 25.00 }
                }
                """);
            }

            return StubHttpMessageHandler.JsonOk("""
            {
              "base": "EUR",
              "start_date": "2025-01-01",
              "end_date": "2025-11-18",
              "rates": {
                "2025-01-15": { "USD": 1.00, "CZK": 25.00 },
                "2025-06-15": { "USD": 1.20, "CZK": 24.00 }
              }
            }
            """);
        });

        HttpClient http = new(handler);
        FrankfurterFxRateProvider fx = new(http, tempDir.FilePath("fx.json"));
        PortfolioDocument seedDoc = new()
        {
            Lots = [new PositionLot { OpenDate = new DateTime(2025, 1, 15), CloseDate = new DateTime(2025, 6, 15) }]
        };
        await fx.EnsurePortfolioRatesAsync(seedDoc);
        return fx;
    }

    /// <summary>
    /// Verifies that <see cref="PortfolioCalculationService.RecalculateAll"/> computes native cost,
    /// market value, EUR/BaseCurrency conversions, capital profit, return %, and sparkline points accurately.
    /// </summary>
    [Fact]
    public async Task RecalculateAll_ComputesMultiCurrencyOpenLotMetricsAndSparkline()
    {
        using TempTestDirectory tempDir = new();
        FrankfurterFxRateProvider fx = await CreateSeededFxProviderAsync(tempDir);
        PortfolioCalculationService calc = new(fx);

        // Arrange: 10 shares bought at $100 on 2025-01-15 (USD/EUR=1.00 -> €1000)
        // Currently trading at $150 (latest EUR/USD=1.25 -> $1500 / 1.25 = €1200)
        PositionLot lot = new()
        {
            ExternalPositionId = "MAN-1",
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            CountryCode = "US",
            StockCurrency = "USD",
            Quantity = 10m,
            OpenDate = new DateTime(2025, 1, 15),
            OpenPrice = 100m,
            CurrentMarketPrice = 150m
        };

        PortfolioDocument doc = new()
        {
            Settings = new AppSettings { BaseCurrency = "EUR" },
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots = [lot]
        };

        // Act
        calc.RecalculateAll(doc);

        // Assert
        Assert.Equal("EUR", lot.BaseCurrency);
        Assert.Equal(1000m, lot.OpenCost);
        Assert.Equal(1500m, lot.CloseOrCurrentValue);
        Assert.Equal(500m, lot.CapitalProfitLoss);
        Assert.Equal(1000m, lot.OpenCostBase);
        Assert.Equal(1200m, lot.CloseOrCurrentValueBase);
        Assert.Equal(200m, lot.CapitalProfitLossBase);
        Assert.Equal(20m, lot.CapitalReturnPercent);
        Assert.Equal(200m, lot.TotalProfitBase);
        Assert.Equal(12, lot.SparklinePoints.Count);
    }

    /// <summary>
    /// Verifies that Yahoo-fetched dividends (<see cref="DividendRecord.IsFromBrokerReport"/> = false)
    /// are dynamically valued into EUR and BaseCurrency using the exact PayDate FX rate and
    /// respect lot-level <see cref="PositionLot.ManualDividendTaxRate"/> overrides.
    /// </summary>
    [Fact]
    public async Task RecalculateAll_CalculatesYahooDividendAmountsWithPayDateFxAndTaxRules()
    {
        using TempTestDirectory tempDir = new();
        FrankfurterFxRateProvider fx = await CreateSeededFxProviderAsync(tempDir);
        PortfolioCalculationService calc = new(fx);

        // Arrange: closed lot with 20 shares and a 10% manual tax override
        PositionLot lot = new()
        {
            CompanyName = "Microsoft",
            Ticker = "MSFT.US",
            CountryCode = "US",
            StockCurrency = "USD",
            Quantity = 20m,
            OpenDate = new DateTime(2025, 1, 15),
            CloseDate = new DateTime(2025, 6, 15),
            OpenPrice = 100m,
            ClosePrice = 120m,
            ManualDividendTaxRate = 10m
        };

        // Yahoo dividend: $3.00/share on 2025-06-15 -> $60 gross; EUR/USD=1.20 -> €50 gross; 10% WHT = €5; €45 net
        DividendRecord yahooDiv = new()
        {
            PositionLotId = lot.Id,
            Ticker = "MSFT.US",
            CompanyName = "Microsoft",
            PayDate = new DateTime(2025, 6, 15),
            NativeCurrency = "USD",
            DividendPerShareNative = 3.00m,
            IsFromBrokerReport = false
        };

        PortfolioDocument doc = new()
        {
            Settings = new AppSettings { BaseCurrency = "EUR" },
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots = [lot],
            Dividends = [yahooDiv]
        };

        // Act
        calc.RecalculateAll(doc);

        // Assert: dividend calculated dynamically and marked closed because linked lot is closed
        Assert.True(yahooDiv.IsPositionClosed);
        Assert.Equal(10m, yahooDiv.TaxRatePercent);
        Assert.Equal(50m, yahooDiv.GrossAmountEur);
        Assert.Equal(5m, yahooDiv.WithholdingTaxEur);
        Assert.Equal(45m, yahooDiv.NetAmountEur);

        // Assert: lot aggregates the net dividend into TotalProfitBase
        Assert.Equal(50m, lot.GrossDividends);
        Assert.Equal(5m, lot.WithholdingTax);
        Assert.Equal(45m, lot.NetDividendsBase);
        Assert.Equal(lot.CapitalProfitLossBase + 45m, lot.TotalProfitBase);
    }

    /// <summary>
    /// Verifies that XTB broker purchase/sale EUR values take precedence when prices are NOT manually overridden,
    /// and are ignored in favor of price × FX when <see cref="PositionLot.IsOpenPriceManual"/> is true.
    /// </summary>
    [Fact]
    public async Task RecalculateAll_RespectsBrokerSnapshotUnlessManualOverrideIsSet()
    {
        using TempTestDirectory tempDir = new();
        FrankfurterFxRateProvider fx = await CreateSeededFxProviderAsync(tempDir);
        PortfolioCalculationService calc = new(fx);

        // Arrange: XTB lot with exact broker purchase EUR value (€987.65)
        PositionLot lot = new()
        {
            Ticker = "AAPL.US",
            CountryCode = "US",
            StockCurrency = "USD",
            Quantity = 10m,
            OpenDate = new DateTime(2025, 1, 15),
            OpenPrice = 100m,
            CurrentMarketPrice = 100m,
            BrokerPurchaseValueEur = 987.65m,
            IsOpenPriceManual = false
        };

        PortfolioDocument doc = new()
        {
            Settings = new AppSettings { BaseCurrency = "EUR" },
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots = [lot]
        };

        // Act 1: not manual -> uses exact broker purchase EUR value
        calc.RecalculateAll(doc);
        Assert.Equal(987.65m, lot.OpenCostBase);

        // Act 2: mark manual -> ignores broker snapshot and uses OpenPrice * Qty * FX (100 * 10 * 1.00 = 1000)
        lot.IsOpenPriceManual = true;
        calc.RecalculateAll(doc);
        Assert.Equal(1000m, lot.OpenCostBase);
    }

    /// <summary>
    /// Verifies that <see cref="PortfolioCalculationService.BuildPortfolioHistory"/> extends the timeline
    /// through today (<see cref="DateTime.UtcNow"/>) even when <see cref="PortfolioDocument.ReportGeneratedUtc"/>
    /// is from an older XTB export date.
    /// </summary>
    [Fact]
    public async Task BuildPortfolioHistory_ExtendsTimelineThroughTodayWhenReportDateIsInPast()
    {
        using TempTestDirectory tempDir = new();
        FrankfurterFxRateProvider fx = await CreateSeededFxProviderAsync(tempDir);
        PortfolioCalculationService calc = new(fx);

        PositionLot lot = new()
        {
            Ticker = "SXR8.DE",
            CountryCode = "IE",
            StockCurrency = "EUR",
            Quantity = 2m,
            OpenDate = new DateTime(2025, 1, 15),
            OpenPrice = 500m,
            CurrentMarketPrice = 600m
        };

        PortfolioDocument doc = new()
        {
            ReportGeneratedUtc = new DateTime(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            Settings = new AppSettings { BaseCurrency = "EUR" },
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots = [lot]
        };

        calc.RecalculateAll(doc);

        // Act
        List<PortfolioHistoryPoint> history = calc.BuildPortfolioHistory(doc);

        // Assert: final checkpoint reaches today (UTC) and reflects full unrealized gain (€200)
        Assert.NotEmpty(history);
        Assert.Equal(DateTime.UtcNow.Date, history[^1].Date);
        Assert.Equal(1000m, history[^1].CumulativeInvestedBase);
        Assert.Equal(1200m, history[^1].HoldingsMarketValueBase);
        Assert.Equal(200m, history[^1].TotalProfitBase);
    }

    /// <summary>
    /// Verifies that updating a country's <see cref="CountryTaxRule.AppliedRatePercent"/> recalculates
    /// withholding tax and net dividend amounts for broker-imported dividends as well.
    /// </summary>
    [Fact]
    public async Task RecalculateAll_UpdatesBrokerDividendTaxes_WhenCountryTaxRuleChanges()
    {
        using TempTestDirectory tempDir = new();
        FrankfurterFxRateProvider fx = await CreateSeededFxProviderAsync(tempDir);
        PortfolioCalculationService calc = new(fx);

        PositionLot lot = new()
        {
            ExternalPositionId = "XTB-1",
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            CountryCode = "US",
            StockCurrency = "USD",
            Quantity = 10m,
            OpenDate = new DateTime(2025, 1, 15),
            OpenPrice = 100m,
            CurrentMarketPrice = 120m,
            Source = DataSource.XtbImport
        };

        DividendRecord brokerDiv = new()
        {
            PositionLotId = lot.Id,
            Ticker = "AAPL.US",
            PayDate = new DateTime(2025, 6, 15),
            GrossAmountEur = 100m,
            WithholdingTaxEur = 15m,
            NetAmountEur = 85m,
            TaxRatePercent = 15m,
            IsFromBrokerReport = true
        };

        List<CountryTaxRule> taxRules = TickerMappingHelper.CreateDefaultTaxRules();
        taxRules.Single(r => r.CountryCode == "US").AppliedRatePercent = 30m;

        PortfolioDocument doc = new()
        {
            Settings = new AppSettings { BaseCurrency = "EUR" },
            TaxRules = taxRules,
            Lots = [lot],
            Dividends = [brokerDiv]
        };

        calc.RecalculateAll(doc);

        Assert.Equal(30m, brokerDiv.TaxRatePercent);
        Assert.Equal(30m, brokerDiv.WithholdingTaxEur);
        Assert.Equal(70m, brokerDiv.NetAmountEur);
        Assert.Equal(30m, brokerDiv.WithholdingTaxBase);
        Assert.Equal(70m, brokerDiv.NetAmountBase);
        Assert.Equal(70m, lot.NetDividendsBase);
    }
}

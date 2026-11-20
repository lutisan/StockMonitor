using System.Net.Http;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="YahooFinanceMarketDataProvider"/> verifying the 3-tier price &amp; dividend model:
/// automatic historical/live price filling, manual lock protection, GBp pence normalization,
/// and post-XTB Yahoo dividend appending with deduplication.
/// </summary>
public sealed class YahooFinanceMarketDataProviderTests
{
    /// <summary>
    /// Verifies that a manually added lot with ONLY Name, Ticker, OpenDate, and Quantity (blank prices)
    /// automatically receives its historical OpenPrice, live CurrentMarketPrice, and holding-period
    /// Yahoo Finance dividend events.
    /// </summary>
    [Fact]
    public async Task PopulateMissingPricesAndDividendsAsync_ManualLotWithBlankPrices_FetchesOpenLiveAndDividends()
    {
        using TempTestDirectory tempDir = new();

        DateTime openDate = new(2025, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        DateTime divDate = new(2025, 5, 15, 0, 0, 0, DateTimeKind.Utc);
        long openUnix = new DateTimeOffset(openDate).ToUnixTimeSeconds();
        long divUnix = new DateTimeOffset(divDate).ToUnixTimeSeconds();

        // Arrange: stub Yahoo Finance v8 chart responses for quote, historical price, and events=div
        StubHttpMessageHandler handler = new(req =>
        {
            string url = req.RequestUri!.ToString();

            if (url.Contains("events=div"))
            {
                return StubHttpMessageHandler.JsonOk($$"""
                {
                  "chart": {
                    "result": [{
                      "meta": { "currency": "USD", "regularMarketPrice": 235.50 },
                      "events": {
                        "dividends": {
                          "{{divUnix}}": { "amount": 0.25, "date": {{divUnix}} }
                        }
                      }
                    }]
                  }
                }
                """);
            }

            if (url.Contains("period1="))
            {
                return StubHttpMessageHandler.JsonOk($$"""
                {
                  "chart": {
                    "result": [{
                      "meta": { "currency": "USD" },
                      "timestamp": [{{openUnix}}],
                      "indicators": {
                        "quote": [{ "close": [210.40] }]
                      }
                    }]
                  }
                }
                """);
            }

            // Live quote (range=5d&interval=1d)
            return StubHttpMessageHandler.JsonOk("""
            {
              "chart": {
                "result": [{
                  "meta": { "currency": "USD", "regularMarketPrice": 235.50 }
                }]
              }
            }
            """);
        });

        using HttpClient http = new(handler);
        YahooFinanceMarketDataProvider provider = new(http, tempDir.Path);

        PositionLot manualLot = new()
        {
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            YahooSymbol = "AAPL",
            StockCurrency = "USD",
            CountryCode = "US",
            Quantity = 10m,
            OpenDate = openDate,
            OpenPrice = 0m,
            CurrentMarketPrice = 0m,
            IsOpenPriceManual = false,
            IsClosePriceManual = false,
            Source = DataSource.ManualEntry
        };

        PortfolioDocument doc = new() { Lots = [manualLot] };

        // Act
        (int filledPrices, int fetchedDivs) = await provider.PopulateMissingPricesAndDividendsAsync(doc);

        // Assert: historical open price, live current price, and dividend were all populated
        Assert.Equal(2, filledPrices);
        Assert.Equal(210.40m, manualLot.OpenPrice);
        Assert.Equal(235.50m, manualLot.CurrentMarketPrice);
        Assert.False(manualLot.IsOpenPriceManual);
        Assert.False(manualLot.IsClosePriceManual);

        Assert.Equal(1, fetchedDivs);
        DividendRecord div = Assert.Single(doc.Dividends);
        Assert.False(div.IsFromBrokerReport);
        Assert.Equal(manualLot.Id, div.PositionLotId);
        Assert.Equal(0.25m, div.DividendPerShareNative);
        Assert.Equal(divDate.Date, div.PayDate.Date);
    }

    /// <summary>
    /// Verifies that manually locked prices (<see cref="PositionLot.IsOpenPriceManual"/> and
    /// <see cref="PositionLot.IsClosePriceManual"/>) are NEVER overwritten by <see cref="YahooFinanceMarketDataProvider.RefreshAllMarketDataAsync"/>.
    /// </summary>
    [Fact]
    public async Task RefreshAllMarketDataAsync_RespectsManualLocksOnOpenAndCurrentPrices()
    {
        using TempTestDirectory tempDir = new();

        StubHttpMessageHandler handler = new(_ => StubHttpMessageHandler.JsonOk("""
        {
          "chart": {
            "result": [{
              "meta": { "currency": "USD", "regularMarketPrice": 999.99 }
            }]
          }
        }
        """));

        using HttpClient http = new(handler);
        YahooFinanceMarketDataProvider provider = new(http, tempDir.Path);

        PositionLot lockedLot = new()
        {
            Ticker = "AAPL.US",
            YahooSymbol = "AAPL",
            StockCurrency = "USD",
            Quantity = 5m,
            OpenDate = new DateTime(2025, 1, 10),
            OpenPrice = 185m,
            CurrentMarketPrice = 200m,
            IsOpenPriceManual = true,
            IsClosePriceManual = true,
            Source = DataSource.ManualEntry
        };

        PortfolioDocument doc = new() { Lots = [lockedLot] };

        // Act
        await provider.RefreshAllMarketDataAsync(doc);

        // Assert: locked manual prices remained completely untouched
        Assert.Equal(185m, lockedLot.OpenPrice);
        Assert.Equal(200m, lockedLot.CurrentMarketPrice);
    }

    /// <summary>
    /// Verifies that for an XTB-imported open lot after <see cref="PortfolioDocument.ReportGeneratedUtc"/>:
    /// 1. Historical XTB open price stays fixed;
    /// 2. Live current price updates and clears stale <see cref="PositionLot.BrokerSaleOrCurrentValueEur"/>;
    /// 3. Existing XTB dividends are preserved, near-duplicate Yahoo dividends (within ±10 days) are skipped,
    ///    and new post-report Yahoo dividends are appended.
    /// </summary>
    [Fact]
    public async Task RefreshAllMarketDataAsync_XtbOpenLot_PreservesXtbDividendsAndAppendsOnlyNewPostReportDividends()
    {
        using TempTestDirectory tempDir = new();

        DateTime reportDate = new(2025, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        DateTime existingDivDate = new(2025, 6, 15, 0, 0, 0, DateTimeKind.Utc);
        DateTime postReportDivDate = new(2025, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        long existingDivUnix = new DateTimeOffset(existingDivDate.AddDays(2)).ToUnixTimeSeconds();
        long postReportDivUnix = new DateTimeOffset(postReportDivDate).ToUnixTimeSeconds();

        StubHttpMessageHandler handler = new(req =>
        {
            string url = req.RequestUri!.ToString();
            if (url.Contains("events=div"))
            {
                return StubHttpMessageHandler.JsonOk($$"""
                {
                  "chart": {
                    "result": [{
                      "meta": { "currency": "USD", "regularMarketPrice": 140.00 },
                      "events": {
                        "dividends": {
                          "{{existingDivUnix}}": { "amount": 0.50, "date": {{existingDivUnix}} },
                          "{{postReportDivUnix}}": { "amount": 0.55, "date": {{postReportDivUnix}} }
                        }
                      }
                    }]
                  }
                }
                """);
            }

            return StubHttpMessageHandler.JsonOk("""
            {
              "chart": {
                "result": [{
                  "meta": { "currency": "USD", "regularMarketPrice": 140.00 }
                }]
              }
            }
            """);
        });

        using HttpClient http = new(handler);
        YahooFinanceMarketDataProvider provider = new(http, tempDir.Path);

        PositionLot xtbLot = new()
        {
            CompanyName = "Example Corp",
            Ticker = "EXM.US",
            YahooSymbol = "EXM",
            StockCurrency = "USD",
            CountryCode = "US",
            Quantity = 10m,
            OpenDate = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            OpenPrice = 100m,
            CurrentMarketPrice = 110m,
            BrokerPurchaseValueEur = 950m,
            BrokerSaleOrCurrentValueEur = 1010m,
            Source = DataSource.XtbImport
        };

        DividendRecord xtbDividend = new()
        {
            PositionLotId = xtbLot.Id,
            Ticker = "EXM.US",
            PayDate = existingDivDate,
            GrossAmountEur = 4.60m,
            WithholdingTaxEur = 0.69m,
            NetAmountEur = 3.91m,
            IsFromBrokerReport = true
        };

        PortfolioDocument doc = new()
        {
            ReportGeneratedUtc = reportDate,
            Lots = [xtbLot],
            Dividends = [xtbDividend]
        };

        // Act
        (int updatedPrices, int fetchedDivs) = await provider.RefreshAllMarketDataAsync(doc);

        // Assert: XTB open price stayed fixed; live price updated and stale broker current snapshot cleared
        Assert.Equal(1, updatedPrices);
        Assert.Equal(100m, xtbLot.OpenPrice);
        Assert.Equal(950m, xtbLot.BrokerPurchaseValueEur);
        Assert.Equal(140m, xtbLot.CurrentMarketPrice);
        Assert.Null(xtbLot.BrokerSaleOrCurrentValueEur);

        // Assert: XTB dividend untouched, duplicate skipped, only post-report Yahoo dividend appended
        Assert.Equal(1, fetchedDivs);
        Assert.Equal(2, doc.Dividends.Count);
        Assert.Contains(doc.Dividends, d => d.IsFromBrokerReport && d.GrossAmountEur == 4.60m);
        Assert.Contains(doc.Dividends, d => !d.IsFromBrokerReport && d.DividendPerShareNative == 0.55m && d.PayDate.Date == postReportDivDate.Date);
    }

    /// <summary>
    /// Verifies that London Stock Exchange quotes returned by Yahoo Finance in pence (<c>GBp</c>)
    /// are automatically divided by 100 when the lot's <see cref="PositionLot.StockCurrency"/> is <c>GBP</c>.
    /// </summary>
    [Fact]
    public async Task RefreshOpenPositionQuotesAsync_NormalizesYahooGbpPenceToGbpPounds()
    {
        using TempTestDirectory tempDir = new();

        StubHttpMessageHandler handler = new(_ => StubHttpMessageHandler.JsonOk("""
        {
          "chart": {
            "result": [{
              "meta": { "currency": "GBp", "regularMarketPrice": 2845.0 }
            }]
          }
        }
        """));

        using HttpClient http = new(handler);
        YahooFinanceMarketDataProvider provider = new(http, tempDir.Path);

        PositionLot ukLot = new()
        {
            Ticker = "SHEL.UK",
            YahooSymbol = "SHEL.L",
            StockCurrency = "GBP",
            Quantity = 10m,
            OpenPrice = 25.00m,
            CurrentMarketPrice = 25.00m
        };

        // Act
        int updated = await provider.RefreshOpenPositionQuotesAsync([ukLot]);

        // Assert: 2845 GBp normalized to £28.45 GBP
        Assert.Equal(1, updated);
        Assert.Equal(28.45m, ukLot.CurrentMarketPrice);
    }
}

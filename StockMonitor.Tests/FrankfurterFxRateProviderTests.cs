using System.Net.Http;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="FrankfurterFxRateProvider"/> verifying online ECB FX parsing,
/// transaction-date range fetching, cross-currency conversion, GBX pence scaling, and disk caching.
/// </summary>
public sealed class FrankfurterFxRateProviderTests
{
    /// <summary>
    /// Verifies that <see cref="FrankfurterFxRateProvider.EnsurePortfolioRatesAsync"/> fetches both
    /// latest ECB rates and historical transaction-date ranges, computes accurate direct and cross-currency
    /// conversions, and applies the 0.01 scale factor for <c>GBX</c> (pence sterling).
    /// </summary>
    [Fact]
    public async Task EnsurePortfolioRatesAsync_FetchesLatestAndHistoricalRates_AndConvertsCrossCurrencies()
    {
        using TempTestDirectory tempDir = new();
        string cacheFile = tempDir.FilePath("fx_cache.json");

        // Arrange: stub Frankfurter API responses for /latest and historical range
        StubHttpMessageHandler handler = new(req =>
        {
            string url = req.RequestUri!.ToString();
            if (url.Contains("/v1/latest"))
            {
                return StubHttpMessageHandler.JsonOk("""
                {
                  "base": "EUR",
                  "date": "2025-11-18",
                  "rates": { "USD": 1.10, "GBP": 0.85, "CZK": 25.00 }
                }
                """);
            }

            if (url.Contains(".."))
            {
                return StubHttpMessageHandler.JsonOk("""
                {
                  "base": "EUR",
                  "start_date": "2025-01-10",
                  "end_date": "2025-06-15",
                  "rates": {
                    "2025-01-10": { "USD": 1.04, "GBP": 0.84, "CZK": 25.20 },
                    "2025-06-13": { "USD": 1.15, "GBP": 0.86, "CZK": 24.80 }
                  }
                }
                """);
            }

            return StubHttpMessageHandler.JsonOk("""
            {
              "base": "EUR",
              "date": "2025-06-13",
              "rates": { "USD": 1.15, "GBP": 0.86, "CZK": 24.80 }
            }
            """);
        });

        using HttpClient http = new(handler);
        FrankfurterFxRateProvider provider = new(http, cacheFile);

        PortfolioDocument doc = new()
        {
            Lots =
            [
                new PositionLot
                {
                    OpenDate = new DateTime(2025, 1, 10),
                    CloseDate = new DateTime(2025, 6, 15), // Sunday -> should fall back to Friday 2025-06-13
                    StockCurrency = "USD"
                }
            ]
        };

        // Act
        await provider.EnsurePortfolioRatesAsync(doc);

        // Assert: exact historical rate on 2025-01-10 (1 USD -> EUR = 1 / 1.04)
        decimal usdToEurOpen = provider.GetConversionRate("USD", "EUR", new DateTime(2025, 1, 10));
        Assert.Equal(Math.Round(1m / 1.04m, 6), usdToEurOpen);

        // Assert: cross-currency conversion on 2025-01-10 (1 USD -> CZK = 25.20 / 1.04)
        decimal usdToCzkOpen = provider.GetConversionRate("USD", "CZK", new DateTime(2025, 1, 10));
        Assert.Equal(Math.Round(25.20m / 1.04m, 6), usdToCzkOpen);

        // Assert: weekend date 2025-06-15 falls back to preceding business day 2025-06-13 (1 EUR -> USD = 1.15)
        decimal eurToUsdClose = provider.GetConversionRate("EUR", "USD", new DateTime(2025, 6, 15));
        Assert.Equal(1.15m, eurToUsdClose);

        // Assert: GBX (pence) converts to GBP at 0.01 and to EUR at (1 / 0.85) * 0.01 using latest rate
        Assert.Equal(0.01m, provider.GetConversionRate("GBX", "GBP"));
        Assert.Equal(Math.Round(1m / 0.85m * 0.01m, 6), provider.GetConversionRate("GBX", "EUR"));

        // Assert: same-currency conversion always returns 1.0
        Assert.Equal(1m, provider.GetConversionRate("EUR", "EUR"));
    }

    /// <summary>
    /// Verifies that rates fetched by one <see cref="FrankfurterFxRateProvider"/> instance are persisted
    /// to disk and reloaded by a second instance without making any network requests.
    /// </summary>
    [Fact]
    public async Task DiskCache_PersistsAndReloadsRatesAcrossInstancesWithoutNetworkCalls()
    {
        using TempTestDirectory tempDir = new();
        string cacheFile = tempDir.FilePath("fx_cache_persist.json");

        // Arrange: first instance populates the cache via mocked HTTP
        StubHttpMessageHandler onlineHandler = new(_ => StubHttpMessageHandler.JsonOk("""
        {
          "base": "EUR",
          "date": "2025-03-03",
          "rates": { "USD": 1.08, "CHF": 0.96 }
        }
        """));

        using (HttpClient http1 = new(onlineHandler))
        {
            FrankfurterFxRateProvider writer = new(http1, cacheFile);
            await writer.RefreshLatestRatesAsync();
        }

        // Act: second instance uses an HTTP handler that throws if any network call is attempted
        StubHttpMessageHandler offlineHandler = new(_ => throw new InvalidOperationException("Should read from cache, not network."));
        using HttpClient http2 = new(offlineHandler);
        FrankfurterFxRateProvider reader = new(http2, cacheFile);

        decimal eurToUsd = reader.GetConversionRate("EUR", "USD", new DateTime(2025, 3, 3));

        // Assert: cached rate returned offline
        Assert.Equal(1.08m, eurToUsd);
        Assert.Empty(offlineHandler.RequestedUrls);
    }
}

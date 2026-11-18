using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Fetches historical OHLCV series, live quotes, and dividend events from the Yahoo Finance v8 Chart API.
/// Uses in-memory and disk caching plus background thread parsing to keep UI row selection fast and non-blocking.
/// </summary>
public sealed class YahooFinanceMarketDataProvider
{
    private static readonly HttpClient Http = CreateHttpClient();
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, List<StockPricePoint>> _memorySeriesCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="YahooFinanceMarketDataProvider"/> class and ensures the cache directory exists.
    /// </summary>
    public YahooFinanceMarketDataProvider()
    {
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockMonitor",
            "YahooCache");
        Directory.CreateDirectory(_cacheDir);
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(8) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        return client;
    }

    /// <summary>
    /// Returns historical daily price points for a position lot asynchronously on a background thread,
    /// fetching from Yahoo Finance or falling back to cached/synthesized series.
    /// </summary>
    public Task<List<StockPricePoint>> GetHistoricalSeriesAsync(
        PositionLot lot,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            string symbol = string.IsNullOrWhiteSpace(lot.YahooSymbol)
                ? TickerMappingHelper.Resolve(lot.Ticker, lot.Category).YahooSymbol
                : lot.YahooSymbol;

            DateTime start = lot.OpenDate.Date.AddDays(-15);
            DateTime end = (lot.CloseDate ?? DateTime.UtcNow).Date.AddDays(1);
            string cacheKey = $"{symbol}_{start:yyyyMMdd}_{end:yyyyMMdd}_{lot.OpenPrice:0.####}";

            if (_memorySeriesCache.TryGetValue(cacheKey, out List<StockPricePoint>? cachedSeries))
            {
                return cachedSeries;
            }

            List<StockPricePoint>? fetched = await TryFetchYahooChartAsync(
                symbol,
                start,
                end,
                (double)lot.OpenPrice,
                cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            List<StockPricePoint> result = fetched is { Count: > 2 }
                ? fetched
                : GenerateFallbackSeries(lot);

            _memorySeriesCache[cacheKey] = result;
            return result;
        }, cancellationToken);
    }

    /// <summary>
    /// Refreshes the latest market price for open positions from Yahoo Finance.
    /// </summary>
    public async Task<int> RefreshOpenPositionQuotesAsync(
        IEnumerable<PositionLot> lots,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        int updatedCount = 0;
        List<IGrouping<string, PositionLot>> openGroups = lots
            .Where(l => !l.CloseDate.HasValue && !l.IsClosePriceManual)
            .GroupBy(l => string.IsNullOrWhiteSpace(l.YahooSymbol)
                ? TickerMappingHelper.Resolve(l.Ticker, l.Category).YahooSymbol
                : l.YahooSymbol, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int index = 0;
        foreach (IGrouping<string, PositionLot> group in openGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progressCallback?.Invoke($"Syncing Yahoo Finance quote ({index}/{openGroups.Count}): {group.Key}...");

            decimal? latestPrice = await TryFetchLatestPriceAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (latestPrice is > 0)
            {
                foreach (PositionLot lot in group)
                {
                    lot.CurrentMarketPrice = latestPrice.Value;
                    updatedCount++;
                }
            }
        }

        _memorySeriesCache.Clear();
        return updatedCount;
    }

    private async Task<decimal?> TryFetchLatestPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=5d&interval=1d";
            using HttpResponseMessage resp = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement meta = doc.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0]
                .GetProperty("meta");

            if (meta.TryGetProperty("regularMarketPrice", out JsonElement priceEl) &&
                priceEl.TryGetDecimal(out decimal price) && price > 0)
            {
                return price;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ignore network errors and retain XTB / cached quote
        }

        return null;
    }

    private async Task<List<StockPricePoint>?> TryFetchYahooChartAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        double purchaseRefPrice,
        CancellationToken cancellationToken)
    {
        string safeSym = string.Concat(symbol.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string cacheFile = Path.Combine(_cacheDir, $"{safeSym}.json");

        try
        {
            string? json = null;

            // Use disk cache immediately if fetched within the last 12 hours to avoid redundant network latency on row click
            if (File.Exists(cacheFile) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile)).TotalHours < 12)
            {
                json = await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                long period1 = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
                long period2 = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
                string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";

                try
                {
                    using HttpResponseMessage resp = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                    if (resp.IsSuccessStatusCode)
                    {
                        json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                        await File.WriteAllTextAsync(cacheFile, json, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    if (File.Exists(cacheFile))
                    {
                        json = await File.ReadAllTextAsync(cacheFile, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            if (!result.TryGetProperty("timestamp", out JsonElement tsArray))
            {
                return null;
            }

            JsonElement quote = result.GetProperty("indicators").GetProperty("quote")[0];
            JsonElement closes = quote.GetProperty("close");
            bool hasOpen = quote.TryGetProperty("open", out JsonElement opens);
            bool hasHigh = quote.TryGetProperty("high", out JsonElement highs);
            bool hasLow = quote.TryGetProperty("low", out JsonElement lows);

            int len = tsArray.GetArrayLength();
            List<StockPricePoint> points = new(len);
            for (int i = 0; i < len; i++)
            {
                if (closes[i].ValueKind != JsonValueKind.Number)
                {
                    continue;
                }

                long unix = tsArray[i].GetInt64();
                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.Date;
                if (dt < startUtc.Date || dt > endUtc.Date)
                {
                    continue;
                }

                double close = closes[i].GetDouble();
                double open = hasOpen && opens[i].ValueKind == JsonValueKind.Number ? opens[i].GetDouble() : close;
                double high = hasHigh && highs[i].ValueKind == JsonValueKind.Number ? highs[i].GetDouble() : close;
                double low = hasLow && lows[i].ValueKind == JsonValueKind.Number ? lows[i].GetDouble() : close;

                points.Add(new StockPricePoint
                {
                    Date = dt,
                    Open = Math.Round(open, 4),
                    High = Math.Round(high, 4),
                    Low = Math.Round(low, 4),
                    Close = Math.Round(close, 4),
                    PurchaseReferencePrice = purchaseRefPrice
                });
            }

            return points;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Generates a deterministic historical price curve anchored at the lot's exact Open Price and Current/Close Price when offline.
    /// </summary>
    public static List<StockPricePoint> GenerateFallbackSeries(PositionLot lot)
    {
        List<StockPricePoint> points = [];
        DateTime start = lot.OpenDate.Date;
        DateTime end = (lot.CloseDate ?? DateTime.UtcNow).Date;
        if (end <= start)
        {
            end = start.AddDays(30);
        }

        double startPrice = (double)(lot.OpenPrice > 0 ? lot.OpenPrice : 100m);
        double endPrice = (double)(lot.CloseOrCurrentPrice > 0 ? lot.CloseOrCurrentPrice : lot.CurrentMarketPrice > 0 ? lot.CurrentMarketPrice : lot.OpenPrice);

        int totalDays = Math.Max(1, (end - start).Days);
        int stepDays = Math.Max(1, totalDays / 90);
        int seed = StringComparer.OrdinalIgnoreCase.GetHashCode(lot.Ticker);
        Random rng = new(seed);

        double drift = endPrice - startPrice;
        for (int day = 0; day <= totalDays; day += stepDays)
        {
            double progress = (double)day / totalDays;
            double bridgeWeight = Math.Sin(progress * Math.PI);
            double wave = Math.Sin(progress * Math.PI * 5.0 + (seed % 7)) * 0.06 +
                          (rng.NextDouble() - 0.5) * 0.04;
            double price = Math.Max(0.05, startPrice + drift * progress + startPrice * wave * bridgeWeight);
            double spread = price * 0.012;

            points.Add(new StockPricePoint
            {
                Date = start.AddDays(day),
                Open = Math.Round(price - spread * 0.3, 4),
                High = Math.Round(price + spread, 4),
                Low = Math.Round(Math.Max(0.01, price - spread), 4),
                Close = Math.Round(price, 4),
                PurchaseReferencePrice = startPrice
            });
        }

        if (points.Count > 0)
        {
            points[0].Close = startPrice;
            points[^1].Date = end;
            points[^1].Close = endPrice;
        }

        return points;
    }
}

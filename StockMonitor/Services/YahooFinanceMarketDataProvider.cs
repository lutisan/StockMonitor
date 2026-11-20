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
    private static readonly HttpClient DefaultHttp = CreateHttpClient();
    private readonly HttpClient _http;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, List<StockPricePoint>> _memorySeriesCache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="YahooFinanceMarketDataProvider"/> class and ensures the cache directory exists.
    /// </summary>
    public YahooFinanceMarketDataProvider()
        : this(DefaultHttp, Path.Combine(PortfolioStorageService.ResolveDefaultDataDirectory(), "YahooCache"))
    {
    }

    internal YahooFinanceMarketDataProvider(HttpClient httpClient, string cacheDir)
    {
        _http = httpClient;
        _cacheDir = cacheDir;
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
    /// Refreshes the latest market price for open positions from Yahoo Finance and clears stale broker snapshot values.
    /// </summary>
    /// <param name="lots">Portfolio position lots.</param>
    /// <param name="progressCallback">Optional progress message callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of open position lots updated with live prices.</returns>
    public async Task<int> RefreshOpenPositionQuotesAsync(
        IEnumerable<PositionLot> lots,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        int updatedCount = 0;
        List<IGrouping<string, PositionLot>> openGroups = lots
            .Where(l => !l.CloseDate.HasValue && !l.IsClosePriceManual)
            .GroupBy(l => ResolveYahooSymbol(l), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int index = 0;
        foreach (IGrouping<string, PositionLot> group in openGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            index++;
            progressCallback?.Invoke($"Syncing Yahoo Finance quote ({index}/{openGroups.Count}): {group.Key}...");

            (decimal? latestPrice, string? yahooCcy) = await TryFetchLatestPriceAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (latestPrice is > 0)
            {
                foreach (PositionLot lot in group)
                {
                    lot.CurrentMarketPrice = NormalizePriceForLotCurrency(latestPrice.Value, yahooCcy, lot.StockCurrency);
                    lot.BrokerSaleOrCurrentValueEur = null;
                    lot.CloseConversionRate = null;
                    updatedCount++;
                }
            }
        }

        _memorySeriesCache.Clear();
        return updatedCount;
    }

    /// <summary>
    /// Populates any missing (cleared or unset) historical Open Prices, historical Close Prices, live Current Prices,
    /// and Yahoo Finance dividend events without overwriting manual or fixed XTB values.
    /// </summary>
    /// <param name="doc">Target portfolio document.</param>
    /// <param name="progressCallback">Optional progress message callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (updatedPricesCount, fetchedDividendsCount).</returns>
    public async Task<(int UpdatedPrices, int FetchedDividends)> PopulateMissingPricesAndDividendsAsync(
        PortfolioDocument doc,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        int updatedPrices = await FetchMissingHistoricalPricesAsync(doc.Lots, progressCallback, cancellationToken).ConfigureAwait(false);

        // Fetch live price for open lots that currently have no price set
        List<PositionLot> openMissingPrice = doc.Lots
            .Where(l => !l.CloseDate.HasValue && !l.IsClosePriceManual && l.CurrentMarketPrice <= 0m)
            .ToList();
        if (openMissingPrice.Count > 0)
        {
            updatedPrices += await RefreshOpenPositionQuotesAsync(openMissingPrice, progressCallback, cancellationToken).ConfigureAwait(false);
        }

        int fetchedDivs = await RefreshYahooDividendsAsync(doc, progressCallback, cancellationToken).ConfigureAwait(false);
        return (updatedPrices, fetchedDivs);
    }

    /// <summary>
    /// Performs a full online market sync: populates any missing historical open/close prices, refreshes live prices
    /// for all open non-manual lots, and fetches Yahoo Finance dividends for manual lots and post-XTB periods.
    /// </summary>
    /// <param name="doc">Target portfolio document.</param>
    /// <param name="progressCallback">Optional progress message callback.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (updatedPricesCount, fetchedDividendsCount).</returns>
    public async Task<(int UpdatedPrices, int FetchedDividends)> RefreshAllMarketDataAsync(
        PortfolioDocument doc,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        int updatedPrices = await FetchMissingHistoricalPricesAsync(doc.Lots, progressCallback, cancellationToken).ConfigureAwait(false);
        updatedPrices += await RefreshOpenPositionQuotesAsync(doc.Lots, progressCallback, cancellationToken).ConfigureAwait(false);
        int fetchedDivs = await RefreshYahooDividendsAsync(doc, progressCallback, cancellationToken).ConfigureAwait(false);
        return (updatedPrices, fetchedDivs);
    }

    private async Task<int> FetchMissingHistoricalPricesAsync(
        IEnumerable<PositionLot> lots,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        int updated = 0;
        foreach (PositionLot lot in lots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string sym = ResolveYahooSymbol(lot);

            // 1. Missing Open Price (manual entry or cleared XTB open price)
            if (!lot.IsOpenPriceManual && lot.OpenPrice <= 0m && lot.OpenDate > DateTime.MinValue)
            {
                progressCallback?.Invoke($"Fetching Yahoo historical Open Price for {sym} on {lot.OpenDate:yyyy-MM-dd}...");
                (decimal? price, string? yahooCcy) = await TryFetchClosePriceOnDateAsync(sym, lot.OpenDate.Date, cancellationToken).ConfigureAwait(false);
                if (price is > 0m)
                {
                    lot.OpenPrice = NormalizePriceForLotCurrency(price.Value, yahooCcy, lot.StockCurrency);
                    lot.BrokerPurchaseValueEur = null;
                    lot.OpenConversionRate = null;
                    updated++;
                }
            }

            // 2. Missing Close Price on a Closed lot (manually closed without close price, or cleared close price)
            if (lot.CloseDate.HasValue && !lot.IsClosePriceManual && (!lot.ClosePrice.HasValue || lot.ClosePrice.Value <= 0m))
            {
                progressCallback?.Invoke($"Fetching Yahoo historical Close Price for {sym} on {lot.CloseDate.Value:yyyy-MM-dd}...");
                (decimal? price, string? yahooCcy) = await TryFetchClosePriceOnDateAsync(sym, lot.CloseDate.Value.Date, cancellationToken).ConfigureAwait(false);
                if (price is > 0m)
                {
                    decimal normalized = NormalizePriceForLotCurrency(price.Value, yahooCcy, lot.StockCurrency);
                    lot.ClosePrice = normalized;
                    lot.CurrentMarketPrice = normalized;
                    lot.BrokerSaleOrCurrentValueEur = null;
                    lot.CloseConversionRate = null;
                    updated++;
                }
            }
        }

        return updated;
    }

    private async Task<int> RefreshYahooDividendsAsync(
        PortfolioDocument doc,
        Action<string>? progressCallback,
        CancellationToken cancellationToken)
    {
        bool hasAnyXtbDividends = doc.Dividends.Any(d => d.IsFromBrokerReport);
        DateTime todayUtc = DateTime.UtcNow.Date;

        List<IGrouping<string, PositionLot>> tickerGroups = doc.Lots
            .Where(l => !string.IsNullOrWhiteSpace(l.Ticker) && l.Quantity > 0m)
            .GroupBy(l => ResolveYahooSymbol(l), StringComparer.OrdinalIgnoreCase)
            .ToList();

        int addedCount = 0;
        int idx = 0;
        foreach (IGrouping<string, PositionLot> group in tickerGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<PositionLot> lotsForSym = group.ToList();
            bool needsFetch = !hasAnyXtbDividends
                || lotsForSym.Any(l => l.Source != DataSource.XtbImport)
                || lotsForSym.Any(l => !l.CloseDate.HasValue || l.CloseDate.Value.Date > doc.ReportGeneratedUtc.Date);

            if (!needsFetch)
            {
                continue;
            }

            idx++;
            progressCallback?.Invoke($"Checking Yahoo Finance dividends ({idx}/{tickerGroups.Count}): {group.Key}...");

            DateTime minOpen = lotsForSym.Min(l => l.OpenDate).Date;
            DateTime maxEnd = lotsForSym.Any(l => !l.CloseDate.HasValue)
                ? todayUtc.AddDays(1)
                : lotsForSym.Max(l => l.CloseDate!.Value).Date.AddDays(1);

            if (minOpen >= maxEnd)
            {
                continue;
            }

            List<(DateTime PayDate, decimal AmountPerShare, string Currency)> events =
                await TryFetchDividendEventsAsync(group.Key, minOpen, maxEnd, cancellationToken).ConfigureAwait(false);

            // Replace only previously fetched Yahoo API dividends for these lots; never touch XTB broker dividends
            HashSet<Guid> lotIds = lotsForSym.Select(l => l.Id).ToHashSet();
            doc.Dividends.RemoveAll(d => !d.IsFromBrokerReport && d.PositionLotId.HasValue && lotIds.Contains(d.PositionLotId.Value));

            foreach ((DateTime payDate, decimal amountPerShare, string nativeCcy) in events)
            {
                foreach (PositionLot lot in lotsForSym)
                {
                    if (lot.OpenDate.Date > payDate.Date)
                    {
                        continue;
                    }

                    if (lot.CloseDate.HasValue && lot.CloseDate.Value.Date < payDate.Date)
                    {
                        continue;
                    }

                    // For XTB-imported lots, never duplicate or override broker dividends up to ReportGeneratedUtc
                    bool hasBrokerMatch = doc.Dividends.Any(d =>
                        d.IsFromBrokerReport &&
                        ((d.PositionLotId.HasValue && d.PositionLotId.Value == lot.Id) ||
                         d.Ticker.Equals(lot.Ticker, StringComparison.OrdinalIgnoreCase)) &&
                        Math.Abs((d.PayDate.Date - payDate.Date).TotalDays) <= 10);

                    if (hasBrokerMatch)
                    {
                        continue;
                    }

                    string effectiveCcy = string.IsNullOrWhiteSpace(nativeCcy) ? lot.StockCurrency : nativeCcy;
                    decimal normalizedDps = NormalizePriceForLotCurrency(amountPerShare, effectiveCcy, lot.StockCurrency);
                    string recordCcy = effectiveCcy.Equals("GBp", StringComparison.OrdinalIgnoreCase) || effectiveCcy.Equals("GBX", StringComparison.OrdinalIgnoreCase)
                        ? "GBP"
                        : effectiveCcy.ToUpperInvariant();

                    doc.Dividends.Add(new DividendRecord
                    {
                        ExternalCashOperationId = $"YDIV-{lot.ExternalPositionId}-{payDate:yyyyMMdd}",
                        PositionLotId = lot.Id,
                        ExternalPositionId = lot.ExternalPositionId,
                        CompanyName = lot.CompanyName,
                        Ticker = lot.Ticker,
                        Category = lot.Category,
                        PayDate = payDate,
                        DividendPerShareNative = normalizedDps,
                        NativeCurrency = recordCcy,
                        TaxRatePercent = lot.EffectiveDividendTaxRate,
                        IsFromBrokerReport = false,
                        Comment = $"Yahoo Finance ({recordCcy} {normalizedDps:0.####}/SHR × {lot.Quantity:0.##})"
                    });
                    addedCount++;
                }
            }
        }

        return addedCount;
    }

    private static string ResolveYahooSymbol(PositionLot lot) =>
        string.IsNullOrWhiteSpace(lot.YahooSymbol)
            ? TickerMappingHelper.Resolve(lot.Ticker, lot.Category).YahooSymbol
            : lot.YahooSymbol;

    private static decimal NormalizePriceForLotCurrency(decimal rawPrice, string? yahooCurrency, string lotCurrency)
    {
        if (!string.IsNullOrWhiteSpace(yahooCurrency) &&
            (yahooCurrency.Equals("GBp", StringComparison.Ordinal) || yahooCurrency.Equals("GBX", StringComparison.OrdinalIgnoreCase)) &&
            lotCurrency.Equals("GBP", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(rawPrice / 100m, 4);
        }

        return Math.Round(rawPrice, 4);
    }

    private async Task<(decimal? Price, string? Currency)> TryFetchLatestPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?range=5d&interval=1d";
            using HttpResponseMessage resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return (null, null);
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement meta = doc.RootElement
                .GetProperty("chart")
                .GetProperty("result")[0]
                .GetProperty("meta");

            string? ccy = meta.TryGetProperty("currency", out JsonElement ccyEl) ? ccyEl.GetString() : null;
            if (meta.TryGetProperty("regularMarketPrice", out JsonElement priceEl) &&
                priceEl.TryGetDecimal(out decimal price) && price > 0)
            {
                return (price, ccy);
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

        return (null, null);
    }

    private async Task<(decimal? Price, string? Currency)> TryFetchClosePriceOnDateAsync(
        string symbol,
        DateTime targetDateUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            long period1 = new DateTimeOffset(DateTime.SpecifyKind(targetDateUtc.AddDays(-7), DateTimeKind.Utc)).ToUnixTimeSeconds();
            long period2 = new DateTimeOffset(DateTime.SpecifyKind(targetDateUtc.AddDays(2), DateTimeKind.Utc)).ToUnixTimeSeconds();
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d";

            using HttpResponseMessage resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return (null, null);
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            string? ccy = result.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("currency", out JsonElement ccyEl)
                ? ccyEl.GetString()
                : null;

            if (!result.TryGetProperty("timestamp", out JsonElement tsArray))
            {
                return (null, ccy);
            }

            JsonElement closes = result.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");
            int len = tsArray.GetArrayLength();
            decimal? bestPrior = null;
            decimal? firstAfter = null;

            for (int i = 0; i < len; i++)
            {
                if (closes[i].ValueKind != JsonValueKind.Number || !closes[i].TryGetDecimal(out decimal close) || close <= 0m)
                {
                    continue;
                }

                DateTime dt = DateTimeOffset.FromUnixTimeSeconds(tsArray[i].GetInt64()).UtcDateTime.Date;
                if (dt <= targetDateUtc.Date)
                {
                    bestPrior = close;
                }
                else
                {
                    firstAfter ??= close;
                }
            }

            return (bestPrior ?? firstAfter, ccy);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<List<(DateTime PayDate, decimal AmountPerShare, string Currency)>> TryFetchDividendEventsAsync(
        string symbol,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        List<(DateTime PayDate, decimal AmountPerShare, string Currency)> list = [];
        try
        {
            long period1 = new DateTimeOffset(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            long period2 = new DateTimeOffset(DateTime.SpecifyKind(endUtc, DateTimeKind.Utc)).ToUnixTimeSeconds();
            string url = $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={period1}&period2={period2}&interval=1d&events=div";

            using HttpResponseMessage resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                return list;
            }

            string json = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement result = doc.RootElement.GetProperty("chart").GetProperty("result")[0];
            string ccy = result.TryGetProperty("meta", out JsonElement meta) && meta.TryGetProperty("currency", out JsonElement ccyEl)
                ? (ccyEl.GetString() ?? "USD")
                : "USD";

            if (!result.TryGetProperty("events", out JsonElement eventsEl) ||
                !eventsEl.TryGetProperty("dividends", out JsonElement divsEl) ||
                divsEl.ValueKind != JsonValueKind.Object)
            {
                return list;
            }

            foreach (JsonProperty prop in divsEl.EnumerateObject())
            {
                JsonElement item = prop.Value;
                if (item.TryGetProperty("date", out JsonElement dateEl) &&
                    item.TryGetProperty("amount", out JsonElement amtEl) &&
                    amtEl.TryGetDecimal(out decimal amt) && amt > 0m)
                {
                    DateTime payDate = DateTimeOffset.FromUnixTimeSeconds(dateEl.GetInt64()).UtcDateTime;
                    list.Add((payDate, amt, ccy));
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Ignore transient network errors
        }

        return list.OrderBy(x => x.PayDate).ToList();
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
                    using HttpResponseMessage resp = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
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

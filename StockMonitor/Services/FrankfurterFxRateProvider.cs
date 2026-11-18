using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Fetches live (current day) and historical (exact day of transaction) exchange rates online
/// from the Frankfurter ECB API (https://api.frankfurter.dev/v1/) with local JSON caching for fetched dates.
/// Contains no hardcoded exchange rate tables.
/// </summary>
public sealed class FrankfurterFxRateProvider
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    private readonly string _cacheFilePath;
    private readonly Lock _syncLock = new();

    // Maps date key ("yyyy-MM-dd" or "LATEST") -> Dictionary of CurrencyCode -> Rate per 1 EUR (1 EUR = X Currency)
    private readonly Dictionary<string, Dictionary<string, decimal>> _eurRatesByDate =
        new(StringComparer.OrdinalIgnoreCase);

    private DateTime _lastLatestFetchUtc = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrankfurterFxRateProvider"/> class and loads any cached date rates.
    /// </summary>
    public FrankfurterFxRateProvider()
    {
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockMonitor");
        Directory.CreateDirectory(appData);
        _cacheFilePath = Path.Combine(appData, "fx_rates_by_date.json");
        LoadCache();
    }

    /// <summary>
    /// Gets the list of supported portfolio base currencies.
    /// </summary>
    public static IReadOnlyList<string> SupportedCurrencies { get; } = ["EUR", "USD", "CZK", "CHF", "GBP"];

    /// <summary>
    /// Ensures online ECB exchange rates are fetched for today (current day) and for every transaction date
    /// (lot open dates, lot close dates, dividend payment dates, and cash operation dates) in the document.
    /// </summary>
    public async Task EnsurePortfolioRatesAsync(
        PortfolioDocument doc,
        Action<string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        progressCallback?.Invoke("Checking online ECB exchange rates for current day...");
        await RefreshLatestRatesAsync(cancellationToken).ConfigureAwait(false);

        HashSet<DateTime> transactionDates = [];
        foreach (PositionLot lot in doc.Lots)
        {
            if (lot.OpenDate > DateTime.MinValue)
            {
                transactionDates.Add(lot.OpenDate.Date);
            }

            if (lot.CloseDate.HasValue && lot.CloseDate.Value > DateTime.MinValue)
            {
                transactionDates.Add(lot.CloseDate.Value.Date);
            }
        }

        foreach (DividendRecord div in doc.Dividends)
        {
            if (div.PayDate > DateTime.MinValue)
            {
                transactionDates.Add(div.PayDate.Date);
            }
        }

        foreach (AccountCashFlowRecord cf in doc.CashFlows)
        {
            if (cf.TimeUtc > DateTime.MinValue)
            {
                transactionDates.Add(cf.TimeUtc.Date);
            }
        }

        List<DateTime> missingDates;
        lock (_syncLock)
        {
            missingDates = transactionDates
                .Where(d => d <= DateTime.UtcNow.Date && !_eurRatesByDate.ContainsKey(FormatDateKey(d)))
                .OrderBy(d => d)
                .ToList();
        }

        if (missingDates.Count == 0)
        {
            return;
        }

        // Fetch the historical range from earliest missing transaction date to latest missing transaction date
        DateTime minDate = missingDates[0];
        DateTime maxDate = missingDates[^1];

        if (minDate < maxDate)
        {
            progressCallback?.Invoke($"Fetching online ECB exchange rates for transaction date range {minDate:yyyy-MM-dd} .. {maxDate:yyyy-MM-dd}...");
            await FetchDateRangeOnlineAsync(minDate, maxDate, cancellationToken).ConfigureAwait(false);
        }

        // For any specific transaction date still missing (e.g., weekend/holiday or single date), resolve online
        foreach (DateTime date in missingDates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = FormatDateKey(date);
            bool exists;
            lock (_syncLock)
            {
                exists = _eurRatesByDate.ContainsKey(key);
            }

            if (!exists)
            {
                if (!TryAssignNearestPrecedingCachedDate(date))
                {
                    progressCallback?.Invoke($"Fetching online ECB exchange rate for transaction date {key}...");
                    await FetchDateRatesOnlineAsync(date, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        SaveCache();
    }

    /// <summary>
    /// Refreshes the current day's latest ECB exchange rates online from Frankfurter.
    /// </summary>
    public async Task RefreshLatestRatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string url = "https://api.frankfurter.dev/v1/latest?base=EUR";
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ParseAndStoreSingleDateResponse(json, FormatDateKey(DateTime.UtcNow.Date), isLatest: true);
            _lastLatestFetchUtc = DateTime.UtcNow;
            SaveCache();
        }
        catch
        {
            // Network unavailable
        }
    }

    /// <summary>
    /// Gets the conversion rate from <paramref name="fromCurrency"/> to <paramref name="toCurrency"/>
    /// for the given <paramref name="date"/> (day of transaction), or for the current day when <paramref name="date"/> is null.
    /// Queries the Frankfurter API online if the requested date is not yet cached.
    /// </summary>
    public decimal GetConversionRate(string fromCurrency, string toCurrency, DateTime? date = null)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency) || string.IsNullOrWhiteSpace(toCurrency))
        {
            return 1m;
        }

        string from = fromCurrency.Trim().ToUpperInvariant();
        string to = toCurrency.Trim().ToUpperInvariant();

        decimal scale = 1m;
        if (from == "GBX")
        {
            from = "GBP";
            scale = 0.01m;
        }

        if (from == to)
        {
            return scale;
        }

        Dictionary<string, decimal>? eurRates = GetOrFetchEurRatesForDate(date);
        if (eurRates == null)
        {
            return scale;
        }

        decimal eurToFrom = from == "EUR" ? 1m : eurRates.GetValueOrDefault(from, 0m);
        decimal eurToTo = to == "EUR" ? 1m : eurRates.GetValueOrDefault(to, 0m);

        if (eurToFrom <= 0m || eurToTo <= 0m)
        {
            return scale;
        }

        return Math.Round(eurToTo / eurToFrom * scale, 6);
    }

    private Dictionary<string, decimal>? GetOrFetchEurRatesForDate(DateTime? date)
    {
        DateTime todayUtc = DateTime.UtcNow.Date;
        DateTime targetDate = date.HasValue && date.Value > DateTime.MinValue && date.Value.Date <= todayUtc
            ? date.Value.Date
            : todayUtc;

        string dateKey = FormatDateKey(targetDate);

        lock (_syncLock)
        {
            if (_eurRatesByDate.TryGetValue(dateKey, out Dictionary<string, decimal>? cached))
            {
                return cached;
            }
        }

        // Check nearest preceding business day in cache (for weekends/holidays)
        if (TryAssignNearestPrecedingCachedDate(targetDate))
        {
            lock (_syncLock)
            {
                if (_eurRatesByDate.TryGetValue(dateKey, out Dictionary<string, decimal>? fallback))
                {
                    return fallback;
                }
            }
        }

        lock (_syncLock)
        {
            return _eurRatesByDate.GetValueOrDefault("LATEST");
        }
    }

    private async Task FetchDateRatesOnlineAsync(DateTime date, CancellationToken cancellationToken = default)
    {
        string requestedKey = FormatDateKey(date);
        try
        {
            string url = $"https://api.frankfurter.dev/v1/{requestedKey}?base=EUR";
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            ParseAndStoreSingleDateResponse(json, requestedKey, isLatest: false);
            SaveCache();
        }
        catch
        {
            // Ignore transient network errors
        }
    }

    private async Task FetchDateRangeOnlineAsync(DateTime startDate, DateTime endDate, CancellationToken cancellationToken = default)
    {
        try
        {
            string startKey = FormatDateKey(startDate);
            string endKey = FormatDateKey(endDate);
            string url = $"https://api.frankfurter.dev/v1/{startKey}..{endKey}?base=EUR";
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("rates", out JsonElement ratesByDateEl) ||
                ratesByDateEl.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            lock (_syncLock)
            {
                foreach (JsonProperty dateProp in ratesByDateEl.EnumerateObject())
                {
                    if (dateProp.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    Dictionary<string, decimal> dayMap = ParseRatesObject(dateProp.Value);
                    if (dayMap.Count > 1)
                    {
                        _eurRatesByDate[dateProp.Name] = dayMap;
                    }
                }
            }
        }
        catch
        {
            // Individual date lookups will fall back if range query fails
        }
    }

    private void ParseAndStoreSingleDateResponse(string json, string requestedDateKey, bool isLatest)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("rates", out JsonElement ratesEl) ||
            ratesEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        Dictionary<string, decimal> ratesMap = ParseRatesObject(ratesEl);
        if (ratesMap.Count <= 1)
        {
            return;
        }

        string? actualDateKey = doc.RootElement.TryGetProperty("date", out JsonElement dateEl)
            ? dateEl.GetString()
            : null;

        lock (_syncLock)
        {
            _eurRatesByDate[requestedDateKey] = ratesMap;
            if (!string.IsNullOrWhiteSpace(actualDateKey))
            {
                _eurRatesByDate[actualDateKey] = ratesMap;
            }

            if (isLatest)
            {
                _eurRatesByDate["LATEST"] = ratesMap;
            }
        }
    }

    private static Dictionary<string, decimal> ParseRatesObject(JsonElement ratesEl)
    {
        Dictionary<string, decimal> map = new(StringComparer.OrdinalIgnoreCase) { ["EUR"] = 1m };
        foreach (JsonProperty prop in ratesEl.EnumerateObject())
        {
            if (prop.Value.TryGetDecimal(out decimal val) && val > 0m)
            {
                map[prop.Name] = val;
            }
        }

        return map;
    }

    private bool TryAssignNearestPrecedingCachedDate(DateTime date)
    {
        string targetKey = FormatDateKey(date);
        lock (_syncLock)
        {
            for (int daysBack = 1; daysBack <= 7; daysBack++)
            {
                string prevKey = FormatDateKey(date.AddDays(-daysBack));
                if (_eurRatesByDate.TryGetValue(prevKey, out Dictionary<string, decimal>? prevRates))
                {
                    _eurRatesByDate[targetKey] = prevRates;
                    return true;
                }
            }
        }

        return false;
    }

    private static string FormatDateKey(DateTime date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                return;
            }

            string json = File.ReadAllText(_cacheFilePath);
            Dictionary<string, Dictionary<string, decimal>>? loaded =
                JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, decimal>>>(json);
            if (loaded == null)
            {
                return;
            }

            lock (_syncLock)
            {
                foreach ((string dateKey, Dictionary<string, decimal> rates) in loaded)
                {
                    Dictionary<string, decimal> copy = new(rates, StringComparer.OrdinalIgnoreCase)
                    {
                        ["EUR"] = 1m
                    };
                    _eurRatesByDate[dateKey] = copy;
                }
            }
        }
        catch
        {
            // Ignore corrupted cache file
        }
    }

    private void SaveCache()
    {
        try
        {
            string json;
            lock (_syncLock)
            {
                json = JsonSerializer.Serialize(_eurRatesByDate, new JsonSerializerOptions { WriteIndented = true });
            }

            File.WriteAllText(_cacheFilePath, json);
        }
        catch
        {
            // Ignore disk write errors
        }
    }
}

using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Dynamically resolves, infers, and synchronizes ticker metadata (Yahoo symbol, country code, currency, category)
/// against the persisted <see cref="PortfolioDocument.TickerMappings"/> list without any hardcoded ticker dictionary.
/// </summary>
public static class TickerMappingHelper
{
    /// <summary>
    /// Represents resolved metadata for a broker ticker.
    /// </summary>
    /// <param name="YahooSymbol">Yahoo Finance symbol.</param>
    /// <param name="CountryCode">Two-letter ISO country code.</param>
    /// <param name="Currency">Trading currency code.</param>
    /// <param name="Category">Instrument category (<c>STOCK</c> or <c>ETF</c>).</param>
    public sealed record InstrumentMeta(string YahooSymbol, string CountryCode, string Currency, string Category);

    /// <summary>
    /// Resolves ticker metadata using the persisted <paramref name="mappings"/> collection first,
    /// falling back to exchange-suffix inference when not yet registered.
    /// </summary>
    public static InstrumentMeta Resolve(
        string ticker,
        string? explicitCategory = null,
        IEnumerable<TickerMapping>? mappings = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return new("SPY", "US", "USD", explicitCategory ?? "STOCK");
        }

        string trimmed = ticker.Trim();
        if (mappings != null)
        {
            TickerMapping? existing = mappings.FirstOrDefault(m =>
                m.Ticker.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                string cat = !string.IsNullOrWhiteSpace(existing.Category)
                    ? existing.Category
                    : (explicitCategory ?? "STOCK");
                return new(
                    string.IsNullOrWhiteSpace(existing.YahooSymbol) ? InferBySuffix(trimmed, cat).YahooSymbol : existing.YahooSymbol,
                    string.IsNullOrWhiteSpace(existing.CountryCode) ? "US" : existing.CountryCode,
                    string.IsNullOrWhiteSpace(existing.Currency) ? "USD" : existing.Currency,
                    cat);
            }
        }

        return InferBySuffix(trimmed, explicitCategory);
    }

    /// <summary>
    /// Finds or creates a dynamic <see cref="TickerMapping"/> in <paramref name="mappings"/> during XTB .xls/.xlsx/.csv import
    /// or manual lot editing. Preserves any entry where <see cref="TickerMapping.IsManualOverride"/> is true.
    /// </summary>
    public static TickerMapping ResolveOrUpsert(
        List<TickerMapping> mappings,
        string ticker,
        string? companyName = null,
        string? category = null,
        string? detectedCurrency = null,
        decimal? openConversionRateToEur = null)
    {
        string trimmed = ticker.Trim();
        TickerMapping? existing = mappings.FirstOrDefault(m =>
            m.Ticker.Equals(trimmed, StringComparison.OrdinalIgnoreCase));

        InstrumentMeta inferred = InferBySuffix(trimmed, category, detectedCurrency, openConversionRateToEur);

        if (existing != null)
        {
            if (!existing.IsManualOverride)
            {
                if (!string.IsNullOrWhiteSpace(companyName) &&
                    (string.IsNullOrWhiteSpace(existing.CompanyName) || existing.CompanyName.Equals(existing.Ticker, StringComparison.OrdinalIgnoreCase)))
                {
                    existing.CompanyName = companyName.Trim();
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    existing.Category = category.Trim().ToUpperInvariant();
                }

                if (!string.IsNullOrWhiteSpace(detectedCurrency))
                {
                    existing.Currency = detectedCurrency.Trim().ToUpperInvariant();
                }

                // Re-infer country if category was updated to ETF and not manually overridden
                if (existing.Category.Equals("ETF", StringComparison.OrdinalIgnoreCase) &&
                    existing.CountryCode is "DE" or "FR" or "UK" or "GB")
                {
                    existing.CountryCode = "IE";
                }
            }

            return existing;
        }

        TickerMapping created = new()
        {
            Ticker = trimmed,
            CompanyName = string.IsNullOrWhiteSpace(companyName) ? trimmed : companyName.Trim(),
            YahooSymbol = inferred.YahooSymbol,
            CountryCode = inferred.CountryCode,
            Currency = inferred.Currency,
            Category = inferred.Category,
            IsManualOverride = false
        };

        mappings.Add(created);
        return created;
    }

    /// <summary>
    /// Infers Yahoo Finance symbol, country code, and currency purely from the broker ticker suffix and XTB report clues.
    /// </summary>
    public static InstrumentMeta InferBySuffix(
        string ticker,
        string? explicitCategory = null,
        string? detectedCurrency = null,
        decimal? openConversionRateToEur = null)
    {
        string trimmed = ticker.Trim();
        string category = string.IsNullOrWhiteSpace(explicitCategory) ? "STOCK" : explicitCategory.Trim().ToUpperInvariant();
        bool isEtf = category.Equals("ETF", StringComparison.OrdinalIgnoreCase);

        string yahooSymbol = trimmed.ToUpperInvariant();
        string country = "US";
        string currency = "USD";

        if (trimmed.EndsWith(".US", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].Replace('.', '-').ToUpperInvariant();
            country = "US";
            currency = "USD";
        }
        else if (trimmed.EndsWith(".DE", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed.ToUpperInvariant();
            country = isEtf ? "IE" : "DE";
            currency = "EUR";
        }
        else if (trimmed.EndsWith(".NL", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".AS";
            country = isEtf ? "IE" : "NL";
            currency = "EUR";
        }
        else if (trimmed.EndsWith(".FR", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".PA";
            country = isEtf ? "IE" : "FR";
            currency = "EUR";
        }
        else if (trimmed.EndsWith(".CH", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".SW";
            country = "CH";
            currency = "CHF";
        }
        else if (trimmed.EndsWith(".CZ", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".PR";
            country = "CZ";
            currency = "CZK";
        }
        else if (trimmed.EndsWith(".UK", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".L";
            country = isEtf ? "IE" : "GB";
            currency = "GBP";
        }
        else if (trimmed.EndsWith(".AT", StringComparison.OrdinalIgnoreCase) || trimmed.EndsWith(".VI", StringComparison.OrdinalIgnoreCase))
        {
            yahooSymbol = trimmed[..^3].ToUpperInvariant() + ".VI";
            country = "AT";
            currency = "EUR";
        }
        else if (trimmed.Contains(':'))
        {
            string[] parts = trimmed.Split(':', 2);
            string prefix = parts[0].ToUpperInvariant();
            string sym = parts[1].ToUpperInvariant();
            (yahooSymbol, country, currency) = prefix switch
            {
                "EPA" => (sym + ".PA", isEtf ? "IE" : "FR", "EUR"),
                "AMS" => (sym + ".AS", isEtf ? "IE" : "NL", "EUR"),
                "ETR" or "FRA" => (sym + ".DE", isEtf ? "IE" : "DE", "EUR"),
                "VIE" => (sym + ".VI", "AT", "EUR"),
                "LON" => (sym + ".L", isEtf ? "IE" : "GB", "GBP"),
                _ => (sym.Replace('.', '-'), "US", "USD")
            };
        }

        if (!string.IsNullOrWhiteSpace(detectedCurrency))
        {
            currency = detectedCurrency.Trim().ToUpperInvariant();
        }
        else if (openConversionRateToEur == 1m && currency != "EUR")
        {
            currency = "EUR";
        }

        return new(yahooSymbol, country, currency, category);
    }

    /// <summary>
    /// Ensures every lot in <paramref name="doc"/> has an entry in <see cref="PortfolioDocument.TickerMappings"/>,
    /// applies the mapping metadata to lots, and dynamically updates the portfolio ticker list in <see cref="PortfolioDocument.TaxRules"/>.
    /// </summary>
    public static void SynchronizeDocumentMappings(PortfolioDocument doc)
    {
        foreach (PositionLot lot in doc.Lots)
        {
            if (string.IsNullOrWhiteSpace(lot.Ticker))
            {
                continue;
            }

            TickerMapping mapping = ResolveOrUpsert(
                doc.TickerMappings,
                lot.Ticker,
                lot.CompanyName,
                lot.Category,
                lot.StockCurrency,
                lot.OpenConversionRate);

            lot.YahooSymbol = mapping.YahooSymbol;
            lot.CountryCode = mapping.CountryCode;
            lot.StockCurrency = mapping.Currency;
            lot.Category = mapping.Category;
            if (!string.IsNullOrWhiteSpace(mapping.CompanyName))
            {
                lot.CompanyName = mapping.CompanyName;
            }
        }

        RefreshTaxRuleSampleTickers(doc);
    }

    /// <summary>
    /// Dynamically populates <see cref="CountryTaxRule.SampleTickers"/> from the actual tickers in <see cref="PortfolioDocument.TickerMappings"/>.
    /// </summary>
    public static void RefreshTaxRuleSampleTickers(PortfolioDocument doc)
    {
        Dictionary<string, List<string>> tickersByCountry = doc.TickerMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.Ticker) && !string.IsNullOrWhiteSpace(m.CountryCode))
            .GroupBy(m => m.CountryCode.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(m => m.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t).ToList(),
                StringComparer.OrdinalIgnoreCase);

        foreach (CountryTaxRule rule in doc.TaxRules)
        {
            if (tickersByCountry.TryGetValue(rule.CountryCode, out List<string>? list) && list.Count > 0)
            {
                rule.SampleTickers = string.Join(", ", list);
            }
            else if (string.IsNullOrWhiteSpace(rule.SampleTickers))
            {
                rule.SampleTickers = "-";
            }
        }
    }

    /// <summary>
    /// Creates default country withholding tax rules without any hardcoded ticker list.
    /// </summary>
    public static List<CountryTaxRule> CreateDefaultTaxRules() =>
    [
        new CountryTaxRule
        {
            CountryCode = "US",
            CountryName = "United States",
            SampleTickers = "-",
            StandardRatePercent = 30m,
            AppliedRatePercent = 15m,
            Notes = "15% treaty rate with W-8BEN (30% without W-8BEN)"
        },
        new CountryTaxRule
        {
            CountryCode = "NL",
            CountryName = "Netherlands",
            SampleTickers = "-",
            StandardRatePercent = 15m,
            AppliedRatePercent = 15m,
            Notes = "15% Dutch statutory withholding tax"
        },
        new CountryTaxRule
        {
            CountryCode = "DE",
            CountryName = "Germany",
            SampleTickers = "-",
            StandardRatePercent = 26.375m,
            AppliedRatePercent = 26.375m,
            Notes = "25% Kapitalertragsteuer + 5.5% solidarity surcharge"
        },
        new CountryTaxRule
        {
            CountryCode = "AT",
            CountryName = "Austria",
            SampleTickers = "-",
            StandardRatePercent = 27.5m,
            AppliedRatePercent = 27.5m,
            Notes = "27.5% Austrian KESt withholding tax"
        },
        new CountryTaxRule
        {
            CountryCode = "FR",
            CountryName = "France",
            SampleTickers = "-",
            StandardRatePercent = 25m,
            AppliedRatePercent = 25m,
            Notes = "25% French non-resident withholding tax + 0.3% French IFTT on purchase"
        },
        new CountryTaxRule
        {
            CountryCode = "CH",
            CountryName = "Switzerland",
            SampleTickers = "-",
            StandardRatePercent = 35m,
            AppliedRatePercent = 35m,
            Notes = "35% Swiss Verrechnungssteuer"
        },
        new CountryTaxRule
        {
            CountryCode = "CZ",
            CountryName = "Czechia",
            SampleTickers = "-",
            StandardRatePercent = 15m,
            AppliedRatePercent = 15m,
            Notes = "15% Czech withholding tax"
        },
        new CountryTaxRule
        {
            CountryCode = "IE",
            CountryName = "Ireland / UK (UCITS ETFs)",
            SampleTickers = "-",
            StandardRatePercent = 0m,
            AppliedRatePercent = 0m,
            Notes = "0% withholding tax at fund distribution level for UCITS ETFs"
        }
    ];
}

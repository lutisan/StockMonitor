using System.Globalization;
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
    /// Dynamically populates <see cref="CountryTaxRule.SampleTickers"/> from the active portfolio lots,
    /// auto-adds any missing countries (using predefined defaults if known, or 0% with <see cref="CountryTaxRule.IsTaxUnknown"/> = true),
    /// and resets unused countries back to <c>-</c>.
    /// </summary>
    public static void RefreshTaxRuleSampleTickers(PortfolioDocument doc)
    {
        IEnumerable<(string Ticker, string CountryCode)> sourcePairs = doc.Lots.Count > 0
            ? doc.Lots
                .Where(l => !string.IsNullOrWhiteSpace(l.Ticker) && !string.IsNullOrWhiteSpace(l.CountryCode))
                .Select(l => (l.Ticker.Trim(), l.CountryCode.Trim().ToUpperInvariant()))
            : doc.TickerMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.Ticker) && !string.IsNullOrWhiteSpace(m.CountryCode))
                .Select(m => (m.Ticker.Trim(), m.CountryCode.Trim().ToUpperInvariant()));

        Dictionary<string, List<string>> tickersByCountry = sourcePairs
            .GroupBy(p => p.CountryCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.Ticker).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        Dictionary<string, CountryTaxRule> defaultRulesByCountry = CreateDefaultTaxRules()
            .ToDictionary(r => r.CountryCode, StringComparer.OrdinalIgnoreCase);

        HashSet<string> existingRuleCodes = doc.TaxRules
            .Where(r => !string.IsNullOrWhiteSpace(r.CountryCode))
            .Select(r => r.CountryCode.Trim().ToUpperInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (string countryCode in tickersByCountry.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            if (existingRuleCodes.Contains(countryCode))
            {
                continue;
            }

            if (defaultRulesByCountry.TryGetValue(countryCode, out CountryTaxRule? predefined))
            {
                doc.TaxRules.Add(predefined);
            }
            else
            {
                doc.TaxRules.Add(new CountryTaxRule
                {
                    CountryCode = countryCode,
                    CountryName = ResolveCountryDisplayName(countryCode),
                    SampleTickers = "-",
                    StandardRatePercent = 0m,
                    AppliedRatePercent = 0m,
                    Notes = "Unknown withholding tax rate — please verify and enter manually",
                    IsTaxUnknown = true
                });
            }

            existingRuleCodes.Add(countryCode);
        }

        foreach (CountryTaxRule rule in doc.TaxRules)
        {
            if (!string.IsNullOrWhiteSpace(rule.CountryCode) &&
                tickersByCountry.TryGetValue(rule.CountryCode.Trim(), out List<string>? list) &&
                list.Count > 0)
            {
                rule.SampleTickers = string.Join(", ", list);
            }
            else
            {
                rule.SampleTickers = "-";
            }
        }
    }

    private static string ResolveCountryDisplayName(string countryCode)
    {
        try
        {
            RegionInfo region = new(countryCode);
            if (!string.IsNullOrWhiteSpace(region.EnglishName))
            {
                return region.EnglishName;
            }
        }
        catch
        {
            // Non-standard or unrecognized ISO country code
        }

        return countryCode;
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
        },
        new CountryTaxRule
        {
            CountryCode = "GB",
            CountryName = "United Kingdom",
            SampleTickers = "-",
            StandardRatePercent = 0m,
            AppliedRatePercent = 0m,
            Notes = "0% statutory withholding tax on ordinary UK corporate dividends (REIT PIDs 20%)"
        },
        new CountryTaxRule
        {
            CountryCode = "ES",
            CountryName = "Spain",
            SampleTickers = "-",
            StandardRatePercent = 19m,
            AppliedRatePercent = 15m,
            Notes = "19% statutory Spanish withholding tax (15% typical EU treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "IT",
            CountryName = "Italy",
            SampleTickers = "-",
            StandardRatePercent = 26m,
            AppliedRatePercent = 15m,
            Notes = "26% Italian statutory withholding tax (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "BE",
            CountryName = "Belgium",
            SampleTickers = "-",
            StandardRatePercent = 30m,
            AppliedRatePercent = 15m,
            Notes = "30% Belgian roerende voorheffing (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "SE",
            CountryName = "Sweden",
            SampleTickers = "-",
            StandardRatePercent = 30m,
            AppliedRatePercent = 15m,
            Notes = "30% Swedish kupongskatt (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "DK",
            CountryName = "Denmark",
            SampleTickers = "-",
            StandardRatePercent = 27m,
            AppliedRatePercent = 15m,
            Notes = "27% Danish udbytteskat (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "FI",
            CountryName = "Finland",
            SampleTickers = "-",
            StandardRatePercent = 35m,
            AppliedRatePercent = 15m,
            Notes = "35% Finnish statutory withholding tax for nominee accounts (15% treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "NO",
            CountryName = "Norway",
            SampleTickers = "-",
            StandardRatePercent = 25m,
            AppliedRatePercent = 15m,
            Notes = "25% Norwegian kildeskatt (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "PL",
            CountryName = "Poland",
            SampleTickers = "-",
            StandardRatePercent = 19m,
            AppliedRatePercent = 15m,
            Notes = "19% Polish statutory withholding tax (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "SK",
            CountryName = "Slovakia",
            SampleTickers = "-",
            StandardRatePercent = 7m,
            AppliedRatePercent = 7m,
            Notes = "7% Slovak dividend withholding tax for residents"
        },
        new CountryTaxRule
        {
            CountryCode = "LU",
            CountryName = "Luxembourg",
            SampleTickers = "-",
            StandardRatePercent = 15m,
            AppliedRatePercent = 15m,
            Notes = "15% Luxembourg statutory withholding tax"
        },
        new CountryTaxRule
        {
            CountryCode = "CA",
            CountryName = "Canada",
            SampleTickers = "-",
            StandardRatePercent = 25m,
            AppliedRatePercent = 15m,
            Notes = "25% Canadian non-resident withholding tax (15% treaty rate with NR301)"
        },
        new CountryTaxRule
        {
            CountryCode = "MX",
            CountryName = "Mexico",
            SampleTickers = "-",
            StandardRatePercent = 10m,
            AppliedRatePercent = 10m,
            Notes = "10% Mexican dividend withholding tax"
        },
        new CountryTaxRule
        {
            CountryCode = "BR",
            CountryName = "Brazil",
            SampleTickers = "-",
            StandardRatePercent = 0m,
            AppliedRatePercent = 0m,
            Notes = "0% on ordinary Brazilian dividends (15% on Interest on Equity / JCP)"
        },
        new CountryTaxRule
        {
            CountryCode = "JP",
            CountryName = "Japan",
            SampleTickers = "-",
            StandardRatePercent = 15.315m,
            AppliedRatePercent = 15m,
            Notes = "15.315% Japanese listed-equity withholding tax (15% typical treaty rate)"
        },
        new CountryTaxRule
        {
            CountryCode = "AU",
            CountryName = "Australia",
            SampleTickers = "-",
            StandardRatePercent = 30m,
            AppliedRatePercent = 15m,
            Notes = "0% on franked dividends; 30% statutory / 15% treaty on unfranked dividends"
        }
    ];
}

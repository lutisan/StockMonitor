using StockMonitor.Models;
using StockMonitor.Services;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="TickerMappingHelper"/> verifying dynamic exchange-suffix inference,
/// manual override preservation, document synchronization, and country tax-rule ticker summaries.
/// </summary>
public sealed class TickerMappingHelperTests
{
    /// <summary>
    /// Verifies that broker tickers from major exchanges are accurately mapped to their
    /// Yahoo Finance symbols, ISO country codes, and trading currencies without hardcoded ticker tables.
    /// </summary>
    [Theory]
    [InlineData("AAPL.US", "STOCK", "AAPL", "US", "USD")]
    [InlineData("BRK.B.US", "STOCK", "BRK-B", "US", "USD")]
    [InlineData("SAP.DE", "STOCK", "SAP.DE", "DE", "EUR")]
    [InlineData("SXR8.DE", "ETF", "SXR8.DE", "IE", "EUR")]
    [InlineData("ASML.NL", "STOCK", "ASML.AS", "NL", "EUR")]
    [InlineData("MC.FR", "STOCK", "MC.PA", "FR", "EUR")]
    [InlineData("NESN.CH", "STOCK", "NESN.SW", "CH", "CHF")]
    [InlineData("CEZ.CZ", "STOCK", "CEZ.PR", "CZ", "CZK")]
    [InlineData("SHEL.UK", "STOCK", "SHEL.L", "GB", "GBP")]
    [InlineData("VUSA.UK", "ETF", "VUSA.L", "IE", "GBP")]
    [InlineData("EBS.AT", "STOCK", "EBS.VI", "AT", "EUR")]
    [InlineData("EPA:AIR", "STOCK", "AIR.PA", "FR", "EUR")]
    [InlineData("LON:AZN", "STOCK", "AZN.L", "GB", "GBP")]
    public void InferBySuffix_MapsExchangeSuffixesToYahooSymbolCountryAndCurrency(
        string ticker,
        string category,
        string expectedYahoo,
        string expectedCountry,
        string expectedCurrency)
    {
        // Act
        TickerMappingHelper.InstrumentMeta meta = TickerMappingHelper.InferBySuffix(ticker, category);

        // Assert
        Assert.Equal(expectedYahoo, meta.YahooSymbol);
        Assert.Equal(expectedCountry, meta.CountryCode);
        Assert.Equal(expectedCurrency, meta.Currency);
        Assert.Equal(category, meta.Category);
    }

    /// <summary>
    /// Verifies that when an XTB conversion rate to EUR equals 1.0 and no explicit currency is given,
    /// <see cref="TickerMappingHelper.InferBySuffix"/> infers EUR as the stock trading currency.
    /// </summary>
    [Fact]
    public void InferBySuffix_WhenOpenConversionRateToEurIsOne_InfersEurCurrency()
    {
        // Act: ticker suffix alone would suggest GBP, but XTB 1:1 EUR conversion proves EUR trading
        TickerMappingHelper.InstrumentMeta meta = TickerMappingHelper.InferBySuffix(
            "CSPX.UK",
            explicitCategory: "ETF",
            detectedCurrency: null,
            openConversionRateToEur: 1m);

        // Assert
        Assert.Equal("EUR", meta.Currency);
        Assert.Equal("IE", meta.CountryCode);
    }

    /// <summary>
    /// Verifies that <see cref="TickerMappingHelper.ResolveOrUpsert"/> preserves a user's manual mapping
    /// override (<see cref="TickerMapping.IsManualOverride"/> = true) during subsequent imports.
    /// </summary>
    [Fact]
    public void ResolveOrUpsert_WhenMappingIsManuallyOverridden_PreservesUserValues()
    {
        // Arrange: user customized the Yahoo symbol and country for a ticker
        List<TickerMapping> mappings =
        [
            new TickerMapping
            {
                Ticker = "CUSTOM.DE",
                CompanyName = "My Custom Co",
                YahooSymbol = "CUSTOM-OVERRIDE.DE",
                CountryCode = "LU",
                Currency = "EUR",
                Category = "ETF",
                IsManualOverride = true
            }
        ];

        // Act: import tries to upsert with different inferred metadata
        TickerMapping resolved = TickerMappingHelper.ResolveOrUpsert(
            mappings,
            "CUSTOM.DE",
            companyName: "Broker Name",
            category: "STOCK",
            detectedCurrency: "USD");

        // Assert: manual override remains untouched
        Assert.Single(mappings);
        Assert.Equal("CUSTOM-OVERRIDE.DE", resolved.YahooSymbol);
        Assert.Equal("LU", resolved.CountryCode);
        Assert.Equal("EUR", resolved.Currency);
        Assert.Equal("My Custom Co", resolved.CompanyName);
    }

    /// <summary>
    /// Verifies that <see cref="TickerMappingHelper.SynchronizeDocumentMappings"/> creates mappings
    /// for all lots, applies metadata back onto the lots, and updates <see cref="CountryTaxRule.SampleTickers"/>.
    /// </summary>
    [Fact]
    public void SynchronizeDocumentMappings_PopulatesMappingsAndUpdatesTaxRuleSampleTickers()
    {
        // Arrange
        PortfolioDocument doc = new()
        {
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots =
            [
                new PositionLot { Ticker = "MSFT.US", CompanyName = "Microsoft", Category = "STOCK", StockCurrency = "USD" },
                new PositionLot { Ticker = "CEZ.CZ", CompanyName = "CEZ Group", Category = "STOCK", StockCurrency = "CZK" }
            ]
        };

        // Act
        TickerMappingHelper.SynchronizeDocumentMappings(doc);

        // Assert: mappings created and applied to lots
        Assert.Equal(2, doc.TickerMappings.Count);
        Assert.Equal("MSFT", doc.Lots[0].YahooSymbol);
        Assert.Equal("US", doc.Lots[0].CountryCode);
        Assert.Equal("CEZ.PR", doc.Lots[1].YahooSymbol);
        Assert.Equal("CZ", doc.Lots[1].CountryCode);

        // Assert: tax rules dynamically list actual portfolio tickers
        CountryTaxRule usRule = doc.TaxRules.Single(r => r.CountryCode == "US");
        CountryTaxRule czRule = doc.TaxRules.Single(r => r.CountryCode == "CZ");
        Assert.Contains("MSFT.US", usRule.SampleTickers);
        Assert.Contains("CEZ.CZ", czRule.SampleTickers);
    }

    /// <summary>
    /// Verifies that <see cref="TickerMappingHelper.RefreshTaxRuleSampleTickers"/> resets stale tickers on unused countries,
    /// auto-adds unknown countries with 0% tax and <see cref="CountryTaxRule.IsTaxUnknown"/> = true,
    /// and clears <see cref="CountryTaxRule.IsTaxUnknown"/> when the user enters a positive tax rate.
    /// </summary>
    [Fact]
    public void RefreshTaxRuleSampleTickers_AutoAddsUnknownCountryInRedAndClearsStaleTickers()
    {
        PortfolioDocument doc = new()
        {
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            Lots =
            [
                new PositionLot { Ticker = "TENCENT.HK", CompanyName = "Tencent", CountryCode = "HK", StockCurrency = "HKD" }
            ]
        };
        doc.TaxRules.Single(r => r.CountryCode == "US").SampleTickers = "OLD.US";

        TickerMappingHelper.RefreshTaxRuleSampleTickers(doc);

        // Stale US ticker cleared
        Assert.Equal("-", doc.TaxRules.Single(r => r.CountryCode == "US").SampleTickers);

        // Unknown country HK auto-added with 0% and IsTaxUnknown = true
        CountryTaxRule hk = doc.TaxRules.Single(r => r.CountryCode == "HK");
        Assert.Equal("TENCENT.HK", hk.SampleTickers);
        Assert.Equal(0m, hk.StandardRatePercent);
        Assert.Equal(0m, hk.AppliedRatePercent);
        Assert.True(hk.IsTaxUnknown);

        // Editing tax rate clears IsTaxUnknown
        hk.AppliedRatePercent = 10m;
        Assert.False(hk.IsTaxUnknown);
    }
}

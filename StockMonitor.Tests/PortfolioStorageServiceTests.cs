using System.IO;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="PortfolioStorageService"/> verifying atomic JSON persistence,
/// round-trip deserialization, automatic tax-rule/ticker-mapping hydration, and corrupted-file recovery.
/// </summary>
public sealed class PortfolioStorageServiceTests
{
    /// <summary>
    /// Verifies that saving a <see cref="PortfolioDocument"/> with <see cref="PortfolioStorageService.SaveAsync"/>
    /// and reloading it via <see cref="PortfolioStorageService.LoadOrSeedAsync"/> preserves lots, settings,
    /// and automatically hydrates missing default tax rules and ticker mappings.
    /// </summary>
    [Fact]
    public async Task SaveAndLoadOrSeedAsync_RoundTripsPortfolioAndHydratesMissingMappings()
    {
        using TempTestDirectory tempDir = new();
        string jsonPath = tempDir.FilePath("portfolio.json");
        PortfolioStorageService storage = new(new XtbWorkbookImportService(), jsonPath);

        PortfolioDocument original = new()
        {
            Settings = new AppSettings
            {
                BaseCurrency = "USD",
                ThemeName = "FluentDark",
                Language = "SK"
            },
            Lots =
            [
                new PositionLot
                {
                    ExternalPositionId = "MAN-100",
                    CompanyName = "Apple Inc.",
                    Ticker = "AAPL.US",
                    StockCurrency = "USD",
                    Quantity = 12m,
                    OpenDate = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                    OpenPrice = 215m,
                    IsOpenPriceManual = true,
                    Comment = "Saved note",
                    Source = DataSource.ManualEntry
                }
            ]
        };

        // Act: save then reload
        await storage.SaveAsync(original);
        Assert.True(File.Exists(jsonPath));

        PortfolioDocument loaded = await storage.LoadOrSeedAsync();

        // Assert: settings and lot persisted accurately
        Assert.Equal("USD", loaded.Settings.BaseCurrency);
        Assert.Equal("FluentDark", loaded.Settings.ThemeName);
        Assert.Equal("SK", loaded.Settings.Language);

        PositionLot lot = Assert.Single(loaded.Lots);
        Assert.Equal("MAN-100", lot.ExternalPositionId);
        Assert.Equal(215m, lot.OpenPrice);
        Assert.True(lot.IsOpenPriceManual);
        Assert.Equal("Saved note", lot.Comment);

        // Assert: default tax rules and dynamic ticker mappings were auto-populated on load
        Assert.NotEmpty(loaded.TaxRules);
        Assert.Contains(loaded.TickerMappings, m => m.Ticker == "AAPL.US" && m.YahooSymbol == "AAPL");
    }

    /// <summary>
    /// Verifies that when <c>portfolio.json</c> contains corrupted JSON,
    /// <see cref="PortfolioStorageService.LoadOrSeedAsync"/> recovers gracefully without throwing
    /// by falling back to a fresh seeded document.
    /// </summary>
    [Fact]
    public async Task LoadOrSeedAsync_WhenJsonFileIsCorrupted_RecoversGracefullyWithSeededDocument()
    {
        using TempTestDirectory tempDir = new();
        string jsonPath = tempDir.FilePath("corrupted_portfolio.json");
        await File.WriteAllTextAsync(jsonPath, "{ invalid json :::");

        PortfolioStorageService storage = new(new XtbWorkbookImportService(), jsonPath);

        // Act
        PortfolioDocument recovered = await storage.LoadOrSeedAsync();

        // Assert: valid document returned with default tax rules
        Assert.NotNull(recovered);
        Assert.NotEmpty(recovered.TaxRules);
    }

    /// <summary>
    /// Verifies that <see cref="PortfolioStorageService.SaveToFileAsync(PortfolioDocument, string, string?, CancellationToken)"/> with a password writes an encrypted JSON
    /// file that requires the exact password to open, and without a password writes plain JSON.
    /// </summary>
    [Fact]
    public async Task SaveToFileAsync_WithAndWithoutPassword_EncryptsAndDecryptsCorrectly()
    {
        using TempTestDirectory tempDir = new();
        PortfolioStorageService storage = new(new XtbWorkbookImportService(), tempDir.FilePath("default.json"));

        PortfolioDocument doc = storage.CreateEmptyDocument();
        doc.AccountNumber = "ACC-999";
        doc.Lots.Add(new PositionLot
        {
            ExternalPositionId = "MAN-500",
            CompanyName = "Microsoft Corp",
            Ticker = "MSFT.US",
            StockCurrency = "USD",
            Quantity = 5m,
            OpenDate = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            OpenPrice = 410m,
            IsOpenPriceManual = true,
            Source = DataSource.ManualEntry
        });

        // 1. Save unencrypted
        string plainPath = tempDir.FilePath("plain.json");
        await storage.SaveToFileAsync(doc, plainPath, password: null);
        Assert.False(await storage.IsFileEncryptedAsync(plainPath));
        string plainRaw = await File.ReadAllTextAsync(plainPath);
        Assert.Contains("ACC-999", plainRaw);

        PortfolioDocument loadedPlain = await storage.LoadFromFileAsync(plainPath);
        Assert.Equal("ACC-999", loadedPlain.AccountNumber);
        Assert.Single(loadedPlain.Lots);

        // 2. Save encrypted
        string encryptedPath = tempDir.FilePath("secret.json");
        await storage.SaveToFileAsync(doc, encryptedPath, password: "MySecretPassword!");
        Assert.True(await storage.IsFileEncryptedAsync(encryptedPath));
        string encryptedRaw = await File.ReadAllTextAsync(encryptedPath);
        Assert.DoesNotContain("ACC-999", encryptedRaw);
        Assert.DoesNotContain("MySecretPassword!", encryptedRaw);

        // Missing password rejected
        await Assert.ThrowsAsync<EncryptedDocumentPasswordRequiredException>(
            () => storage.LoadFromFileAsync(encryptedPath, password: null));

        // Wrong password rejected
        await Assert.ThrowsAsync<InvalidDocumentPasswordException>(
            () => storage.LoadFromFileAsync(encryptedPath, password: "WrongPassword"));

        // Correct password succeeds
        PortfolioDocument loadedSecret = await storage.LoadFromFileAsync(encryptedPath, password: "MySecretPassword!");
        Assert.Equal("ACC-999", loadedSecret.AccountNumber);
        Assert.Single(loadedSecret.Lots);
    }

    /// <summary>
    /// Verifies that <see cref="PortfolioStorageService.CreateEmptyDocument"/> creates an empty portfolio
    /// with default country tax rules and no lots, dividends, or cash flows.
    /// </summary>
    [Fact]
    public void CreateEmptyDocument_ReturnsEmptyPortfolioWithDefaultTaxRules()
    {
        using TempTestDirectory tempDir = new();
        PortfolioStorageService storage = new(new XtbWorkbookImportService(), tempDir.FilePath("default.json"));

        PortfolioDocument empty = storage.CreateEmptyDocument();

        Assert.Empty(empty.Lots);
        Assert.Empty(empty.Dividends);
        Assert.Empty(empty.CashFlows);
        Assert.NotEmpty(empty.TaxRules);
    }
}

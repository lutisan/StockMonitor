using System.IO;
using System.IO.Compression;
using Microsoft.Extensions.DependencyInjection;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Verifies dynamic <c>*.export.dll</c> discovery via <see cref="ExportPluginLoader"/> and
/// end-to-end execution of the discovered <see cref="IExportService"/> implementations.
/// </summary>
public sealed class ExportPluginLoaderTests
{
    private static string DefaultExportsDirectory =>
        Path.Combine(AppContext.BaseDirectory, ExportPluginLoader.DefaultPluginsFolderName);

    /// <summary>
    /// Verifies that <see cref="ExportPluginLoader.RegisterExportPlugins"/> discovers the Excel and CSV
    /// implementations from <c>StockMonitor.Export.Default.export.dll</c> and the PDF implementation from
    /// <c>StockMonitor.Export.Pdf.export.dll</c> and registers all of them into DI.
    /// </summary>
    [Fact]
    public void RegisterExportPlugins_DiscoversDefaultExcelCsvAndPdfExporters()
    {
        ServiceCollection services = new();
        int count = ExportPluginLoader.RegisterExportPlugins(services, DefaultExportsDirectory);

        Assert.Equal(3, count);

        using ServiceProvider provider = services.BuildServiceProvider();
        List<IExportService> exporters = provider.GetServices<IExportService>().ToList();

        Assert.Equal(3, exporters.Count);
        Assert.Contains(exporters, e => e.Id == "excel" && e.DefaultExtension == ".xlsx");
        Assert.Contains(exporters, e => e.Id == "csv" && e.DefaultExtension == ".csv");
        Assert.Contains(exporters, e => e.Id == "pdf" && e.DefaultExtension == ".pdf");
    }

    /// <summary>
    /// Verifies that non-<c>*.export.dll</c> files and missing directories are safely ignored.
    /// </summary>
    [Fact]
    public void DiscoverExportServiceTypes_IgnoresNonExportDllsAndMissingDirectories()
    {
        using TempTestDirectory temp = new();
        File.WriteAllText(temp.FilePath("RegularLibrary.dll"), "not a plugin");

        IReadOnlyList<Type> discovered = ExportPluginLoader.DiscoverExportServiceTypes(
            [temp.Path, Path.Combine(temp.Path, "DoesNotExist")]);

        Assert.Empty(discovered);
    }

    /// <summary>
    /// Verifies that the dynamically loaded CSV and Excel exporters both produce valid output files.
    /// </summary>
    [Fact]
    public void DiscoveredExporters_WriteValidCsvAndExcelFiles()
    {
        ServiceCollection services = new();
        ExportPluginLoader.RegisterExportPlugins(services, DefaultExportsDirectory);
        using ServiceProvider provider = services.BuildServiceProvider();
        List<IExportService> exporters = provider.GetServices<IExportService>().ToList();

        PortfolioDocument doc = CreateSampleDocument();
        List<PortfolioHistoryPoint> history =
        [
            new PortfolioHistoryPoint
            {
                Date = new DateTime(2025, 6, 30),
                CumulativeDepositsBase = 1000m,
                CumulativeInvestedBase = 1000m,
                HoldingsMarketValueBase = 1080m,
                TotalPortfolioValueBase = 1080m,
                TotalProfitBase = 80m
            },
            new PortfolioHistoryPoint
            {
                Date = new DateTime(2025, 12, 31),
                CumulativeDepositsBase = 1500m,
                CumulativeInvestedBase = 1500m,
                HoldingsMarketValueBase = 1750m,
                TotalPortfolioValueBase = 1750m,
                TotalProfitBase = 250m
            }
        ];

        using TempTestDirectory temp = new();

        IExportService csvExporter = Assert.Single(exporters, e => e.Id == "csv");
        string csvPath = temp.FilePath("portfolio.csv");
        csvExporter.ExportPortfolio(doc, history, csvPath);
        Assert.True(File.Exists(csvPath));
        string csvText = File.ReadAllText(csvPath);
        Assert.Contains("Apple Inc.", csvText);
        Assert.Contains("AAPL.US", csvText);

        IExportService excelExporter = Assert.Single(exporters, e => e.Id == "excel");
        string xlsxPath = temp.FilePath("portfolio.xlsx");
        excelExporter.ExportPortfolio(doc, history, xlsxPath);
        Assert.True(File.Exists(xlsxPath));
        byte[] headerBytes = File.ReadAllBytes(xlsxPath);
        Assert.True(headerBytes.Length > 100);
        // .xlsx files are ZIP archives starting with 'PK' and containing native chart XML parts
        Assert.Equal((byte)'P', headerBytes[0]);
        Assert.Equal((byte)'K', headerBytes[1]);
        using (ZipArchive zip = ZipFile.OpenRead(xlsxPath))
        {
            int chartParts = zip.Entries.Count(e =>
                e.FullName.StartsWith("xl/charts/chart", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(4, chartParts);
        }

        IExportService pdfExporter = Assert.Single(exporters, e => e.Id == "pdf");
        string pdfPath = temp.FilePath("portfolio.pdf");
        pdfExporter.ExportPortfolio(doc, history, pdfPath);
        Assert.True(File.Exists(pdfPath));
        byte[] pdfBytes = File.ReadAllBytes(pdfPath);
        Assert.True(pdfBytes.Length > 200);
        // PDF files start with "%PDF-"
        Assert.Equal((byte)'%', pdfBytes[0]);
        Assert.Equal((byte)'P', pdfBytes[1]);
        Assert.Equal((byte)'D', pdfBytes[2]);
        Assert.Equal((byte)'F', pdfBytes[3]);
        Assert.Equal((byte)'-', pdfBytes[4]);
    }

    private static PortfolioDocument CreateSampleDocument()
    {
        PortfolioDocument doc = new();
        doc.Settings.BaseCurrency = "EUR";
        doc.Lots.Add(new PositionLot
        {
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            YahooSymbol = "AAPL",
            Category = "Tech",
            OpenDate = new DateTime(2025, 1, 15),
            Quantity = 5m,
            StockCurrency = "USD",
            OpenPrice = 200m,
            CurrentMarketPrice = 220m,
            OpenCostBase = 920m,
            CloseOrCurrentValueBase = 1012m,
            CapitalProfitLossBase = 92m,
            NetDividendsBase = 2.5m,
            TotalProfitBase = 94.5m,
            ExternalPositionId = "POS-1"
        });
        doc.Lots.Add(new PositionLot
        {
            CompanyName = "Microsoft Corp.",
            Ticker = "MSFT.US",
            YahooSymbol = "MSFT",
            Category = "Tech",
            OpenDate = new DateTime(2025, 3, 10),
            Quantity = 2m,
            StockCurrency = "USD",
            OpenPrice = 420m,
            CurrentMarketPrice = 395m,
            OpenCostBase = 770m,
            CloseOrCurrentValueBase = 725m,
            CapitalProfitLossBase = -45m,
            NetDividendsBase = 1.2m,
            TotalProfitBase = -43.8m,
            ExternalPositionId = "POS-2"
        });
        doc.Dividends.Add(new DividendRecord
        {
            PayDate = new DateTime(2024, 11, 15),
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            NativeCurrency = "USD",
            DividendPerShareNative = 0.25m,
            GrossAmountBase = 1.10m,
            TaxRatePercent = 15m,
            WithholdingTaxBase = 0.16m,
            NetAmountBase = 0.94m
        });
        doc.Dividends.Add(new DividendRecord
        {
            PayDate = new DateTime(2025, 6, 1),
            CompanyName = "Apple Inc.",
            Ticker = "AAPL.US",
            NativeCurrency = "USD",
            DividendPerShareNative = 0.25m,
            GrossAmountBase = 1.15m,
            TaxRatePercent = 15m,
            WithholdingTaxBase = 0.17m,
            NetAmountBase = 0.98m
        });
        doc.TaxRules.Add(new CountryTaxRule
        {
            CountryCode = "US",
            CountryName = "United States",
            StandardRatePercent = 30m,
            AppliedRatePercent = 15m
        });
        return doc;
    }
}

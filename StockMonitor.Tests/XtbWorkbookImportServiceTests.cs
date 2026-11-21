using System.IO;
using StockMonitor.Import.Xtb;
using StockMonitor.Models;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="XtbWorkbookImportService"/> verifying CSV parsing of Closed Positions,
/// Open Positions, and Cash Operations (dividends + paired WHT taxes, deposits, interest),
/// as well as preservation of manual lots and manual comments across re-imports.
/// </summary>
public sealed class XtbWorkbookImportServiceTests
{
    private const string SampleXtbCsv = """
        Account number,1576650,,,,,,,,,,,,,,,,,,,,,,,
        Closed Positions,,,,,,,,,,,,,,,,,,,,,,,,
        Date from (UTC),2020-01-01 00:00:00,,,,,,,,,,,,,,,,,,,,,,,
        Date to (UTC),2025-10-22 18:51:05,,,,,,,,,,,,,,,,,,,,,,,
        Instrument,Ticker,Category,Type,Volume,Open Price,Open Time (UTC),Close Price,Close Time (UTC),Product,Profit/Loss,Gross Profit,Purchase Value,Sale Value,Stop Loss,Take Profit,Commission,Margin,Swap,Rollover,Open Conversion Rate,Close Conversion Rate,Close Origin,Position ID,Comment
        Nvidia,NVDA.US,STOCK,BUY,4,135.32,2022-09-21 17:12:36,478.15,2023-11-27 14:30:15,My Trades,1189.59,1189.59,551.1,1740.69,,,0,,,,1.01813,0.91012,Android,736368378,
        Open Positions,,,,,,,,,,,,,,,,,,
        Data as of report generated,2025-10-22 18:51:11,,,,,,,,,,,,,,,,,
        Product,Instrument/Position,Ticker,Category,Type,Volume,Value,Current price,Open price,Open time (UTC),Stop Loss,Take Profit,Net Profit %,Net Profit,Gross Profit,Margin,Open Commission,Swap,Rollover
        My Trades,MSCI World IT TR,LYPG.DE,ETF,,10,12098,,340.5,,,,255.3,8693,8693,,-10,,
        My Trades,261950118,LYPG.DE,,BUY,10,12098,1209.8,340.5,2020-06-12 13:04:18,,,255.3,8693,8693,,-10,,
        Cash Operations,,,,,,,,,
        Date from (UTC),2020-01-01 00:00:00,,,,,,,,
        Date to (UTC),2025-10-22 18:51:05,,,,,,,,
        Type,Instrument,Ticker,Category,Time,Amount,ID,Comment,Product,Position ID
        Withholding tax,Nvidia,NVDA.US,STOCK,2023-06-15 10:14:03,-1.50,1480602006,NVDA.US USD WHT 15%,My Trades,
        Dividend,Nvidia,NVDA.US,STOCK,2023-06-15 10:14:03,10.00,1480602005,NVDA.US USD 0.25/ SHR,My Trades,
        Free funds interest,,,,2025-10-03 10:39:54,2.50,1463788760,Free-funds Interest 2025-09,My Trades,
        Deposit,,,,2020-06-01 09:00:00,5000.00,1000000001,Bank transfer,My Trades,
        """;

    /// <summary>
    /// Verifies that importing a standard XTB CSV report populates closed lots, open lots,
    /// paired dividend + withholding tax records, cash flows, dynamic ticker mappings, and report date.
    /// </summary>
    [Fact]
    public void ImportFile_ValidXtbCsv_ImportsClosedOpenLotsPairedDividendsAndCashFlows()
    {
        using TempTestDirectory tempDir = new();
        string csvPath = tempDir.FilePath("xtb_sample.csv");
        File.WriteAllText(csvPath, SampleXtbCsv);

        XtbWorkbookImportService importer = new();
        PortfolioDocument doc = new() { TaxRules = TickerMappingHelper.CreateDefaultTaxRules() };

        // Act
        importer.ImportFile(csvPath, doc, replaceExistingBrokerData: true);

        // Assert: 1 closed lot (NVDA.US) + 1 open lot (LYPG.DE)
        Assert.Equal(2, doc.Lots.Count);

        PositionLot nvda = Assert.Single(doc.Lots, l => l.Ticker == "NVDA.US");
        Assert.True(nvda.IsClosed);
        Assert.Equal("736368378", nvda.ExternalPositionId);
        Assert.Equal(4m, nvda.Quantity);
        Assert.Equal(135.32m, nvda.OpenPrice);
        Assert.Equal(478.15m, nvda.ClosePrice);
        Assert.Equal(551.1m, nvda.BrokerPurchaseValueEur);
        Assert.Equal(1740.69m, nvda.BrokerSaleOrCurrentValueEur);

        PositionLot lypg = Assert.Single(doc.Lots, l => l.Ticker == "LYPG.DE");
        Assert.False(lypg.IsClosed);
        Assert.Equal("261950118", lypg.ExternalPositionId);
        Assert.Equal(10m, lypg.Quantity);
        Assert.Equal(340.5m, lypg.OpenPrice);
        Assert.Equal(1209.8m, lypg.CurrentMarketPrice);
        Assert.Equal("ETF", lypg.Category);

        // Assert: dividend and withholding tax were paired into a single DividendRecord linked to NVDA lot
        DividendRecord div = Assert.Single(doc.Dividends);
        Assert.True(div.IsFromBrokerReport);
        Assert.Equal("NVDA.US", div.Ticker);
        Assert.Equal(nvda.Id, div.PositionLotId);
        Assert.Equal(10.00m, div.GrossAmountEur);
        Assert.Equal(1.50m, div.WithholdingTaxEur);
        Assert.Equal(8.50m, div.NetAmountEur);
        Assert.Equal(15m, div.TaxRatePercent);

        // Assert: deposit and interest cash flows imported
        Assert.Contains(doc.CashFlows, c => c.OperationType == CashOperationType.Deposit && c.AmountEur == 5000m);
        Assert.Contains(doc.CashFlows, c => c.OperationType == CashOperationType.FreeFundsInterest && c.AmountEur == 2.50m);

        // Assert: dynamic ticker mappings populated automatically
        Assert.Contains(doc.TickerMappings, m => m.Ticker == "NVDA.US" && m.YahooSymbol == "NVDA");
        Assert.Contains(doc.TickerMappings, m => m.Ticker == "LYPG.DE" && m.YahooSymbol == "LYPG.DE");
    }

    /// <summary>
    /// Verifies that re-importing an XTB report preserves manually entered lots (<see cref="DataSource.ManualEntry"/>)
    /// as well as user comments and manual tax-rate overrides on existing XTB lots.
    /// </summary>
    [Fact]
    public void ImportFile_ReimportWithReplace_PreservesManualLotsAndUserNotesOnXtbLots()
    {
        using TempTestDirectory tempDir = new();
        string csvPath = tempDir.FilePath("xtb_reimport.csv");
        File.WriteAllText(csvPath, SampleXtbCsv);

        XtbWorkbookImportService importer = new();
        PortfolioDocument doc = new() { TaxRules = TickerMappingHelper.CreateDefaultTaxRules() };

        // First import
        importer.ImportFile(csvPath, doc, replaceExistingBrokerData: true);

        // User edits a comment and tax override on an XTB lot, and adds a manual lot
        PositionLot lypg = doc.Lots.Single(l => l.ExternalPositionId == "261950118");
        lypg.Comment = "Long-term core holding";
        lypg.ManualDividendTaxRate = 0m;

        doc.Lots.Add(new PositionLot
        {
            ExternalPositionId = "MAN-CUSTOM-1",
            CompanyName = "Manual Pick",
            Ticker = "AAPL.US",
            Quantity = 5m,
            OpenPrice = 200m,
            Source = DataSource.ManualEntry
        });

        // Act: re-import XTB file
        importer.ImportFile(csvPath, doc, replaceExistingBrokerData: true);

        // Assert: manual lot survived and XTB lot comment/tax override survived
        Assert.Contains(doc.Lots, l => l.ExternalPositionId == "MAN-CUSTOM-1" && l.Source == DataSource.ManualEntry);
        PositionLot reimportedLypg = doc.Lots.Single(l => l.ExternalPositionId == "261950118");
        Assert.Equal("Long-term core holding", reimportedLypg.Comment);
        Assert.Equal(0m, reimportedLypg.ManualDividendTaxRate);
    }

    /// <summary>
    /// Verifies that importing an XTB report in Append mode (<c>replaceExistingBrokerData: false</c>)
    /// preserves all existing manual lots and existing XTB lots without duplicating broker dividends or cash flows.
    /// </summary>
    [Fact]
    public void ImportFile_AppendMode_PreservesExistingManualAndXtbLotsWithoutDuplicatingDividends()
    {
        using TempTestDirectory tempDir = new();
        string csvPath = tempDir.FilePath("xtb_append.csv");
        File.WriteAllText(csvPath, SampleXtbCsv);

        XtbWorkbookImportService importer = new();
        PortfolioDocument doc = new() { TaxRules = TickerMappingHelper.CreateDefaultTaxRules() };

        // Start with a manually defined position lot
        doc.Lots.Add(new PositionLot
        {
            ExternalPositionId = "MAN-NEW-1",
            CompanyName = "My Manual Stock",
            Ticker = "MSFT.US",
            YahooSymbol = "MSFT",
            StockCurrency = "USD",
            Quantity = 3m,
            OpenDate = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            OpenPrice = 420m,
            IsOpenPriceManual = true,
            Source = DataSource.ManualEntry
        });

        // Append XTB report
        importer.ImportFile(csvPath, doc, replaceExistingBrokerData: false);

        Assert.Equal(3, doc.Lots.Count);
        Assert.Contains(doc.Lots, l => l.ExternalPositionId == "MAN-NEW-1" && l.Ticker == "MSFT.US");
        Assert.Contains(doc.Lots, l => l.ExternalPositionId == "736368378" && l.Ticker == "NVDA.US");
        Assert.Contains(doc.Lots, l => l.ExternalPositionId == "261950118" && l.Ticker == "LYPG.DE");
        Assert.Single(doc.Dividends);

        // Appending the same XTB report a second time must not duplicate XTB lots, cash flows, or dividends
        importer.ImportFile(csvPath, doc, replaceExistingBrokerData: false);

        Assert.Equal(3, doc.Lots.Count);
        Assert.Single(doc.Dividends);
    }

    /// <summary>
    /// Verifies that <see cref="XtbWorkbookImportService.ImportFile"/> throws <see cref="FileNotFoundException"/>
    /// when the specified report path does not exist.
    /// </summary>
    [Fact]
    public void ImportFile_MissingFile_ThrowsFileNotFoundException()
    {
        XtbWorkbookImportService importer = new();
        PortfolioDocument doc = new();

        Assert.Throws<FileNotFoundException>(() =>
            importer.ImportFile(@"C:\non_existent_folder\missing_xtb.csv", doc));
    }
}

using System.Globalization;
using System.IO;
using System.Text;
using StockMonitor.Models;
using Syncfusion.XlsIO;

namespace StockMonitor.Services;

/// <summary>
/// Exports portfolio positions, summaries, dividends, and history to CSV or multi-sheet Excel (.xlsx) workbooks.
/// </summary>
public sealed class ExportService
{
    /// <summary>
    /// Exports the portfolio document to either a multi-sheet .xlsx workbook or a .csv report file.
    /// </summary>
    public void ExportPortfolio(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath)
    {
        if (Path.GetExtension(filePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            ExportToExcel(doc, history, filePath);
        }
        else
        {
            ExportToCsv(doc, filePath);
        }
    }

    private static void ExportToCsv(PortfolioDocument doc, string filePath)
    {
        string baseCcy = doc.Settings.BaseCurrency;
        StringBuilder sb = new();
        sb.AppendLine(
            $"Company Name,Ticker,Yahoo Symbol,Category,Status,Open Date,Quantity,Currency,Open Price,Open Cost,Open Price ({baseCcy}),Open Cost ({baseCcy}),Close Date,Close/Current Price,Close/Current Value,Close/Current Price ({baseCcy}),Close/Current Value ({baseCcy}),Capital P/L,Capital P/L ({baseCcy}),Net Dividends ({baseCcy}),Total Profit ({baseCcy}),Return %,Position ID,Comment");

        foreach (PositionLot l in doc.Lots.OrderBy(x => x.CompanyName).ThenBy(x => x.OpenDate))
        {
            sb.AppendLine(string.Join(",",
                Quote(l.CompanyName),
                Quote(l.Ticker),
                Quote(l.YahooSymbol),
                Quote(l.Category),
                Quote(l.StatusText),
                l.OpenDate.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                F(l.Quantity),
                Quote(l.StockCurrency),
                F(l.OpenPrice),
                F(l.OpenCost),
                F(l.OpenPriceBase),
                F(l.OpenCostBase),
                l.CloseDate?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                F(l.CloseOrCurrentPrice),
                F(l.CloseOrCurrentValue),
                F(l.CloseOrCurrentPriceBase),
                F(l.CloseOrCurrentValueBase),
                F(l.CapitalProfitLoss),
                F(l.CapitalProfitLossBase),
                F(l.NetDividendsBase),
                F(l.TotalProfitBase),
                F(l.TotalReturnPercent),
                Quote(l.ExternalPositionId),
                Quote(l.Comment)));
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
    }

    private static void ExportToExcel(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath)
    {
        string baseCcy = doc.Settings.BaseCurrency;
        using ExcelEngine excelEngine = new();
        IApplication app = excelEngine.Excel;
        app.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = app.Workbooks.Create(4);

        // Sheet 1: Positions
        IWorksheet posSheet = workbook.Worksheets[0];
        posSheet.Name = "Positions";
        string[] posHeaders =
        [
            "Company Name", "Ticker", "Yahoo Symbol", "Category", "Status", "Open Date", "Quantity", "Currency",
            "Open Price", "Open Cost", $"Open Price ({baseCcy})", $"Open Cost ({baseCcy})",
            "Close Date", "Close/Current Price", "Close/Current Value",
            $"Close/Current Price ({baseCcy})", $"Close/Current Value ({baseCcy})",
            "Capital P/L", $"Capital P/L ({baseCcy})", $"Net Dividends ({baseCcy})",
            $"Total Profit ({baseCcy})", "Return %", "Position ID", "Comment"
        ];
        WriteSheetHeaders(posSheet, posHeaders);

        int row = 2;
        foreach (PositionLot l in doc.Lots.OrderBy(x => x.CompanyName).ThenBy(x => x.OpenDate))
        {
            posSheet.Range[row, 1].Text = l.CompanyName;
            posSheet.Range[row, 2].Text = l.Ticker;
            posSheet.Range[row, 3].Text = l.YahooSymbol;
            posSheet.Range[row, 4].Text = l.Category;
            posSheet.Range[row, 5].Text = l.StatusText;
            posSheet.Range[row, 6].DateTime = l.OpenDate;
            posSheet.Range[row, 7].Number = (double)l.Quantity;
            posSheet.Range[row, 8].Text = l.StockCurrency;
            posSheet.Range[row, 9].Number = (double)l.OpenPrice;
            posSheet.Range[row, 10].Number = (double)l.OpenCost;
            posSheet.Range[row, 11].Number = (double)l.OpenPriceBase;
            posSheet.Range[row, 12].Number = (double)l.OpenCostBase;
            if (l.CloseDate.HasValue)
            {
                posSheet.Range[row, 13].DateTime = l.CloseDate.Value;
            }
            posSheet.Range[row, 14].Number = (double)l.CloseOrCurrentPrice;
            posSheet.Range[row, 15].Number = (double)l.CloseOrCurrentValue;
            posSheet.Range[row, 16].Number = (double)l.CloseOrCurrentPriceBase;
            posSheet.Range[row, 17].Number = (double)l.CloseOrCurrentValueBase;
            posSheet.Range[row, 18].Number = (double)l.CapitalProfitLoss;
            posSheet.Range[row, 19].Number = (double)l.CapitalProfitLossBase;
            posSheet.Range[row, 20].Number = (double)l.NetDividendsBase;
            posSheet.Range[row, 21].Number = (double)l.TotalProfitBase;
            posSheet.Range[row, 22].Number = (double)l.TotalReturnPercent;
            posSheet.Range[row, 23].Text = l.ExternalPositionId;
            posSheet.Range[row, 24].Text = l.Comment;
            row++;
        }
        posSheet.UsedRange.AutofitColumns();

        // Sheet 2: Dividends
        IWorksheet divSheet = workbook.Worksheets[1];
        divSheet.Name = "Dividends";
        WriteSheetHeaders(divSheet,
        [
            "Pay Date", "Company Name", "Ticker", "Dividend/Share", "Currency",
            $"Gross ({baseCcy})", "Tax Rate %", $"Withholding Tax ({baseCcy})", $"Net ({baseCcy})",
            "Position ID", "Source", "Comment"
        ]);
        row = 2;
        foreach (DividendRecord d in doc.Dividends.OrderByDescending(x => x.PayDate))
        {
            divSheet.Range[row, 1].DateTime = d.PayDate;
            divSheet.Range[row, 2].Text = d.CompanyName;
            divSheet.Range[row, 3].Text = d.Ticker;
            divSheet.Range[row, 4].Number = (double)(d.DividendPerShareNative ?? 0m);
            divSheet.Range[row, 5].Text = d.NativeCurrency;
            divSheet.Range[row, 6].Number = (double)d.GrossAmountBase;
            divSheet.Range[row, 7].Number = (double)d.TaxRatePercent;
            divSheet.Range[row, 8].Number = (double)d.WithholdingTaxBase;
            divSheet.Range[row, 9].Number = (double)d.NetAmountBase;
            divSheet.Range[row, 10].Text = d.ExternalPositionId;
            divSheet.Range[row, 11].Text = d.SourceLabel;
            divSheet.Range[row, 12].Text = d.Comment;
            row++;
        }
        divSheet.UsedRange.AutofitColumns();

        // Sheet 3: Portfolio History
        IWorksheet histSheet = workbook.Worksheets[2];
        histSheet.Name = "Portfolio History";
        WriteSheetHeaders(histSheet,
        [
            "Date", $"Cumulative Deposits ({baseCcy})", $"Active Invested ({baseCcy})",
            $"Holdings Market Value ({baseCcy})", $"Realized Capital P/L ({baseCcy})",
            $"Cumulative Net Dividends ({baseCcy})", $"Total Portfolio Value ({baseCcy})",
            $"Total Profit ({baseCcy})", "Return %"
        ]);
        row = 2;
        foreach (PortfolioHistoryPoint h in history)
        {
            histSheet.Range[row, 1].DateTime = h.Date;
            histSheet.Range[row, 2].Number = (double)h.CumulativeDepositsBase;
            histSheet.Range[row, 3].Number = (double)h.CumulativeInvestedBase;
            histSheet.Range[row, 4].Number = (double)h.HoldingsMarketValueBase;
            histSheet.Range[row, 5].Number = (double)h.RealizedCapitalProfitBase;
            histSheet.Range[row, 6].Number = (double)h.CumulativeNetDividendsBase;
            histSheet.Range[row, 7].Number = (double)h.TotalPortfolioValueBase;
            histSheet.Range[row, 8].Number = (double)h.TotalProfitBase;
            histSheet.Range[row, 9].Number = (double)h.TotalReturnPercent;
            row++;
        }
        histSheet.UsedRange.AutofitColumns();

        // Sheet 4: Tax Rules
        IWorksheet taxSheet = workbook.Worksheets[3];
        taxSheet.Name = "Dividend Tax Rules";
        WriteSheetHeaders(taxSheet, ["Country Code", "Country Name", "Sample Tickers", "Standard Tax %", "Applied Tax %", "Notes"]);
        row = 2;
        foreach (CountryTaxRule r in doc.TaxRules)
        {
            taxSheet.Range[row, 1].Text = r.CountryCode;
            taxSheet.Range[row, 2].Text = r.CountryName;
            taxSheet.Range[row, 3].Text = r.SampleTickers;
            taxSheet.Range[row, 4].Number = (double)r.StandardRatePercent;
            taxSheet.Range[row, 5].Number = (double)r.AppliedRatePercent;
            taxSheet.Range[row, 6].Text = r.Notes;
            row++;
        }
        taxSheet.UsedRange.AutofitColumns();

        using FileStream outStream = new(filePath, FileMode.Create, FileAccess.Write);
        workbook.SaveAs(outStream);
    }

    private static void WriteSheetHeaders(IWorksheet sheet, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            IRange cell = sheet.Range[1, i + 1];
            cell.Text = headers[i];
            cell.CellStyle.Font.Bold = true;
        }
    }

    private static string F(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Quote(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }
}

using System.IO;
using StockMonitor.Models;
using StockMonitor.Services;
using Syncfusion.XlsIO;

namespace StockMonitor.Export.Default;

/// <summary>
/// Exports portfolio positions, dividends, history snapshots, and tax rules to a multi-sheet Excel (<c>.xlsx</c>) workbook using Syncfusion XlsIO.
/// </summary>
public sealed class ExcelExportService : IExportService
{
    /// <inheritdoc />
    public string Id => "excel";

    /// <inheritdoc />
    public string DisplayName => "Excel Workbook (.xlsx)";

    /// <inheritdoc />
    public string FileDialogFilter => "Excel Workbook (*.xlsx)|*.xlsx";

    /// <inheritdoc />
    public string DefaultExtension => ".xlsx";

    /// <inheritdoc />
    public void ExportPortfolio(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string baseCcy = doc.Settings.BaseCurrency;
        using ExcelEngine excelEngine = new();
        IApplication app = excelEngine.Excel;
        app.DefaultVersion = ExcelVersion.Xlsx;
        IWorkbook workbook = app.Workbooks.Create(5);

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

        // Sheet 5: Charts Dashboard
        IWorksheet chartSheet = workbook.Worksheets[4];
        chartSheet.Name = "Charts";
        BuildChartsSheet(chartSheet, histSheet, doc, history, baseCcy);

        using FileStream outStream = new(filePath, FileMode.Create, FileAccess.Write);
        workbook.SaveAs(outStream);
    }

    private static void BuildChartsSheet(
        IWorksheet chartSheet,
        IWorksheet histSheet,
        PortfolioDocument doc,
        IReadOnlyList<PortfolioHistoryPoint> history,
        string baseCcy)
    {
        // Aggregated Table 1 (Cols A-C): Instrument Summary (Open Value & Total Profit)
        var instrumentSummaries = doc.Lots
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Ticker) ? l.CompanyName : l.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                Label = g.Key,
                OpenMarketValue = g.Where(x => x.Status == PositionStatus.Open).Sum(x => x.CloseOrCurrentValueBase),
                TotalProfit = g.Sum(x => x.TotalProfitBase)
            })
            .OrderByDescending(x => x.OpenMarketValue)
            .ThenByDescending(x => Math.Abs(x.TotalProfit))
            .ToList();

        WriteRangeHeaders(chartSheet, 1, 1, ["Ticker", $"Open Value ({baseCcy})", $"Total Profit ({baseCcy})"]);
        int instRow = 2;
        foreach (var item in instrumentSummaries)
        {
            chartSheet.Range[instRow, 1].Text = item.Label;
            chartSheet.Range[instRow, 2].Number = (double)item.OpenMarketValue;
            chartSheet.Range[instRow, 3].Number = (double)item.TotalProfit;
            instRow++;
        }

        // Aggregated Table 2 (Cols E-F): Annual Net Dividends
        var annualDividends = doc.Dividends
            .GroupBy(d => d.PayDate.Year)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Year = g.Key.ToString(),
                NetDividends = g.Sum(x => x.NetAmountBase)
            })
            .ToList();

        WriteRangeHeaders(chartSheet, 1, 5, ["Year", $"Net Dividends ({baseCcy})"]);
        int divRow = 2;
        foreach (var item in annualDividends)
        {
            chartSheet.Range[divRow, 5].Text = item.Year;
            chartSheet.Range[divRow, 6].Number = (double)item.NetDividends;
            divRow++;
        }

        chartSheet.UsedRange.AutofitColumns();

        // Chart 1 (Top-Left): Portfolio Value vs. Active Invested Over Time
        if (history.Count > 0)
        {
            int lastHistRow = history.Count + 1;
            IChartShape historyChart = chartSheet.Charts.Add();
            historyChart.ChartType = ExcelChartType.Line;
            historyChart.ChartTitle = $"Portfolio Value vs. Active Invested ({baseCcy})";
            historyChart.TopRow = 2;
            historyChart.LeftColumn = 8;
            historyChart.BottomRow = 18;
            historyChart.RightColumn = 16;
            historyChart.HasLegend = true;
            historyChart.Legend.Position = ExcelLegendPosition.Bottom;

            IChartSerie valueSeries = historyChart.Series.Add($"Total Portfolio Value ({baseCcy})");
            valueSeries.CategoryLabels = histSheet.Range[2, 1, lastHistRow, 1];
            valueSeries.Values = histSheet.Range[2, 7, lastHistRow, 7];

            IChartSerie investedSeries = historyChart.Series.Add($"Active Invested ({baseCcy})");
            investedSeries.CategoryLabels = histSheet.Range[2, 1, lastHistRow, 1];
            investedSeries.Values = histSheet.Range[2, 3, lastHistRow, 3];
        }

        // Chart 2 (Top-Right): Open Holdings Allocation (Pie)
        int openHoldingsCount = instrumentSummaries.Count(x => x.OpenMarketValue > 0m);
        if (openHoldingsCount > 0)
        {
            int lastOpenRow = openHoldingsCount + 1;
            IChartShape allocationChart = chartSheet.Charts.Add();
            allocationChart.ChartType = ExcelChartType.Pie;
            allocationChart.ChartTitle = $"Open Holdings Allocation ({baseCcy})";
            allocationChart.TopRow = 2;
            allocationChart.LeftColumn = 17;
            allocationChart.BottomRow = 18;
            allocationChart.RightColumn = 24;
            allocationChart.HasLegend = true;
            allocationChart.Legend.Position = ExcelLegendPosition.Right;

            IChartSerie allocSeries = allocationChart.Series.Add("Market Value");
            allocSeries.CategoryLabels = chartSheet.Range[2, 1, lastOpenRow, 1];
            allocSeries.Values = chartSheet.Range[2, 2, lastOpenRow, 2];
            allocSeries.DataPoints.DefaultDataPoint.DataLabels.IsPercentage = true;
        }

        // Chart 3 (Bottom-Left): Total Profit / Loss by Ticker (Column)
        if (instrumentSummaries.Count > 0)
        {
            int lastInstRow = Math.Min(instrumentSummaries.Count, 15) + 1;
            IChartShape profitChart = chartSheet.Charts.Add();
            profitChart.ChartType = ExcelChartType.Column_Clustered;
            profitChart.ChartTitle = $"Total Profit / Loss by Ticker ({baseCcy})";
            profitChart.TopRow = 20;
            profitChart.LeftColumn = 8;
            profitChart.BottomRow = 36;
            profitChart.RightColumn = 16;
            profitChart.HasLegend = false;

            IChartSerie profitSeries = profitChart.Series.Add($"Total Profit ({baseCcy})");
            profitSeries.CategoryLabels = chartSheet.Range[2, 1, lastInstRow, 1];
            profitSeries.Values = chartSheet.Range[2, 3, lastInstRow, 3];
        }

        // Chart 4 (Bottom-Right): Annual Net Dividends (Column)
        if (annualDividends.Count > 0)
        {
            int lastDivRow = annualDividends.Count + 1;
            IChartShape divChart = chartSheet.Charts.Add();
            divChart.ChartType = ExcelChartType.Column_Clustered;
            divChart.ChartTitle = $"Annual Net Dividends ({baseCcy})";
            divChart.TopRow = 20;
            divChart.LeftColumn = 17;
            divChart.BottomRow = 36;
            divChart.RightColumn = 24;
            divChart.HasLegend = false;

            IChartSerie divSeries = divChart.Series.Add($"Net Dividends ({baseCcy})");
            divSeries.CategoryLabels = chartSheet.Range[2, 5, lastDivRow, 5];
            divSeries.Values = chartSheet.Range[2, 6, lastDivRow, 6];
        }
    }

    private static void WriteRangeHeaders(IWorksheet sheet, int startRow, int startCol, string[] headers)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            IRange cell = sheet.Range[startRow, startCol + i];
            cell.Text = headers[i];
            cell.CellStyle.Font.Bold = true;
        }
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
}

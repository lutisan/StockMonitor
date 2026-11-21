using System.Globalization;
using System.IO;
using StockMonitor.Models;
using StockMonitor.Services;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Graphics;
using Syncfusion.Pdf.Grid;

namespace StockMonitor.Export.Pdf;

/// <summary>
/// Exports portfolio positions, dividends, history snapshots, and dividend tax rules to a formatted landscape PDF (<c>.pdf</c>) report using Syncfusion PDF.
/// </summary>
public sealed class PdfExportService : IExportService
{
    /// <inheritdoc />
    public string Id => "pdf";

    /// <inheritdoc />
    public string DisplayName => "PDF Report (.pdf)";

    /// <inheritdoc />
    public string FileDialogFilter => "PDF Document (*.pdf)|*.pdf";

    /// <inheritdoc />
    public string DefaultExtension => ".pdf";

    /// <inheritdoc />
    public void ExportPortfolio(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string baseCcy = doc.Settings.BaseCurrency;

        using PdfDocument pdfDocument = new();
        pdfDocument.PageSettings.Size = PdfPageSize.A4;
        pdfDocument.PageSettings.Orientation = PdfPageOrientation.Landscape;
        pdfDocument.PageSettings.Margins.All = 24f;

        PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 16, PdfFontStyle.Bold);
        PdfFont subtitleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9, PdfFontStyle.Regular);
        PdfFont sectionFont = new PdfStandardFont(PdfFontFamily.Helvetica, 11, PdfFontStyle.Bold);
        PdfFont headerFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Bold);
        PdfFont cellFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.5f, PdfFontStyle.Regular);

        PdfPage page = pdfDocument.Pages.Add();
        float y = 0f;

        // Report Header
        page.Graphics.DrawString("StockMonitor Portfolio Report", titleFont, PdfBrushes.DarkSlateBlue, new System.Drawing.PointF(0, y));
        y += 22f;

        int openCount = doc.Lots.Count(l => l.Status == PositionStatus.Open);
        int closedCount = doc.Lots.Count - openCount;
        string summaryLine =
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}   |   Base Currency: {baseCcy}   |   Positions: {doc.Lots.Count} ({openCount} open, {closedCount} closed)   |   Dividends: {doc.Dividends.Count}";
        page.Graphics.DrawString(summaryLine, subtitleFont, PdfBrushes.DimGray, new System.Drawing.PointF(0, y));
        y += 20f;

        // Page 1: Visual Charts Dashboard (2x2 vector charts)
        System.Drawing.SizeF clientSize = page.GetClientSize();
        System.Drawing.RectangleF dashboardArea = new(0f, y, clientSize.Width, clientSize.Height - y);
        PdfChartRenderer.DrawDashboard(page.Graphics, dashboardArea, doc, history, baseCcy);

        // Start tabular data sections cleanly on Page 2
        page = pdfDocument.Pages.Add();
        y = 0f;

        // Section 1: Positions
        (page, y) = DrawSectionWithGrid(
            pdfDocument,
            page,
            y,
            "1. Positions",
            sectionFont,
            headerFont,
            cellFont,
            [
                "Company", "Ticker", "Status", "Open Date", "Qty", "Ccy",
                "Open Price", "Close/Curr", $"Open Cost ({baseCcy})", $"Value ({baseCcy})",
                $"Capital P/L ({baseCcy})", $"Net Div ({baseCcy})", $"Total Profit ({baseCcy})", "Return %"
            ],
            doc.Lots
                .OrderBy(x => x.CompanyName)
                .ThenBy(x => x.OpenDate)
                .Select(l => new[]
                {
                    SafeText(l.CompanyName),
                    SafeText(l.Ticker),
                    SafeText(l.StatusText),
                    l.OpenDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    F(l.Quantity),
                    SafeText(l.StockCurrency),
                    F2(l.OpenPrice),
                    F2(l.CloseOrCurrentPrice),
                    F2(l.OpenCostBase),
                    F2(l.CloseOrCurrentValueBase),
                    F2(l.CapitalProfitLossBase),
                    F2(l.NetDividendsBase),
                    F2(l.TotalProfitBase),
                    $"{F2(l.TotalReturnPercent)}%"
                }));

        // Section 2: Dividends
        if (doc.Dividends.Count > 0)
        {
            (page, y) = DrawSectionWithGrid(
                pdfDocument,
                page,
                y + 14f,
                "2. Dividends",
                sectionFont,
                headerFont,
                cellFont,
                [
                    "Pay Date", "Company", "Ticker", "Div/Share", "Ccy",
                    $"Gross ({baseCcy})", "Tax %", $"Tax ({baseCcy})", $"Net ({baseCcy})", "Source"
                ],
                doc.Dividends
                    .OrderByDescending(x => x.PayDate)
                    .Select(d => new[]
                    {
                        d.PayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        SafeText(d.CompanyName),
                        SafeText(d.Ticker),
                        F(d.DividendPerShareNative ?? 0m),
                        SafeText(d.NativeCurrency),
                        F2(d.GrossAmountBase),
                        $"{F2(d.TaxRatePercent)}%",
                        F2(d.WithholdingTaxBase),
                        F2(d.NetAmountBase),
                        SafeText(d.SourceLabel)
                    }));
        }

        // Section 3: Portfolio History
        if (history.Count > 0)
        {
            (page, y) = DrawSectionWithGrid(
                pdfDocument,
                page,
                y + 14f,
                "3. Portfolio History",
                sectionFont,
                headerFont,
                cellFont,
                [
                    "Date", $"Deposits ({baseCcy})", $"Active Invested ({baseCcy})",
                    $"Market Value ({baseCcy})", $"Realized P/L ({baseCcy})",
                    $"Net Dividends ({baseCcy})", $"Total Value ({baseCcy})",
                    $"Total Profit ({baseCcy})", "Return %"
                ],
                history.Select(h => new[]
                {
                    h.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    F2(h.CumulativeDepositsBase),
                    F2(h.CumulativeInvestedBase),
                    F2(h.HoldingsMarketValueBase),
                    F2(h.RealizedCapitalProfitBase),
                    F2(h.CumulativeNetDividendsBase),
                    F2(h.TotalPortfolioValueBase),
                    F2(h.TotalProfitBase),
                    $"{F2(h.TotalReturnPercent)}%"
                }));
        }

        // Section 4: Dividend Tax Rules
        if (doc.TaxRules.Count > 0)
        {
            DrawSectionWithGrid(
                pdfDocument,
                page,
                y + 14f,
                "4. Dividend Tax Rules",
                sectionFont,
                headerFont,
                cellFont,
                ["Country Code", "Country Name", "Sample Tickers", "Standard Tax %", "Applied Tax %", "Notes"],
                doc.TaxRules.Select(r => new[]
                {
                    SafeText(r.CountryCode),
                    SafeText(r.CountryName),
                    SafeText(r.SampleTickers),
                    $"{F2(r.StandardRatePercent)}%",
                    $"{F2(r.AppliedRatePercent)}%",
                    SafeText(r.Notes)
                }));
        }

        using FileStream outStream = new(filePath, FileMode.Create, FileAccess.Write);
        pdfDocument.Save(outStream);
    }

    private static (PdfPage Page, float BottomY) DrawSectionWithGrid(
        PdfDocument pdfDocument,
        PdfPage currentPage,
        float startY,
        string sectionTitle,
        PdfFont sectionFont,
        PdfFont headerFont,
        PdfFont cellFont,
        string[] headers,
        IEnumerable<string[]> rows)
    {
        float pageHeight = currentPage.GetClientSize().Height;
        if (startY > pageHeight - 70f)
        {
            currentPage = pdfDocument.Pages.Add();
            startY = 0f;
        }

        currentPage.Graphics.DrawString(sectionTitle, sectionFont, PdfBrushes.DarkSlateBlue, new System.Drawing.PointF(0, startY));
        float gridTopY = startY + 16f;

        PdfGrid grid = new();
        grid.Columns.Add(headers.Length);
        grid.Headers.Add(1);
        grid.RepeatHeader = true;

        PdfGridRow headerRow = grid.Headers[0];
        for (int i = 0; i < headers.Length; i++)
        {
            headerRow.Cells[i].Value = headers[i];
        }

        PdfGridCellStyle headerStyle = new()
        {
            Font = headerFont,
            BackgroundBrush = new PdfSolidBrush(new PdfColor(44, 62, 80)),
            TextBrush = PdfBrushes.White
        };
        headerRow.ApplyStyle(headerStyle);

        PdfGridCellStyle altRowStyle = new()
        {
            BackgroundBrush = new PdfSolidBrush(new PdfColor(245, 247, 250))
        };

        int rowIndex = 0;
        foreach (string[] values in rows)
        {
            PdfGridRow gridRow = grid.Rows.Add();
            for (int i = 0; i < headers.Length && i < values.Length; i++)
            {
                gridRow.Cells[i].Value = values[i];
            }

            if (rowIndex % 2 == 1)
            {
                gridRow.ApplyStyle(altRowStyle);
            }

            rowIndex++;
        }

        grid.Style.Font = cellFont;
        grid.Style.CellPadding = new PdfPaddings(3, 3, 2, 2);
        grid.AllowRowBreakAcrossPages = false;

        PdfGridLayoutFormat layoutFormat = new()
        {
            Layout = PdfLayoutType.Paginate,
            Break = PdfLayoutBreakType.FitPage
        };

        PdfLayoutResult result = grid.Draw(
            currentPage,
            new System.Drawing.RectangleF(0, gridTopY, currentPage.GetClientSize().Width, pageHeight - gridTopY),
            layoutFormat);

        return (result.Page, result.Bounds.Bottom);
    }

    private static string F(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string F2(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string SafeText(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

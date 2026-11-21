using System.Drawing;
using System.Globalization;
using StockMonitor.Models;
using Syncfusion.Pdf.Graphics;

namespace StockMonitor.Export.Pdf;

/// <summary>
/// Draws vector portfolio charts (line, pie, profit/loss bar, and annual dividend column charts)
/// directly onto a <see cref="PdfGraphics"/> canvas.
/// </summary>
internal static class PdfChartRenderer
{
    private static readonly PdfColor[] Palette =
    [
        new PdfColor(37, 99, 235),   // Blue
        new PdfColor(16, 185, 129),  // Emerald
        new PdfColor(245, 158, 11),  // Amber
        new PdfColor(139, 92, 246),  // Violet
        new PdfColor(236, 72, 153),  // Pink
        new PdfColor(6, 182, 212),   // Cyan
        new PdfColor(249, 115, 22),  // Orange
        new PdfColor(100, 116, 139)  // Slate (Other)
    ];

    private static readonly PdfColor CardBorderColor = new(218, 224, 232);
    private static readonly PdfColor CardBackgroundColor = new(252, 253, 255);
    private static readonly PdfColor GridLineColor = new(230, 235, 240);
    private static readonly PdfColor AxisColor = new(148, 163, 184);
    private static readonly PdfColor PositiveColor = new(22, 163, 74);
    private static readonly PdfColor NegativeColor = new(220, 38, 38);

    /// <summary>
    /// Draws a 2x2 visual chart dashboard inside <paramref name="area"/>.
    /// </summary>
    public static void DrawDashboard(
        PdfGraphics graphics,
        RectangleF area,
        PortfolioDocument doc,
        IReadOnlyList<PortfolioHistoryPoint> history,
        string baseCcy)
    {
        const float gap = 12f;
        float cardWidth = (area.Width - gap) / 2f;
        float cardHeight = (area.Height - gap) / 2f;

        RectangleF topLeft = new(area.X, area.Y, cardWidth, cardHeight);
        RectangleF topRight = new(area.X + cardWidth + gap, area.Y, cardWidth, cardHeight);
        RectangleF bottomLeft = new(area.X, area.Y + cardHeight + gap, cardWidth, cardHeight);
        RectangleF bottomRight = new(area.X + cardWidth + gap, area.Y + cardHeight + gap, cardWidth, cardHeight);

        DrawHistoryLineChart(graphics, topLeft, history, baseCcy);
        DrawHoldingsPieChart(graphics, topRight, doc, baseCcy);
        DrawProfitByTickerChart(graphics, bottomLeft, doc, baseCcy);
        DrawAnnualDividendsChart(graphics, bottomRight, doc, baseCcy);
    }

    private static void DrawHistoryLineChart(
        PdfGraphics g,
        RectangleF card,
        IReadOnlyList<PortfolioHistoryPoint> history,
        string baseCcy)
    {
        RectangleF plot = DrawCardFrame(g, card, $"Portfolio Value vs. Active Invested ({baseCcy})");
        if (history.Count == 0)
        {
            DrawEmptyMessage(g, plot, "No portfolio history snapshots available.");
            return;
        }

        PdfFont axisFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        PdfFont legendFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7f);

        // Legend at top of plot
        float legendY = plot.Y;
        DrawLegendSwatch(g, plot.X, legendY, Palette[0], "Total Portfolio Value", legendFont);
        DrawLegendSwatch(g, plot.X + 125f, legendY, Palette[2], "Active Invested", legendFont);

        RectangleF chartRect = new(plot.X + 46f, plot.Y + 14f, plot.Width - 52f, plot.Height - 32f);
        if (chartRect.Width <= 10 || chartRect.Height <= 10)
        {
            return;
        }

        decimal minVal = history.Min(h => Math.Min(h.TotalPortfolioValueBase, h.CumulativeInvestedBase));
        decimal maxVal = history.Max(h => Math.Max(h.TotalPortfolioValueBase, h.CumulativeInvestedBase));
        if (minVal > 0m)
        {
            minVal = 0m;
        }
        if (maxVal <= minVal)
        {
            maxVal = minVal + 100m;
        }

        float minF = (float)minVal;
        float maxF = (float)maxVal;
        float span = maxF - minF;

        // Horizontal gridlines and Y-axis labels
        const int gridSteps = 4;
        PdfPen gridPen = new(GridLineColor, 0.5f);
        PdfStringFormat rightAlign = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle);
        for (int i = 0; i <= gridSteps; i++)
        {
            float frac = i / (float)gridSteps;
            float y = chartRect.Bottom - frac * chartRect.Height;
            g.DrawLine(gridPen, new PointF(chartRect.Left, y), new PointF(chartRect.Right, y));
            float labelVal = minF + frac * span;
            g.DrawString(FormatCompact(labelVal), axisFont, PdfBrushes.DimGray, new RectangleF(plot.X, y - 6f, 42f, 12f), rightAlign);
        }

        // Axes
        PdfPen axisPen = new(AxisColor, 0.8f);
        g.DrawLine(axisPen, new PointF(chartRect.Left, chartRect.Top), new PointF(chartRect.Left, chartRect.Bottom));
        g.DrawLine(axisPen, new PointF(chartRect.Left, chartRect.Bottom), new PointF(chartRect.Right, chartRect.Bottom));

        PointF[] valuePoints = new PointF[history.Count];
        PointF[] investedPoints = new PointF[history.Count];

        for (int i = 0; i < history.Count; i++)
        {
            float x = history.Count == 1
                ? chartRect.Left + chartRect.Width / 2f
                : chartRect.Left + (i / (float)(history.Count - 1)) * chartRect.Width;

            float yVal = chartRect.Bottom - (((float)history[i].TotalPortfolioValueBase - minF) / span) * chartRect.Height;
            float yInv = chartRect.Bottom - (((float)history[i].CumulativeInvestedBase - minF) / span) * chartRect.Height;

            valuePoints[i] = new PointF(x, yVal);
            investedPoints[i] = new PointF(x, yInv);
        }

        PdfPen investedPen = new(Palette[2], 1.4f);
        PdfPen valuePen = new(Palette[0], 1.8f);

        if (history.Count == 1)
        {
            g.DrawEllipse(null, new PdfSolidBrush(Palette[2]), new RectangleF(investedPoints[0].X - 3, investedPoints[0].Y - 3, 6, 6));
            g.DrawEllipse(null, new PdfSolidBrush(Palette[0]), new RectangleF(valuePoints[0].X - 3, valuePoints[0].Y - 3, 6, 6));
        }
        else
        {
            for (int i = 1; i < history.Count; i++)
            {
                g.DrawLine(investedPen, investedPoints[i - 1], investedPoints[i]);
                g.DrawLine(valuePen, valuePoints[i - 1], valuePoints[i]);
            }
        }

        // X-axis date ticks (up to 4 evenly spaced labels)
        int tickCount = Math.Min(4, history.Count);
        PdfStringFormat centerAlign = new(PdfTextAlignment.Center, PdfVerticalAlignment.Top);
        for (int t = 0; t < tickCount; t++)
        {
            int idx = tickCount == 1 ? 0 : (int)Math.Round(t * (history.Count - 1) / (double)(tickCount - 1));
            float x = valuePoints[idx].X;
            string dateText = history[idx].Date.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            g.DrawString(dateText, axisFont, PdfBrushes.DimGray, new RectangleF(x - 24f, chartRect.Bottom + 3f, 48f, 12f), centerAlign);
        }
    }

    private static void DrawHoldingsPieChart(
        PdfGraphics g,
        RectangleF card,
        PortfolioDocument doc,
        string baseCcy)
    {
        RectangleF plot = DrawCardFrame(g, card, $"Open Holdings Allocation ({baseCcy})");

        var grouped = doc.Lots
            .Where(l => l.Status == PositionStatus.Open && l.CloseOrCurrentValueBase > 0m)
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Ticker) ? l.CompanyName : l.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(grp => (Label: grp.Key, Value: grp.Sum(x => x.CloseOrCurrentValueBase)))
            .OrderByDescending(x => x.Value)
            .ToList();

        if (grouped.Count == 0)
        {
            DrawEmptyMessage(g, plot, "No open holdings with positive market value.");
            return;
        }

        // Keep top 7 slices and group remaining into "Other"
        List<(string Label, decimal Value)> slices = [];
        const int maxSlices = 7;
        if (grouped.Count <= maxSlices)
        {
            slices.AddRange(grouped);
        }
        else
        {
            slices.AddRange(grouped.Take(maxSlices - 1));
            decimal otherTotal = grouped.Skip(maxSlices - 1).Sum(x => x.Value);
            slices.Add(("Other", otherTotal));
        }

        decimal total = slices.Sum(x => x.Value);
        if (total <= 0m)
        {
            DrawEmptyMessage(g, plot, "No positive open holdings value.");
            return;
        }

        float diameter = Math.Min(plot.Height - 10f, plot.Width * 0.44f);
        RectangleF pieRect = new(plot.X + 8f, plot.Y + (plot.Height - diameter) / 2f, diameter, diameter);

        float startAngle = -90f;
        PdfPen sliceBorder = new(PdfColor.Empty, 0f);
        PdfPen whiteBorder = new(new PdfColor(255, 255, 255), 0.8f);

        for (int i = 0; i < slices.Count; i++)
        {
            float sweep = (float)(slices[i].Value / total * 360m);
            PdfSolidBrush brush = new(Palette[i % Palette.Length]);
            g.DrawPie(slices.Count > 1 ? whiteBorder : sliceBorder, brush, pieRect, startAngle, sweep);
            startAngle += sweep;
        }

        // Right-side legend
        float legendX = pieRect.Right + 16f;
        float legendWidth = plot.Right - legendX;
        float legendY = plot.Y + 8f;
        PdfFont legendFont = new PdfStandardFont(PdfFontFamily.Helvetica, 7.2f);
        PdfStringFormat rightAlign = new(PdfTextAlignment.Right, PdfVerticalAlignment.Top);

        for (int i = 0; i < slices.Count; i++)
        {
            float pct = (float)(slices[i].Value / total * 100m);
            PdfSolidBrush swatchBrush = new(Palette[i % Palette.Length]);
            g.DrawRectangle(null, swatchBrush, new RectangleF(legendX, legendY + 2f, 8f, 8f));
            g.DrawString(Truncate(slices[i].Label, 14), legendFont, PdfBrushes.Black, new PointF(legendX + 12f, legendY));
            string valStr = $"{FormatCompact((float)slices[i].Value)} ({pct:0.0}%)";
            g.DrawString(valStr, legendFont, PdfBrushes.DimGray, new RectangleF(legendX + 12f, legendY, legendWidth - 14f, 12f), rightAlign);
            legendY += 15f;
        }
    }

    private static void DrawProfitByTickerChart(
        PdfGraphics g,
        RectangleF card,
        PortfolioDocument doc,
        string baseCcy)
    {
        RectangleF plot = DrawCardFrame(g, card, $"Total Profit / Loss by Ticker ({baseCcy})");

        var items = doc.Lots
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Ticker) ? l.CompanyName : l.Ticker, StringComparer.OrdinalIgnoreCase)
            .Select(grp => (Label: grp.Key, Profit: grp.Sum(x => x.TotalProfitBase)))
            .OrderByDescending(x => Math.Abs(x.Profit))
            .Take(10)
            .OrderByDescending(x => x.Profit)
            .ToList();

        if (items.Count == 0)
        {
            DrawEmptyMessage(g, plot, "No positions available.");
            return;
        }

        RectangleF chartRect = new(plot.X + 44f, plot.Y + 10f, plot.Width - 50f, plot.Height - 32f);
        float minVal = Math.Min(0f, (float)items.Min(x => x.Profit));
        float maxVal = Math.Max(0f, (float)items.Max(x => x.Profit));
        if (Math.Abs(maxVal - minVal) < 0.01f)
        {
            maxVal = minVal + 100f;
        }

        float span = maxVal - minVal;
        float zeroY = chartRect.Bottom - ((0f - minVal) / span) * chartRect.Height;

        PdfFont axisFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        PdfStringFormat rightAlign = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle);
        PdfStringFormat centerAlign = new(PdfTextAlignment.Center, PdfVerticalAlignment.Top);

        // Y-axis max, zero, min labels
        PdfPen gridPen = new(GridLineColor, 0.5f);
        g.DrawLine(gridPen, new PointF(chartRect.Left, chartRect.Top), new PointF(chartRect.Right, chartRect.Top));
        g.DrawString(FormatCompact(maxVal), axisFont, PdfBrushes.DimGray, new RectangleF(plot.X, chartRect.Top - 6f, 40f, 12f), rightAlign);

        if (minVal < 0f)
        {
            g.DrawLine(gridPen, new PointF(chartRect.Left, chartRect.Bottom), new PointF(chartRect.Right, chartRect.Bottom));
            g.DrawString(FormatCompact(minVal), axisFont, PdfBrushes.DimGray, new RectangleF(plot.X, chartRect.Bottom - 6f, 40f, 12f), rightAlign);
        }

        PdfPen axisPen = new(AxisColor, 0.8f);
        g.DrawLine(axisPen, new PointF(chartRect.Left, chartRect.Top), new PointF(chartRect.Left, chartRect.Bottom));
        g.DrawLine(axisPen, new PointF(chartRect.Left, zeroY), new PointF(chartRect.Right, zeroY));

        float slotWidth = chartRect.Width / items.Count;
        float barWidth = Math.Min(28f, slotWidth * 0.62f);

        for (int i = 0; i < items.Count; i++)
        {
            float val = (float)items[i].Profit;
            float cx = chartRect.Left + i * slotWidth + slotWidth / 2f;
            float barX = cx - barWidth / 2f;
            float valY = chartRect.Bottom - ((val - minVal) / span) * chartRect.Height;

            float top = Math.Min(zeroY, valY);
            float height = Math.Max(1.5f, Math.Abs(valY - zeroY));
            PdfSolidBrush barBrush = new(val >= 0f ? PositiveColor : NegativeColor);
            g.DrawRectangle(null, barBrush, new RectangleF(barX, top, barWidth, height));

            // Value label
            float labelY = val >= 0f ? Math.Max(plot.Y, top - 10f) : Math.Min(chartRect.Bottom - 2f, top + height + 1f);
            g.DrawString(FormatCompact(val), axisFont, PdfBrushes.Black, new RectangleF(cx - 22f, labelY, 44f, 10f), centerAlign);

            // Ticker label along bottom
            g.DrawString(Truncate(items[i].Label, 7), axisFont, PdfBrushes.DimGray, new RectangleF(cx - slotWidth / 2f, chartRect.Bottom + 3f, slotWidth, 12f), centerAlign);
        }
    }

    private static void DrawAnnualDividendsChart(
        PdfGraphics g,
        RectangleF card,
        PortfolioDocument doc,
        string baseCcy)
    {
        RectangleF plot = DrawCardFrame(g, card, $"Annual Net Dividends ({baseCcy})");

        var annual = doc.Dividends
            .GroupBy(d => d.PayDate.Year)
            .OrderBy(grp => grp.Key)
            .Select(grp => (Year: grp.Key.ToString(CultureInfo.InvariantCulture), Net: grp.Sum(x => x.NetAmountBase)))
            .ToList();

        if (annual.Count == 0)
        {
            DrawEmptyMessage(g, plot, "No dividend records available.");
            return;
        }

        RectangleF chartRect = new(plot.X + 44f, plot.Y + 12f, plot.Width - 50f, plot.Height - 32f);
        float maxVal = Math.Max(1f, (float)annual.Max(x => x.Net));

        PdfFont axisFont = new PdfStandardFont(PdfFontFamily.Helvetica, 6.5f);
        PdfStringFormat rightAlign = new(PdfTextAlignment.Right, PdfVerticalAlignment.Middle);
        PdfStringFormat centerAlign = new(PdfTextAlignment.Center, PdfVerticalAlignment.Top);

        PdfPen gridPen = new(GridLineColor, 0.5f);
        for (int i = 0; i <= 3; i++)
        {
            float frac = i / 3f;
            float y = chartRect.Bottom - frac * chartRect.Height;
            g.DrawLine(gridPen, new PointF(chartRect.Left, y), new PointF(chartRect.Right, y));
            g.DrawString(FormatCompact(frac * maxVal), axisFont, PdfBrushes.DimGray, new RectangleF(plot.X, y - 6f, 40f, 12f), rightAlign);
        }

        PdfPen axisPen = new(AxisColor, 0.8f);
        g.DrawLine(axisPen, new PointF(chartRect.Left, chartRect.Top), new PointF(chartRect.Left, chartRect.Bottom));
        g.DrawLine(axisPen, new PointF(chartRect.Left, chartRect.Bottom), new PointF(chartRect.Right, chartRect.Bottom));

        float slotWidth = chartRect.Width / annual.Count;
        float barWidth = Math.Min(36f, slotWidth * 0.55f);
        PdfSolidBrush barBrush = new(Palette[1]);

        for (int i = 0; i < annual.Count; i++)
        {
            float val = (float)annual[i].Net;
            float cx = chartRect.Left + i * slotWidth + slotWidth / 2f;
            float barX = cx - barWidth / 2f;
            float height = Math.Max(1.5f, (val / maxVal) * chartRect.Height);
            float top = chartRect.Bottom - height;

            g.DrawRectangle(null, barBrush, new RectangleF(barX, top, barWidth, height));
            g.DrawString(FormatCompact(val), axisFont, PdfBrushes.Black, new RectangleF(cx - 24f, Math.Max(plot.Y, top - 10f), 48f, 10f), centerAlign);
            g.DrawString(annual[i].Year, axisFont, PdfBrushes.DimGray, new RectangleF(cx - 24f, chartRect.Bottom + 3f, 48f, 12f), centerAlign);
        }
    }

    private static RectangleF DrawCardFrame(PdfGraphics g, RectangleF card, string title)
    {
        g.DrawRectangle(new PdfPen(CardBorderColor, 0.75f), new PdfSolidBrush(CardBackgroundColor), card);
        PdfFont titleFont = new PdfStandardFont(PdfFontFamily.Helvetica, 9f, PdfFontStyle.Bold);
        g.DrawString(title, titleFont, PdfBrushes.DarkSlateBlue, new PointF(card.X + 8f, card.Y + 6f));
        return new RectangleF(card.X + 8f, card.Y + 22f, card.Width - 16f, card.Height - 28f);
    }

    private static void DrawEmptyMessage(PdfGraphics g, RectangleF plot, string message)
    {
        PdfFont font = new PdfStandardFont(PdfFontFamily.Helvetica, 8f, PdfFontStyle.Italic);
        PdfStringFormat center = new(PdfTextAlignment.Center, PdfVerticalAlignment.Middle);
        g.DrawString(message, font, PdfBrushes.Gray, plot, center);
    }

    private static void DrawLegendSwatch(PdfGraphics g, float x, float y, PdfColor color, string label, PdfFont font)
    {
        g.DrawRectangle(null, new PdfSolidBrush(color), new RectangleF(x, y + 2f, 8f, 6f));
        g.DrawString(label, font, PdfBrushes.DimGray, new PointF(x + 11f, y));
    }

    private static string FormatCompact(float value)
    {
        float abs = Math.Abs(value);
        if (abs >= 1_000_000f)
        {
            return $"{(value / 1_000_000f).ToString("0.0", CultureInfo.InvariantCulture)}M";
        }
        if (abs >= 1_000f)
        {
            return $"{(value / 1_000f).ToString("0.0", CultureInfo.InvariantCulture)}k";
        }
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 1)] + ".";
}

using System.Globalization;
using System.IO;
using System.Text;
using StockMonitor.Models;
using StockMonitor.Services;

namespace StockMonitor.Export.Default;

/// <summary>
/// Exports portfolio positions to a UTF-8 comma-separated values (<c>.csv</c>) report file.
/// </summary>
public sealed class CsvExportService : IExportService
{
    /// <inheritdoc />
    public string Id => "csv";

    /// <inheritdoc />
    public string DisplayName => "CSV Report (.csv)";

    /// <inheritdoc />
    public string FileDialogFilter => "CSV File (*.csv)|*.csv";

    /// <inheritdoc />
    public string DefaultExtension => ".csv";

    /// <inheritdoc />
    public void ExportPortfolio(PortfolioDocument doc, IReadOnlyList<PortfolioHistoryPoint> history, string filePath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

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

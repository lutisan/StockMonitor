using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using StockMonitor.Models;
using Syncfusion.XlsIO;

namespace StockMonitor.Services;

/// <summary>
/// Imports XTB Excel (.xlsx) and CSV reports containing Closed Positions, Cash Operations, and Open Positions.
/// </summary>
public sealed partial class XtbWorkbookImportService
{
    [GeneratedRegex(@"(?<ccy>[A-Z]{3})\s+(?<rate>\d+(?:\.\d+)?)\s*/\s*SHR", RegexOptions.IgnoreCase)]
    private static partial Regex DividendPerShareRegex();

    [GeneratedRegex(@"(?<ccy>[A-Z]{3})\s+WHT\s+(?<pct>\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase)]
    private static partial Regex WithholdingTaxRateRegex();

    /// <summary>
    /// Imports an XTB report from an .xlsx workbook or .csv file into the target portfolio document.
    /// </summary>
    public void ImportFile(string filePath, PortfolioDocument document, bool replaceExistingXtbData = true)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("XTB report file was not found.", filePath);
        }

        List<List<string>> allRows = Path.GetExtension(filePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
            || Path.GetExtension(filePath).Equals(".xls", StringComparison.OrdinalIgnoreCase)
            ? ReadExcelRows(filePath)
            : ReadCsvRows(filePath);

        ImportRows(allRows, document, replaceExistingXtbData);
        document.Settings.LastImportedXtbPath = filePath;
    }

    private static List<List<string>> ReadExcelRows(string filePath)
    {
        List<List<string>> rows = [];
        using ExcelEngine excelEngine = new();
        IApplication application = excelEngine.Excel;
        using FileStream stream = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        IWorkbook workbook = application.Workbooks.Open(stream);

        foreach (IWorksheet sheet in workbook.Worksheets)
        {
            string sheetTitle = sheet.Name.Trim();
            if (sheetTitle.Contains("Closed", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(["Closed Positions"]);
            }
            else if (sheetTitle.Contains("Cash", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(["Cash Operations"]);
            }
            else if (sheetTitle.Contains("Open", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(["Open Positions"]);
            }

            IRange usedRange = sheet.UsedRange;
            if (usedRange == null || usedRange.LastRow < 1)
            {
                continue;
            }

            int lastRow = usedRange.LastRow;
            int lastCol = Math.Min(usedRange.LastColumn, 35);

            for (int r = 1; r <= lastRow; r++)
            {
                List<string> rowValues = new(lastCol);
                bool hasValue = false;
                for (int c = 1; c <= lastCol; c++)
                {
                    IRange cell = sheet.Range[r, c];
                    string val = GetExcelCellText(cell);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        hasValue = true;
                    }
                    rowValues.Add(val);
                }

                if (hasValue)
                {
                    rows.Add(rowValues);
                }
            }
        }

        return rows;
    }

    private static string GetExcelCellText(IRange cell)
    {
        if (cell.HasDateTime)
        {
            return cell.DateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (cell.HasNumber && !double.IsNaN(cell.Number))
        {
            return cell.Number.ToString(CultureInfo.InvariantCulture);
        }

        return (cell.Value ?? cell.DisplayText ?? string.Empty).Trim();
    }

    private static List<List<string>> ReadCsvRows(string filePath)
    {
        List<List<string>> rows = [];
        foreach (string line in File.ReadLines(filePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            rows.Add(ParseCsvLine(line));
        }

        return rows;
    }

    private static List<string> ParseCsvLine(string line)
    {
        List<string> fields = [];
        bool inQuotes = false;
        int start = 0;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                fields.Add(Unquote(line[start..i]));
                start = i + 1;
            }
        }

        fields.Add(Unquote(line[start..]));
        return fields;
    }

    private static string Unquote(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            return trimmed[1..^1].Replace("\"\"", "\"");
        }

        return trimmed;
    }

    /// <summary>
    /// Parses pre-extracted tabular rows from an XTB report into <paramref name="document"/>.
    /// </summary>
    /// <param name="rows">Table rows across Closed Positions, Cash Operations, and Open Positions.</param>
    /// <param name="document">Target portfolio document.</param>
    /// <param name="replaceExistingXtbData">When <see langword="true"/>, replaces previously imported XTB rows while keeping manual entries.</param>
    public void ImportRows(List<List<string>> rows, PortfolioDocument document, bool replaceExistingXtbData = true)
    {
        Dictionary<string, (string Comment, decimal? ManualTaxRate)> preservedOverrides = new(StringComparer.OrdinalIgnoreCase);

        if (replaceExistingXtbData)
        {
            foreach (PositionLot existing in document.Lots.Where(l => l.Source == DataSource.XtbImport && !string.IsNullOrWhiteSpace(l.ExternalPositionId)))
            {
                if (!string.IsNullOrWhiteSpace(existing.Comment) || existing.ManualDividendTaxRate.HasValue)
                {
                    preservedOverrides[existing.ExternalPositionId] = (existing.Comment, existing.ManualDividendTaxRate);
                }
            }

            document.Lots.RemoveAll(l => l.Source == DataSource.XtbImport);
            document.Dividends.RemoveAll(d => d.IsFromBrokerReport);
            document.CashFlows.Clear();
        }

        if (document.TaxRules.Count == 0)
        {
            document.TaxRules.AddRange(TickerMappingHelper.CreateDefaultTaxRules());
        }

        string section = string.Empty;
        Dictionary<string, int> headerMap = new(StringComparer.OrdinalIgnoreCase);
        string currentOpenInstrumentName = string.Empty;
        string currentOpenCategory = "STOCK";

        foreach (List<string> row in rows)
        {
            if (row.Count == 0)
            {
                continue;
            }

            string c0 = row[0].Trim();
            string c1 = row.Count > 1 ? row[1].Trim() : string.Empty;

            if (c0.Equals("Account number", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c1))
            {
                document.AccountNumber = c1;
                continue;
            }

            if ((c0.Equals("Date to (UTC)", StringComparison.OrdinalIgnoreCase) ||
                 c0.Equals("Data as of report generated", StringComparison.OrdinalIgnoreCase)) &&
                TryParseDate(c1, out DateTime reportDate))
            {
                document.ReportGeneratedUtc = reportDate;
                continue;
            }

            if (c0.Equals("Closed Positions", StringComparison.OrdinalIgnoreCase))
            {
                section = "CLOSED";
                headerMap.Clear();
                continue;
            }

            if (c0.Equals("Cash Operations", StringComparison.OrdinalIgnoreCase))
            {
                section = "CASH";
                headerMap.Clear();
                continue;
            }

            if (c0.Equals("Open Positions", StringComparison.OrdinalIgnoreCase))
            {
                section = "OPEN";
                headerMap.Clear();
                continue;
            }

            // Detect table header rows
            if (c0.Equals("Instrument", StringComparison.OrdinalIgnoreCase) && c1.Equals("Ticker", StringComparison.OrdinalIgnoreCase))
            {
                section = "CLOSED";
                BuildHeaderMap(row, headerMap);
                continue;
            }

            if (c0.Equals("Type", StringComparison.OrdinalIgnoreCase) && c1.Equals("Instrument", StringComparison.OrdinalIgnoreCase))
            {
                section = "CASH";
                BuildHeaderMap(row, headerMap);
                continue;
            }

            if (c0.Equals("Product", StringComparison.OrdinalIgnoreCase) &&
                c1.Equals("Instrument/Position", StringComparison.OrdinalIgnoreCase))
            {
                section = "OPEN";
                BuildHeaderMap(row, headerMap);
                continue;
            }

            if (headerMap.Count == 0)
            {
                continue;
            }

            switch (section)
            {
                case "CLOSED":
                    ParseClosedPositionRow(row, headerMap, document);
                    break;
                case "OPEN":
                    ParseOpenPositionRow(row, headerMap, document, ref currentOpenInstrumentName, ref currentOpenCategory);
                    break;
                case "CASH":
                    ParseCashOperationRow(row, headerMap, document);
                    break;
            }
        }

        if (preservedOverrides.Count > 0)
        {
            foreach (PositionLot lot in document.Lots.Where(l => l.Source == DataSource.XtbImport && !string.IsNullOrWhiteSpace(l.ExternalPositionId)))
            {
                if (preservedOverrides.TryGetValue(lot.ExternalPositionId, out (string Comment, decimal? ManualTaxRate) saved))
                {
                    if (!string.IsNullOrWhiteSpace(saved.Comment))
                    {
                        lot.Comment = saved.Comment;
                    }

                    if (saved.ManualTaxRate.HasValue)
                    {
                        lot.ManualDividendTaxRate = saved.ManualTaxRate;
                    }
                }
            }
        }

        ReconcileCashOperationsWithPositions(document);
        TickerMappingHelper.SynchronizeDocumentMappings(document);
    }

    private static void BuildHeaderMap(List<string> row, Dictionary<string, int> headerMap)
    {
        headerMap.Clear();
        for (int i = 0; i < row.Count; i++)
        {
            string h = row[i].Trim();
            if (!string.IsNullOrWhiteSpace(h) && !headerMap.ContainsKey(h))
            {
                headerMap[h] = i;
            }
        }
    }

    private static string GetCol(List<string> row, Dictionary<string, int> map, string name)
    {
        return map.TryGetValue(name, out int idx) && idx < row.Count ? row[idx].Trim() : string.Empty;
    }

    private static void ParseClosedPositionRow(List<string> row, Dictionary<string, int> map, PortfolioDocument doc)
    {
        string instrument = GetCol(row, map, "Instrument");
        string ticker = GetCol(row, map, "Ticker");
        string posId = GetCol(row, map, "Position ID");
        if (string.IsNullOrWhiteSpace(instrument) || string.IsNullOrWhiteSpace(ticker) ||
            instrument.StartsWith("Profit/loss", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string category = GetCol(row, map, "Category");

        TryParseDecimal(GetCol(row, map, "Volume"), out decimal volume);
        TryParseDecimal(GetCol(row, map, "Open Price"), out decimal openPrice);
        TryParseDecimal(GetCol(row, map, "Close Price"), out decimal closePrice);
        TryParseDate(GetCol(row, map, "Open Time (UTC)"), out DateTime openTime);
        TryParseDate(GetCol(row, map, "Close Time (UTC)"), out DateTime closeTime);
        TryParseDecimal(GetCol(row, map, "Purchase Value"), out decimal purchaseValEur);
        TryParseDecimal(GetCol(row, map, "Sale Value"), out decimal saleValEur);
        TryParseDecimal(GetCol(row, map, "Profit/Loss"), out decimal netProfitEur);
        TryParseDecimal(GetCol(row, map, "Gross Profit"), out decimal grossProfitEur);
        TryParseDecimal(GetCol(row, map, "Commission"), out decimal commission);
        TryParseDecimal(GetCol(row, map, "Swap"), out decimal swap);
        TryParseDecimal(GetCol(row, map, "Rollover"), out decimal rollover);
        TryParseDecimal(GetCol(row, map, "Open Conversion Rate"), out decimal openConvRate);
        TryParseDecimal(GetCol(row, map, "Close Conversion Rate"), out decimal closeConvRate);

        TickerMapping mapping = TickerMappingHelper.ResolveOrUpsert(
            doc.TickerMappings,
            ticker,
            instrument,
            category,
            detectedCurrency: null,
            openConversionRateToEur: openConvRate > 0 ? openConvRate : null);

        PositionLot lot = new()
        {
            ExternalPositionId = posId,
            CompanyName = string.IsNullOrWhiteSpace(mapping.CompanyName) ? instrument : mapping.CompanyName,
            Ticker = ticker,
            YahooSymbol = mapping.YahooSymbol,
            Category = mapping.Category,
            TradeType = GetCol(row, map, "Type"),
            CountryCode = mapping.CountryCode,
            StockCurrency = mapping.Currency,
            Quantity = volume,
            OpenDate = openTime,
            CloseDate = closeTime,
            OpenPrice = openPrice,
            ClosePrice = closePrice,
            CurrentMarketPrice = closePrice,
            OpenConversionRate = openConvRate > 0 ? openConvRate : null,
            CloseConversionRate = closeConvRate > 0 ? closeConvRate : null,
            BrokerPurchaseValueEur = purchaseValEur > 0 ? purchaseValEur : null,
            BrokerSaleOrCurrentValueEur = saleValEur > 0 ? saleValEur : null,
            BrokerNetProfitEur = netProfitEur,
            BrokerGrossProfitEur = grossProfitEur,
            OpenCommission = Math.Abs(commission),
            Swap = swap,
            Rollover = rollover,
            CloseOrigin = GetCol(row, map, "Close Origin"),
            Product = GetCol(row, map, "Product"),
            Comment = GetCol(row, map, "Comment"),
            Source = DataSource.XtbImport
        };

        doc.Lots.Add(lot);
    }

    private static void ParseOpenPositionRow(
        List<string> row,
        Dictionary<string, int> map,
        PortfolioDocument doc,
        ref string currentInstrumentName,
        ref string currentCategory)
    {
        string instOrPos = GetCol(row, map, "Instrument/Position");
        string ticker = GetCol(row, map, "Ticker");
        string category = GetCol(row, map, "Category");
        string tradeType = GetCol(row, map, "Type");

        if (string.IsNullOrWhiteSpace(instOrPos) || string.IsNullOrWhiteSpace(ticker))
        {
            return;
        }

        // Summary row per instrument has empty Type and non-numeric Instrument/Position
        if (string.IsNullOrWhiteSpace(tradeType) && !long.TryParse(instOrPos, out _))
        {
            currentInstrumentName = instOrPos;
            currentCategory = string.IsNullOrWhiteSpace(category) ? "STOCK" : category;
            return;
        }

        string companyName = string.IsNullOrWhiteSpace(currentInstrumentName) ? ticker : currentInstrumentName;
        string effCategory = string.IsNullOrWhiteSpace(category) ? currentCategory : category;

        TryParseDecimal(GetCol(row, map, "Volume"), out decimal volume);
        TryParseDecimal(GetCol(row, map, "Value"), out decimal currentValEur);
        TryParseDecimal(GetCol(row, map, "Current price"), out decimal currentPrice);
        TryParseDecimal(GetCol(row, map, "Open price"), out decimal openPrice);
        TryParseDate(GetCol(row, map, "Open time (UTC)"), out DateTime openTime);
        TryParseDecimal(GetCol(row, map, "Net Profit"), out decimal netProfitEur);
        TryParseDecimal(GetCol(row, map, "Gross Profit"), out decimal grossProfitEur);
        TryParseDecimal(GetCol(row, map, "Open Commission"), out decimal openComm);
        TryParseDecimal(GetCol(row, map, "Swap"), out decimal swap);
        TryParseDecimal(GetCol(row, map, "Rollover"), out decimal rollover);

        decimal derivedPurchaseEur = currentValEur - grossProfitEur;
        decimal? openConv = derivedPurchaseEur > 0 && volume > 0 && openPrice > 0
            ? Math.Round(derivedPurchaseEur / (volume * openPrice), 6)
            : null;

        TickerMapping mapping = TickerMappingHelper.ResolveOrUpsert(
            doc.TickerMappings,
            ticker,
            companyName,
            effCategory,
            detectedCurrency: null,
            openConversionRateToEur: openConv);

        PositionLot lot = new()
        {
            ExternalPositionId = instOrPos,
            CompanyName = string.IsNullOrWhiteSpace(mapping.CompanyName) ? companyName : mapping.CompanyName,
            Ticker = ticker,
            YahooSymbol = mapping.YahooSymbol,
            Category = mapping.Category,
            TradeType = string.IsNullOrWhiteSpace(tradeType) ? "BUY" : tradeType,
            CountryCode = mapping.CountryCode,
            StockCurrency = mapping.Currency,
            Quantity = volume,
            OpenDate = openTime,
            CloseDate = null,
            OpenPrice = openPrice,
            CurrentMarketPrice = currentPrice,
            BrokerPurchaseValueEur = derivedPurchaseEur > 0 ? Math.Round(derivedPurchaseEur, 2) : null,
            BrokerSaleOrCurrentValueEur = currentValEur > 0 ? currentValEur : null,
            BrokerNetProfitEur = netProfitEur,
            BrokerGrossProfitEur = grossProfitEur,
            OpenCommission = Math.Abs(openComm),
            Swap = swap,
            Rollover = rollover,
            Product = GetCol(row, map, "Product"),
            Source = DataSource.XtbImport
        };

        if (lot.BrokerPurchaseValueEur.HasValue && volume > 0 && openPrice > 0)
        {
            lot.OpenConversionRate = Math.Round(lot.BrokerPurchaseValueEur.Value / (volume * openPrice), 6);
        }

        if (currentValEur > 0 && volume > 0 && currentPrice > 0)
        {
            lot.CloseConversionRate = Math.Round(currentValEur / (volume * currentPrice), 6);
        }

        doc.Lots.Add(lot);
    }

    private static void ParseCashOperationRow(List<string> row, Dictionary<string, int> map, PortfolioDocument doc)
    {
        string rawType = GetCol(row, map, "Type");
        if (string.IsNullOrWhiteSpace(rawType) || rawType.Equals("Total", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryParseDate(GetCol(row, map, "Time"), out DateTime timeUtc);
        TryParseDecimal(GetCol(row, map, "Amount"), out decimal amountEur);

        AccountCashFlowRecord record = new()
        {
            OperationId = GetCol(row, map, "ID"),
            RawType = rawType,
            OperationType = MapCashOperationType(rawType),
            Instrument = GetCol(row, map, "Instrument"),
            Ticker = GetCol(row, map, "Ticker"),
            Category = GetCol(row, map, "Category"),
            TimeUtc = timeUtc,
            AmountEur = amountEur,
            AmountBase = amountEur,
            Comment = GetCol(row, map, "Comment"),
            Product = GetCol(row, map, "Product"),
            PositionId = GetCol(row, map, "Position ID")
        };

        doc.CashFlows.Add(record);
    }

    private static CashOperationType MapCashOperationType(string rawType) => rawType.ToUpperInvariant() switch
    {
        "DEPOSIT" => CashOperationType.Deposit,
        "WITHDRAWAL" => CashOperationType.Withdrawal,
        "FREE FUNDS INTEREST" => CashOperationType.FreeFundsInterest,
        "FREE FUNDS INTEREST TAX" => CashOperationType.FreeFundsInterestTax,
        "DIVIDEND" => CashOperationType.Dividend,
        "WITHHOLDING TAX" => CashOperationType.WithholdingTax,
        "STOCK PURCHASE" => CashOperationType.StockPurchase,
        "STOCK SELL" => CashOperationType.StockSale,
        "COMMISSION" => CashOperationType.Commission,
        "SEC FEE" => CashOperationType.SecFee,
        "TAX IFTT" => CashOperationType.FinancialTransactionTax,
        "SPINOFF" => CashOperationType.Spinoff,
        _ => CashOperationType.Other
    };

    private static void ReconcileCashOperationsWithPositions(PortfolioDocument doc)
    {
        Dictionary<string, PositionLot> lotsByPosId = new(StringComparer.OrdinalIgnoreCase);
        foreach (PositionLot lot in doc.Lots)
        {
            if (!string.IsNullOrWhiteSpace(lot.ExternalPositionId))
            {
                lotsByPosId.TryAdd(lot.ExternalPositionId, lot);
            }
        }

        // Map split fills (such as AbbVie 358825604 merged into 358825602 at same timestamp)
        foreach (AccountCashFlowRecord purchase in doc.CashFlows.Where(c => c.OperationType == CashOperationType.StockPurchase))
        {
            if (!string.IsNullOrWhiteSpace(purchase.PositionId) && !lotsByPosId.ContainsKey(purchase.PositionId))
            {
                PositionLot? sibling = doc.Lots.FirstOrDefault(l =>
                    l.Ticker.Equals(purchase.Ticker, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs((l.OpenDate - purchase.TimeUtc).TotalMinutes) < 5);
                if (sibling != null)
                {
                    if (!sibling.MergedPositionIds.Contains(purchase.PositionId))
                    {
                        sibling.MergedPositionIds.Add(purchase.PositionId);
                    }
                    lotsByPosId[purchase.PositionId] = sibling;
                }
            }
        }

        // Attach fees and spinoffs to lots
        foreach (AccountCashFlowRecord op in doc.CashFlows)
        {
            if (string.IsNullOrWhiteSpace(op.PositionId) || !lotsByPosId.TryGetValue(op.PositionId, out PositionLot? lot))
            {
                continue;
            }

            switch (op.OperationType)
            {
                case CashOperationType.SecFee:
                    lot.SecFee += Math.Abs(op.AmountEur);
                    break;
                case CashOperationType.FinancialTransactionTax:
                    lot.FinancialTransactionTax += Math.Abs(op.AmountEur);
                    break;
                case CashOperationType.Spinoff:
                    lot.SpinoffProceedsBase += op.AmountEur;
                    break;
            }
        }

        // Pair Dividend + Withholding Tax operations
        List<AccountCashFlowRecord> divOps = doc.CashFlows
            .Where(c => c.OperationType == CashOperationType.Dividend)
            .OrderByDescending(c => c.TimeUtc)
            .ToList();

        List<AccountCashFlowRecord> taxOps = doc.CashFlows
            .Where(c => c.OperationType == CashOperationType.WithholdingTax)
            .ToList();

        HashSet<string>matchedTaxIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (AccountCashFlowRecord div in divOps)
        {
            AccountCashFlowRecord? matchingTax = taxOps.FirstOrDefault(t =>
                !matchedTaxIds.Contains(t.OperationId) &&
                t.Ticker.Equals(div.Ticker, StringComparison.OrdinalIgnoreCase) &&
                t.PositionId.Equals(div.PositionId, StringComparison.OrdinalIgnoreCase) &&
                Math.Abs((t.TimeUtc - div.TimeUtc).TotalMinutes) < 10);

            if (matchingTax != null)
            {
                matchedTaxIds.Add(matchingTax.OperationId);
            }

            decimal grossEur = div.AmountEur;
            decimal taxEur = matchingTax != null ? Math.Abs(matchingTax.AmountEur) : 0m;
            decimal netEur = grossEur - taxEur;

            string nativeCcy = TickerMappingHelper.Resolve(div.Ticker, div.Category, doc.TickerMappings).Currency;
            decimal? divPerShare = null;
            Match mDiv = DividendPerShareRegex().Match(div.Comment);
            if (mDiv.Success)
            {
                nativeCcy = mDiv.Groups["ccy"].Value.ToUpperInvariant();
                if (TryParseDecimal(mDiv.Groups["rate"].Value, out decimal dps))
                {
                    divPerShare = dps;
                }
            }

            if (!string.IsNullOrWhiteSpace(div.Ticker))
            {
                TickerMappingHelper.ResolveOrUpsert(
                    doc.TickerMappings,
                    div.Ticker,
                    div.Instrument,
                    div.Category,
                    detectedCurrency: nativeCcy);
            }

            decimal taxRatePct = grossEur > 0 ? Math.Round(taxEur / grossEur * 100m, 3) : 0m;
            if (matchingTax != null)
            {
                Match mTax = WithholdingTaxRateRegex().Match(matchingTax.Comment);
                if (mTax.Success && TryParseDecimal(mTax.Groups["pct"].Value, out decimal parsedPct))
                {
                    taxRatePct = parsedPct;
                }
            }

            PositionLot? linkedLot = !string.IsNullOrWhiteSpace(div.PositionId) &&
                                     lotsByPosId.TryGetValue(div.PositionId, out PositionLot? foundLot)
                ? foundLot
                : null;

            if (linkedLot != null)
            {
                doc.Dividends.Add(new DividendRecord
                {
                    ExternalCashOperationId = div.OperationId,
                    ExternalTaxOperationId = matchingTax?.OperationId ?? string.Empty,
                    PositionLotId = linkedLot.Id,
                    ExternalPositionId = linkedLot.ExternalPositionId,
                    CompanyName = string.IsNullOrWhiteSpace(div.Instrument) ? linkedLot.CompanyName : div.Instrument,
                    Ticker = div.Ticker,
                    Category = string.IsNullOrWhiteSpace(div.Category) ? linkedLot.Category : div.Category,
                    PayDate = div.TimeUtc,
                    DividendPerShareNative = divPerShare,
                    NativeCurrency = nativeCcy,
                    GrossAmountEur = grossEur,
                    WithholdingTaxEur = taxEur,
                    NetAmountEur = netEur,
                    GrossAmountBase = grossEur,
                    WithholdingTaxBase = taxEur,
                    NetAmountBase = netEur,
                    TaxRatePercent = taxRatePct,
                    Comment = div.Comment,
                    IsFromBrokerReport = true
                });
            }
            else
            {
                // Allocate dividend with blank Position ID across lots of that ticker active on PayDate
                List<PositionLot> activeLots = doc.Lots
                    .Where(l => l.Ticker.Equals(div.Ticker, StringComparison.OrdinalIgnoreCase) &&
                                l.OpenDate <= div.TimeUtc.AddDays(1) &&
                                (!l.CloseDate.HasValue || l.CloseDate.Value >= div.TimeUtc.AddDays(-5)))
                    .ToList();

                decimal totalQty = activeLots.Sum(l => l.Quantity);
                if (activeLots.Count > 0 && totalQty > 0)
                {
                    foreach (PositionLot activeLot in activeLots)
                    {
                        decimal ratio = activeLot.Quantity / totalQty;
                        decimal lotGross = Math.Round(grossEur * ratio, 2);
                        decimal lotTax = Math.Round(taxEur * ratio, 2);
                        doc.Dividends.Add(new DividendRecord
                        {
                            ExternalCashOperationId = $"{div.OperationId}-{activeLot.ExternalPositionId}",
                            ExternalTaxOperationId = matchingTax?.OperationId ?? string.Empty,
                            PositionLotId = activeLot.Id,
                            ExternalPositionId = activeLot.ExternalPositionId,
                            CompanyName = string.IsNullOrWhiteSpace(div.Instrument) ? activeLot.CompanyName : div.Instrument,
                            Ticker = div.Ticker,
                            Category = string.IsNullOrWhiteSpace(div.Category) ? activeLot.Category : div.Category,
                            PayDate = div.TimeUtc,
                            DividendPerShareNative = divPerShare,
                            NativeCurrency = nativeCcy,
                            GrossAmountEur = lotGross,
                            WithholdingTaxEur = lotTax,
                            NetAmountEur = lotGross - lotTax,
                            GrossAmountBase = lotGross,
                            WithholdingTaxBase = lotTax,
                            NetAmountBase = lotGross - lotTax,
                            TaxRatePercent = taxRatePct,
                            Comment = $"{div.Comment} (Allocated {activeLot.Quantity}/{totalQty} sh)",
                            IsFromBrokerReport = true
                        });
                    }
                }
                else
                {
                    doc.Dividends.Add(new DividendRecord
                    {
                        ExternalCashOperationId = div.OperationId,
                        ExternalTaxOperationId = matchingTax?.OperationId ?? string.Empty,
                        CompanyName = div.Instrument,
                        Ticker = div.Ticker,
                        Category = div.Category,
                        PayDate = div.TimeUtc,
                        DividendPerShareNative = divPerShare,
                        NativeCurrency = nativeCcy,
                        GrossAmountEur = grossEur,
                        WithholdingTaxEur = taxEur,
                        NetAmountEur = netEur,
                        GrossAmountBase = grossEur,
                        WithholdingTaxBase = taxEur,
                        NetAmountBase = netEur,
                        TaxRatePercent = taxRatePct,
                        Comment = div.Comment,
                        IsFromBrokerReport = true
                    });
                }
            }
        }

        // Capture standalone WHT corrections / reclassifications (e.g., WPC.US WHT refunds)
        foreach (AccountCashFlowRecord unmatchedTax in taxOps.Where(t => !matchedTaxIds.Contains(t.OperationId)))
        {
            PositionLot? linkedLot = !string.IsNullOrWhiteSpace(unmatchedTax.PositionId) &&
                                     lotsByPosId.TryGetValue(unmatchedTax.PositionId, out PositionLot? foundLot)
                ? foundLot
                : null;

            decimal taxAdjustmentEur = -unmatchedTax.AmountEur; // positive amount in cash op means tax refund (reduces tax)
            doc.Dividends.Add(new DividendRecord
            {
                ExternalCashOperationId = unmatchedTax.OperationId,
                ExternalTaxOperationId = unmatchedTax.OperationId,
                PositionLotId = linkedLot?.Id,
                ExternalPositionId = linkedLot?.ExternalPositionId ?? unmatchedTax.PositionId,
                CompanyName = unmatchedTax.Instrument,
                Ticker = unmatchedTax.Ticker,
                Category = unmatchedTax.Category,
                PayDate = unmatchedTax.TimeUtc,
                NativeCurrency = TickerMappingHelper.Resolve(unmatchedTax.Ticker, unmatchedTax.Category, doc.TickerMappings).Currency,
                GrossAmountEur = 0m,
                WithholdingTaxEur = taxAdjustmentEur,
                NetAmountEur = unmatchedTax.AmountEur,
                GrossAmountBase = 0m,
                WithholdingTaxBase = taxAdjustmentEur,
                NetAmountBase = unmatchedTax.AmountEur,
                TaxRatePercent = 0m,
                IsTaxAdjustment = true,
                IsFromBrokerReport = true,
                Comment = unmatchedTax.Comment
            });
        }
    }

    private static bool TryParseDecimal(string text, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = 0m;
            return false;
        }

        string cleaned = text.Replace("€", "").Replace(" ", "").Trim();
        return decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out result) ||
               decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
    }

    private static bool TryParseDate(string text, out DateTime result)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            result = DateTime.MinValue;
            return false;
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}

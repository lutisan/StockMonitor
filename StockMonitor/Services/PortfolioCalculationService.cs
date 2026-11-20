using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Computes position values, currency conversions, dividend summaries, sparklines, and historical timeline snapshots.
/// </summary>
public sealed class PortfolioCalculationService
{
    private readonly FrankfurterFxRateProvider _fxProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PortfolioCalculationService"/> class.
    /// </summary>
    /// <param name="fxProvider">Exchange rate provider used for multi-currency conversions.</param>
    public PortfolioCalculationService(FrankfurterFxRateProvider fxProvider)
    {
        _fxProvider = fxProvider;
    }

    /// <summary>
    /// Recalculates all lots, dividends, cash flows, and sparklines in the selected base currency.
    /// </summary>
    public void RecalculateAll(PortfolioDocument doc)
    {
        string baseCcy = string.IsNullOrWhiteSpace(doc.Settings.BaseCurrency)
            ? "EUR"
            : doc.Settings.BaseCurrency.Trim().ToUpperInvariant();

        decimal eurToBaseLatest = _fxProvider.GetConversionRate("EUR", baseCcy);

        Dictionary<string, decimal> countryTaxLookup = doc.TaxRules
            .GroupBy(r => r.CountryCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().AppliedRatePercent, StringComparer.OrdinalIgnoreCase);

        // 1. Recalculate cash flows into BaseCurrency
        foreach (AccountCashFlowRecord cf in doc.CashFlows)
        {
            cf.BaseCurrency = baseCcy;
            decimal eurToBaseAtDate = _fxProvider.GetConversionRate("EUR", baseCcy, cf.TimeUtc);
            cf.AmountBase = Math.Round(cf.AmountEur * (eurToBaseAtDate > 0 ? eurToBaseAtDate : eurToBaseLatest), 2);
        }

        Dictionary<Guid, PositionLot> lotsById = doc.Lots.ToDictionary(l => l.Id);
        Dictionary<string, bool> tickerAllClosedLookup = doc.Lots
            .Where(l => !string.IsNullOrWhiteSpace(l.Ticker))
            .GroupBy(l => l.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.All(l => l.CloseDate.HasValue), StringComparer.OrdinalIgnoreCase);

        // 2. Recalculate dividend records into BaseCurrency
        foreach (DividendRecord div in doc.Dividends)
        {
            div.BaseCurrency = baseCcy;
            PositionLot? linkedLot = null;
            if (div.PositionLotId.HasValue && lotsById.TryGetValue(div.PositionLotId.Value, out PositionLot? foundLot))
            {
                linkedLot = foundLot;
                div.IsPositionClosed = foundLot.CloseDate.HasValue;
            }
            else if (!string.IsNullOrWhiteSpace(div.Ticker) && tickerAllClosedLookup.TryGetValue(div.Ticker, out bool allClosed))
            {
                div.IsPositionClosed = allClosed;
            }
            else
            {
                div.IsPositionClosed = false;
            }

            // Compute EUR amounts dynamically for Yahoo Finance fetched dividends using PayDate ECB FX rate and Country/Lot Tax Rule
            if (!div.IsFromBrokerReport && div.DividendPerShareNative is > 0m && linkedLot != null)
            {
                decimal grossNative = div.DividendPerShareNative.Value * linkedLot.Quantity;
                decimal nativeToEurAtPay = _fxProvider.GetConversionRate(div.NativeCurrency, "EUR", div.PayDate);
                decimal taxRate = linkedLot.ManualDividendTaxRate ?? countryTaxLookup.GetValueOrDefault(linkedLot.CountryCode, 15m);

                div.TaxRatePercent = taxRate;
                div.GrossAmountEur = Math.Round(grossNative * nativeToEurAtPay, 2);
                div.WithholdingTaxEur = Math.Round(div.GrossAmountEur * (taxRate / 100m), 2);
                div.NetAmountEur = Math.Round(div.GrossAmountEur - div.WithholdingTaxEur, 2);
            }

            decimal eurToBaseAtPay = _fxProvider.GetConversionRate("EUR", baseCcy, div.PayDate);
            decimal mult = eurToBaseAtPay > 0 ? eurToBaseAtPay : eurToBaseLatest;

            div.GrossAmountBase = Math.Round(div.GrossAmountEur * mult, 2);
            div.WithholdingTaxBase = Math.Round(div.WithholdingTaxEur * mult, 2);
            div.NetAmountBase = Math.Round(div.NetAmountEur * mult, 2);
        }

        // Group dividends by PositionLotId
        Dictionary<Guid, List<DividendRecord>> divsByLot = doc.Dividends
            .Where(d => d.PositionLotId.HasValue)
            .GroupBy(d => d.PositionLotId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 3. Recalculate every position lot
        foreach (PositionLot lot in doc.Lots)
        {
            lot.BaseCurrency = baseCcy;
            decimal defaultCountryTax = countryTaxLookup.GetValueOrDefault(lot.CountryCode, 15m);
            lot.EffectiveDividendTaxRate = lot.ManualDividendTaxRate ?? defaultCountryTax;

            decimal openPrice = lot.OpenPrice;
            decimal closeOrCurrPrice = lot.CloseDate.HasValue
                ? (lot.ClosePrice ?? lot.CurrentMarketPrice)
                : (lot.CurrentMarketPrice > 0 ? lot.CurrentMarketPrice : lot.OpenPrice);

            lot.CloseOrCurrentPrice = closeOrCurrPrice;
            lot.OpenCost = Math.Round(openPrice * lot.Quantity + lot.OpenCommission, 2);
            lot.CloseOrCurrentValue = Math.Round(closeOrCurrPrice * lot.Quantity - lot.CloseCommission, 2);
            lot.CapitalProfitLoss = Math.Round(lot.CloseOrCurrentValue - lot.OpenCost, 2);

            // Conversion to EUR first (using exact XTB conversion rates/values when available and price not manually overridden)
            decimal openCostEur;
            if (!lot.IsOpenPriceManual && lot.BrokerPurchaseValueEur is > 0)
            {
                openCostEur = lot.BrokerPurchaseValueEur.Value + lot.OpenCommission + lot.FinancialTransactionTax;
            }
            else
            {
                decimal openFxToEur = lot.OpenConversionRate is > 0 && !lot.IsOpenPriceManual
                    ? lot.OpenConversionRate.Value
                    : _fxProvider.GetConversionRate(lot.StockCurrency, "EUR", lot.OpenDate);
                openCostEur = openPrice * lot.Quantity * openFxToEur + lot.OpenCommission + lot.FinancialTransactionTax;
            }

            decimal closeOrCurrValEur;
            if (!lot.IsClosePriceManual && lot.BrokerSaleOrCurrentValueEur is > 0 &&
                (lot.CloseDate.HasValue || lot.CurrentMarketPrice == closeOrCurrPrice))
            {
                closeOrCurrValEur = lot.BrokerSaleOrCurrentValueEur.Value - lot.CloseCommission - lot.SecFee;
            }
            else
            {
                decimal closeFxToEur = lot.CloseConversionRate is > 0 && !lot.IsClosePriceManual
                    ? lot.CloseConversionRate.Value
                    : _fxProvider.GetConversionRate(lot.StockCurrency, "EUR", lot.CloseDate);
                closeOrCurrValEur = closeOrCurrPrice * lot.Quantity * closeFxToEur - lot.CloseCommission - lot.SecFee;
            }

            decimal openEurToBase = baseCcy == "EUR" ? 1m : _fxProvider.GetConversionRate("EUR", baseCcy, lot.OpenDate);
            decimal closeEurToBase = baseCcy == "EUR" ? 1m : _fxProvider.GetConversionRate("EUR", baseCcy, lot.CloseDate);

            lot.OpenCostBase = Math.Round(openCostEur * openEurToBase, 2);
            lot.OpenPriceBase = lot.Quantity > 0 ? Math.Round(lot.OpenCostBase / lot.Quantity, 4) : 0m;

            lot.CloseOrCurrentValueBase = Math.Round(closeOrCurrValEur * closeEurToBase, 2);
            lot.CloseOrCurrentPriceBase = lot.Quantity > 0 ? Math.Round(lot.CloseOrCurrentValueBase / lot.Quantity, 4) : 0m;

            lot.CapitalProfitLossBase = Math.Round(lot.CloseOrCurrentValueBase - lot.OpenCostBase + lot.Swap + lot.Rollover, 2);
            lot.CapitalReturnPercent = lot.OpenCostBase > 0
                ? Math.Round(lot.CapitalProfitLossBase / lot.OpenCostBase * 100m, 2)
                : 0m;

            // Aggregate dividends for this lot
            if (divsByLot.TryGetValue(lot.Id, out List<DividendRecord>? lotDivs) && lotDivs.Count > 0)
            {
                decimal grossBase = 0m;
                decimal taxBase = 0m;
                decimal netBase = 0m;

                foreach (DividendRecord d in lotDivs)
                {
                    if (lot.ManualDividendTaxRate.HasValue && !d.IsTaxAdjustment)
                    {
                        decimal customTax = Math.Round(d.GrossAmountBase * (lot.ManualDividendTaxRate.Value / 100m), 2);
                        grossBase += d.GrossAmountBase;
                        taxBase += customTax;
                        netBase += d.GrossAmountBase - customTax;
                    }
                    else
                    {
                        grossBase += d.GrossAmountBase;
                        taxBase += d.WithholdingTaxBase;
                        netBase += d.NetAmountBase;
                    }

                    d.AnnualizedYieldPercent = lot.OpenCostBase > 0 && d.GrossAmountBase > 0
                        ? Math.Round(d.GrossAmountBase * 4m / lot.OpenCostBase * 100m, 2)
                        : 0m;
                }

                lot.GrossDividends = Math.Round(grossBase, 2);
                lot.WithholdingTax = Math.Round(taxBase, 2);
                lot.NetDividends = Math.Round(netBase, 2);
                lot.NetDividendsBase = Math.Round(netBase, 2);
            }
            else
            {
                lot.GrossDividends = 0m;
                lot.WithholdingTax = 0m;
                lot.NetDividends = 0m;
                lot.NetDividendsBase = 0m;
            }

            decimal spinoffBase = Math.Round(lot.SpinoffProceedsBase * closeEurToBase, 2);
            lot.TotalProfitBase = Math.Round(lot.CapitalProfitLossBase + lot.NetDividendsBase + spinoffBase, 2);
            lot.TotalReturnPercent = lot.OpenCostBase > 0
                ? Math.Round(lot.TotalProfitBase / lot.OpenCostBase * 100m, 2)
                : 0m;

            lot.SparklinePoints = BuildSparklinePoints(lot);
        }
    }

    private static List<SparklinePoint> BuildSparklinePoints(PositionLot lot)
    {
        List<SparklinePoint> points = new(12);
        DateTime start = lot.OpenDate.Date;
        DateTime end = (lot.CloseDate ?? DateTime.UtcNow).Date;
        if (end <= start)
        {
            end = start.AddDays(12);
        }

        double totalProfit = (double)lot.CapitalProfitLossBase;
        double invested = (double)Math.Max(100m, lot.OpenCostBase);
        int seed = StringComparer.OrdinalIgnoreCase.GetHashCode(lot.ExternalPositionId + lot.Ticker);
        Random rng = new(seed);

        for (int i = 1; i <= 12; i++)
        {
            double t = i / 12.0;
            double bridge = Math.Sin(t * Math.PI);
            double noise = (Math.Sin(t * Math.PI * 3.0 + (seed % 5)) * 0.04 + (rng.NextDouble() - 0.5) * 0.03) * invested * bridge;
            double val = i == 12 ? totalProfit : totalProfit * t + noise;
            points.Add(new SparklinePoint
            {
                Date = start.AddDays((end - start).TotalDays * t),
                ProfitBase = Math.Round(val, 2)
            });
        }

        return points;
    }

    /// <summary>
    /// Builds quarterly historical snapshots from the earliest purchase date to the report date.
    /// </summary>
    public List<PortfolioHistoryPoint> BuildPortfolioHistory(PortfolioDocument doc)
    {
        List<PortfolioHistoryPoint> history = [];
        if (doc.Lots.Count == 0)
        {
            return history;
        }

        DateTime minDate = doc.Lots.Min(l => l.OpenDate).Date;
        DateTime todayUtc = DateTime.UtcNow.Date;
        DateTime maxDate = doc.ReportGeneratedUtc.Date > todayUtc ? doc.ReportGeneratedUtc.Date : todayUtc;
        if (maxDate < minDate)
        {
            maxDate = minDate;
        }

        // Generate quarter-end checkpoints plus final report date
        List<DateTime> checkpoints = [];
        DateTime cursor = new(minDate.Year, ((minDate.Month - 1) / 3 + 1) * 3, 1);
        cursor = cursor.AddMonths(1).AddDays(-1);
        while (cursor < maxDate)
        {
            if (cursor >= minDate)
            {
                checkpoints.Add(cursor);
            }
            cursor = cursor.AddMonths(3);
            cursor = new DateTime(cursor.Year, cursor.Month, DateTime.DaysInMonth(cursor.Year, cursor.Month));
        }
        checkpoints.Add(maxDate);

        foreach (DateTime dt in checkpoints)
        {
            List<PositionLot> activeOpenLots = doc.Lots
                .Where(l => l.OpenDate.Date <= dt && (!l.CloseDate.HasValue || l.CloseDate.Value.Date > dt))
                .ToList();

            List<PositionLot> closedByDate = doc.Lots
                .Where(l => l.CloseDate.HasValue && l.CloseDate.Value.Date <= dt)
                .ToList();

            decimal activeInvested = activeOpenLots.Sum(l => l.OpenCostBase);
            decimal realizedProfit = closedByDate.Sum(l => l.CapitalProfitLossBase);

            // Estimate market value at checkpoint date by time-weighted interpolation of each active lot's gain
            decimal holdingsValue = 0m;
            foreach (PositionLot lot in activeOpenLots)
            {
                DateTime lotEnd = (lot.CloseDate ?? maxDate).Date;
                double totalSpan = Math.Max(1.0, (lotEnd - lot.OpenDate.Date).TotalDays);
                double elapsed = Math.Clamp((dt - lot.OpenDate.Date).TotalDays, 0.0, totalSpan);
                decimal ratio = (decimal)(elapsed / totalSpan);
                holdingsValue += lot.OpenCostBase + lot.CapitalProfitLossBase * ratio;
            }

            decimal cumNetDivs = doc.Dividends
                .Where(d => d.PayDate.Date <= dt)
                .Sum(d => d.NetAmountBase);

            decimal cumDeposits = doc.CashFlows
                .Where(c => c.OperationType == CashOperationType.Deposit && c.TimeUtc.Date <= dt)
                .Sum(c => c.AmountBase);

            decimal cumInterest = doc.CashFlows
                .Where(c => (c.OperationType == CashOperationType.FreeFundsInterest ||
                             c.OperationType == CashOperationType.FreeFundsInterestTax) &&
                            c.TimeUtc.Date <= dt)
                .Sum(c => c.AmountBase);

            decimal unrealizedProfit = Math.Round(holdingsValue - activeInvested, 2);
            decimal totalProfit = Math.Round(unrealizedProfit + realizedProfit + cumNetDivs + cumInterest, 2);
            decimal totalValue = Math.Round(holdingsValue + cumNetDivs + cumInterest + Math.Max(0m, realizedProfit), 2);

            history.Add(new PortfolioHistoryPoint
            {
                Date = dt,
                BaseCurrency = string.IsNullOrWhiteSpace(doc.Settings.BaseCurrency) ? "EUR" : doc.Settings.BaseCurrency.Trim().ToUpperInvariant(),
                CumulativeDepositsBase = Math.Round(cumDeposits, 2),
                CumulativeInvestedBase = Math.Round(activeInvested, 2),
                HoldingsMarketValueBase = Math.Round(holdingsValue, 2),
                RealizedCapitalProfitBase = Math.Round(realizedProfit, 2),
                CumulativeNetDividendsBase = Math.Round(cumNetDivs, 2),
                CumulativeNetInterestBase = Math.Round(cumInterest, 2),
                TotalPortfolioValueBase = totalValue,
                TotalProfitBase = totalProfit
            });
        }

        return history;
    }
}

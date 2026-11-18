# Prehlad 3 & XTB Specification Reference

> **Purpose**: Preserves the original audit of Google Sheet `Prehlad 3` (`1tPWzW3PKwNxcsIaAJs9--pKQMYrEfzlwraOOB9eM9Xg`) and explains how `StockMonitor` fixes all 10 spreadsheet defects using XTB broker report imports.

---

## 1. Original Google Sheet Structure (`Prehlad 3`)

| Sheet | Original Purpose | Replacement in `StockMonitor` |
|---|---|---|
| **Aktualne** | 77 purchase lots across 39 tickers | **Tab 1: `Positions (Open & Closed)`** (`SfDataGrid` grouped by `CompanyName`, status filter `All / Open / Closed`, inline `SfColumnSparkline`, lot overrides sidebar, top chart + Yahoo web view) |
| **Summary** | KPI tiles + per-lot summary table | **Tab 2: `Summary & Overview`** (KPI cards, per-ticker aggregated `SfDataGrid`, allocation `DoughnutSeries`, ticker P/L `ColumnSeries`) |
| **Dividendy** | 539-column horizontal dividend grid | **Tab 3: `Dividends`** (normalized `DividendRecord` table from XTB Cash Operations + Yahoo fallback, monthly/annual `SfChart`) |
| **Vývoj portfólia** | Quarterly history table & chart | **Tab 4: `Portfolio History`** (daily historical valuation curve calculated from actual open/close dates, historical prices, FX rates, and cumulative net dividends) |
| **Dan z divident** | Static withholding tax text notes | **Tab 5: `Dividend Tax, Cash Flow & Settings`** (active `CountryTaxRule` table driving treaty tax and reclaimable tax calculations + XTB Cash Operations table) |

---

## 2. How `StockMonitor` Resolves the 10 Spreadsheet Defects

1. **Row Misalignment Between `Aktualne` and `Summary`**:
   - Resolved by computing all summaries directly from the strongly typed `PositionLot` collection in `PortfolioCalculationService`.
2. **Positional Dividend Indexing (`INDEX(Dividendy, , (ROW()-10)*7 + n)`)**:
   - Resolved by linking each `DividendRecord` by normalized ticker symbol and matching payment date against open lot holding intervals (`OpenTime <= PayDate <= CloseTime`).
3. **`#N/A` Live Price Lookups Zeroing Invested Capital**:
   - Resolved by storing exact XTB broker fill prices (`OpenPriceNative`, `PurchaseValueBase`, `ClosePriceNative`, `SaleValueBase`, `XtbGrossProfitLossBase`) and supporting user manual overrides (`ManualCurrentPriceOverride`).
4. **Synthetic Linear Portfolio History**:
   - Resolved by building a true time-series valuation from historical daily close series and historical FX rates.
5. **Flat 15% Dividend Tax Assumption**:
   - Resolved by importing exact withholding tax entries from XTB `CASH OPERATION HISTORY` (`Withholding Tax`) and applying country-specific `CountryTaxRule` treaty rates (`US`, `DE`, `FR`, `GB`, `CZ`, `CH`, `NL`, `IE`).
6. **British Pence (`GBX` / `GBp`) Conversion Failures**:
   - Resolved in `TickerMappingHelper` and `YahooFinanceMarketDataProvider` by normalizing `.UK` / `.L` pence quotes into `GBP` before converting to the user's selected Base Currency.
7. **`#REF!` Errors in `YAHOO_DIVIDENDS` Blocks**:
   - Eliminated by normalized `DividendRecord` persistence in `portfolio.json`.
8. **Invalid Sell Date Text (`"."`)**:
   - Eliminated by typed `DateTime? CloseTime` parsing in `XtbWorkbookImportService`.
9. **Hardcoded `× 4` Quarterly Yield Multiplier**:
   - Resolved by calculating actual trailing and realized dividend yields from real payouts over the holding period.
10. **Inconsistent Ticker Formats (`AMS:AD`, `DBXD.DE`, `CA`, `AAPL.US`)**:
    - Normalized in `TickerMappingHelper.cs` with explicit XTB-to-Yahoo symbol mapping (`XtbSymbol` ↔ `YahooSymbol`).

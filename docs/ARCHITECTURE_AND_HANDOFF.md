# StockMonitor — Architecture & Development Handoff

## 1. Project Goal

`StockMonitor` is a desktop WPF application built with **Syncfusion Essential Studio for WPF `31.1.17`** and **`CommunityToolkit.Mvvm` (`8.4.0`)** on **`.NET 9.0`**.
It replaces and extends the user's Google Sheet (`Prehlad 3`, ID `1tPWzW3PKwNxcsIaAJs9--pKQMYrEfzlwraOOB9eM9Xg`) and directly imports XTB broker export workbooks (`.xlsx` and `.csv`).

---

## 2. Core Functional Requirements & Design Decisions

### 2.1 Data Priority Hierarchy
1. **Manual User Overrides** (`ManualCurrentPriceOverride`, `ManualOpenFxOverride`, `ManualCloseFxOverride`, `Comment`, `UserNotes`) always take highest priority.
2. **XTB Broker Export Data** (`Closed Position History`, `Open Position`, `Cash Operation History`) takes priority over online API data for executed prices, purchase values, sale values, realized P/L, and recorded dividends/withholding taxes.
3. **Live Market & FX APIs** (`YahooFinanceMarketDataProvider` + `FrankfurterFxRateProvider`) fill current market prices, historical daily series, live/historical FX rates, and fallback dividend events when not overridden.

### 2.2 Selectable Base Currency
- Selectable in the top bar and Settings tab: **`EUR` (default)**, `USD`, `CZK`, `CHF`, `GBP`.
- Switching base currency triggers `PortfolioCalculationService.RecalculateAllAsync`, updating all lot costs, current/close values, capital P/L, dividends, cash flows, summaries, and portfolio history curves.

### 2.3 UI Layout (5 Main Tabs via `TabControlExt`)
1. **Tab 1 — `Positions (Open & Closed)`**:
   - Split vertically by a horizontal `SfGridSplitter` so the user can resize top and bottom areas.
   - **Top-Left Pane**: Switchable view (`TabControlExt`) between:
     - **Stock Price Chart**: `SfChart` (`FastLineBitmapSeries` + `ColumnSeries` volume) paired with `SfDateTimeRangeNavigator`, entry/exit markers, and range quick-filters.
     - **Yahoo Finance Web View**: Embedded `WebBrowser` navigating to `https://finance.yahoo.com/quote/{YahooSymbol}` with an "Open in External Browser" fallback button.
   - **Top-Right Sidebar**: Persistent **Lot Details, Manual Overrides & Comments** editor for the selected `PositionLot` (plus Add New Manual Lot / Save / Delete actions).
   - **Bottom Pane**: `SfDataGrid` grouped by `CompanyName` (`ShowGroupDropArea="True"`, `AutoExpandGroups="True"`, `FrozenColumnCount="2"`) with group summary rows, table summary rows, status filter (`All`, `Open Only`, `Closed Only`), search filter, inline `SfColumnSparkline` for 30-day performance, and Excel/CSV export buttons.
2. **Tab 2 — `Summary & Overview`**:
   - KPI summary cards (Open Invested, Open Market Value, Unrealized P/L, Realized P/L, Net Dividends, Total Return).
   - Ticker-level aggregated `SfDataGrid` + `SfChart` (`DoughnutSeries` allocation by ticker & currency, `ColumnSeries` profit by ticker).
3. **Tab 3 — `Dividends`**:
   - Dividend transactions `SfDataGrid` (ex-date/pay-date, ticker, country, gross dividend, withholding tax, net dividend, treaty tax rate, reclaimable tax in Base Currency).
   - Monthly/annual dividend bar charts (`SfChart`).
4. **Tab 4 — `Portfolio History`**:
   - Daily historical portfolio valuation `SfChart` (`Invested Capital` vs. `Market Value` vs. `Total Return incl. Dividends & Realized P/L`) with `SfDateTimeRangeNavigator` and historical `SfDataGrid`.
5. **Tab 5 — `Dividend Tax, Cash Flow & Settings`**:
   - Editable Country Withholding Tax treaty table (`CountryTaxRule`).
   - Account Cash Operations `SfDataGrid` (deposits, withdrawals, free-funds interest, taxes).
   - Theme selector (`Windows11Dark`, `Windows11Light`, `FluentDark`, `FluentLight`) and Base Currency selector.

---

## 3. Build & Verification Status

- **Current Build State**: `0 Warning(s), 0 Error(s)` (`bin\Debug\net9.0-windows\StockMonitor.exe`).
- **Known XAML Note**:
  - In Syncfusion WPF `31.1.17`, the frozen column property on `SfDataGrid` is `FrozenColumnCount="2"` (singular `Column`), not `FrozenColumnsCount`.

---

## 4. Suggested Next Enhancements for Future Sessions

1. **Interactive Testing & UI Polish**:
   - Run the app with `dotnet run --project StockMonitor/StockMonitor.csproj --no-build` and inspect column widths, group summary formatting, and dark/light theme contrast across all 5 tabs.
2. **WebView2 Upgrade (Optional)**:
   - Replace the standard WPF `WebBrowser` control on Tab 1 with `Microsoft.Web.WebView2.Wpf` if modern Chromium rendering is desired for Yahoo Finance pages.
3. **Additional XTB Report Formats**:
   - Test importing live `.xlsx` files exported directly from XTB xStation 5 across multiple account currencies (`EUR`, `USD`, `CZK`) and verify edge-case symbol mappings in `TickerMappingHelper.cs`.
4. **Custom Indicators on Stock Chart**:
   - Add optional SMA/EMA overlays or dividend payment markers directly onto the top-left `SfChart` in Tab 1.

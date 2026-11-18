# StockMonitor — Agent & Developer Context Guide

> **Purpose**: Hand-off and context reference for starting a new Antigravity conversation directly inside the `C:\Users\lutis\source\repos\StockMonitor` workspace.

---

## 1. Quick Start for New Conversations

When opening `C:\Users\lutis\source\repos\StockMonitor` in Antigravity, paste this prompt into your new conversation:

```text
Read AGENTS.md, docs/ARCHITECTURE_AND_HANDOFF.md, and docs/PREHLAD3_SPECIFICATION.md to load the project context, Syncfusion 31.1.17 offline build rules, and current implementation status.
```

---

## 2. Environment & Build Rules (Critical)

1. **Target Framework**: `.NET 9.0` (`net9.0-windows`) WPF application.
2. **Syncfusion Version**: Locally installed **Syncfusion Essential Studio for WPF `31.1.17`**.
   - Local NuGet fallback folder: `C:\Program Files (x86)\Syncfusion\Essential Studio\WPF\31.1.17\ToolboxNuGetPackages` (configured via `C:\Program Files (x86)\NuGet\Config\Syncfusion Toolbox for WPF.config`).
   - Local precompiled assemblies: `C:\Program Files (x86)\Syncfusion\Essential Studio\WPF\31.1.17\precompiledassemblies\net9.0\`.
   - **Do NOT upgrade Syncfusion packages to newer online versions (`33.x`)** and do not run online `dotnet add package` against `nuget.org` unless requested.
3. **Offline Build Command**:
   ```powershell
   dotnet build "C:\Users\lutis\source\repos\StockMonitor\StockMonitor\StockMonitor.csproj" --no-restore
   ```
   *(If package assets need refresh, use `dotnet build --ignore-failed-sources` so local cache + Syncfusion Toolbox folder are used cleanly).*
4. **Syncfusion Component Skills**:
   - Installed locally in `.agents/skills/syncfusion-wpf-*` (`datagrid`, `charts`, `range-selector`, `sparkline`, `tabcontrol`, `gridsplitter`, `theming`, `licensing`, `busy-indicator`).
5. **Licensing**:
   - `App.xaml.cs` reads `SYNCFUSION_LICENSE_KEY` from environment variables at startup. Never hardcode or commit license keys in source files.

---

## 3. Solution Map

- `StockMonitor.slnx`: Root solution file.
- `StockMonitor/StockMonitor.csproj`: WPF project file.
- `StockMonitor/Data/xtb_seed.csv`: Bundled seed dataset imported from the user's XTB account report (`CLOSED POSITION HISTORY`, `OPEN POSITION`, `CASH OPERATION HISTORY`).
- `StockMonitor/Models/`:
  - `Enums.cs` (`PositionStatus`, `PositionSide`, `CashFlowType`, `PositionFilterMode`)
  - `PositionLot.cs` (Open/Closed lots, manual overrides, native & base currency metrics, sparkline history)
  - `DividendRecord.cs` (Cash dividends, withholding tax, treaty reclaim calculations)
  - `AccountCashFlowRecord.cs` (Deposits, withdrawals, free-funds interest, withholding tax)
  - `CountryTaxRule.cs` (Default & treaty tax rates by country code)
  - `AppSettings.cs` (`BaseCurrency`, `ThemeName`, `CountryTaxRules`)
  - `PortfolioDocument.cs` (Root JSON persistence aggregate stored in `%LocalAppData%\StockMonitor\portfolio.json`)
- `StockMonitor/Services/`:
  - `TickerMappingHelper.cs` (Maps XTB symbols like `AAPL.US`, `SXR8.DE`, `CEZ.CZ` to Yahoo Finance symbols, currencies, and country codes)
  - `XtbWorkbookImportService.cs` (Imports XTB `.xlsx` OpenXML workbooks and `.csv` reports; merges lots, dividends, and cash flows without losing manual user notes/overrides)
  - `YahooFinanceMarketDataProvider.cs` (Fetches live quotes, historical OHLCV candles, and fallback dividends via `query1.finance.yahoo.com/v8/finance/chart/{symbol}`)
  - `FrankfurterFxRateProvider.cs` (Fetches live and historical ECB FX rates via `api.frankfurter.dev` with local cache and offline fallbacks)
  - `PortfolioCalculationService.cs` (Computes all financial formulas in native and selected base currency, allocates dividends to open intervals, builds ticker/currency summaries and daily portfolio curves)
  - `PortfolioStorageService.cs` (Reads/writes `%LocalAppData%\StockMonitor\portfolio.json` and caches market/FX responses)
  - `ExportService.cs` (Exports `SfDataGrid` to `.xlsx` via `Syncfusion.UI.Xaml.Grid.Converter` and to `.csv`)
- `StockMonitor/ViewModels/`:
  - `MainViewModel.cs` (MVVM state, commands, summaries, filtering, manual override saving, theme & currency switching)
- `StockMonitor/MainWindow.xaml` & `MainWindow.xaml.cs`:
  - 5-tab Syncfusion WPF interface with `SfSkinManager` theme support (`Windows11Dark`, `Windows11Light`, `FluentDark`, `FluentLight`).

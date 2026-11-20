using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockMonitor.Models;
using StockMonitor.Services;

namespace StockMonitor.ViewModels;

/// <summary>
/// Main ViewModel coordinating positions, charts, dividends, history, dynamic ticker mappings, tax rules,
/// and XTB Excel/CSV imports. All I/O and portfolio calculations run asynchronously with live status bar timing.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly PortfolioStorageService _storageService;
    private readonly PortfolioCalculationService _calculationService;
    private readonly YahooFinanceMarketDataProvider _marketDataProvider;
    private readonly FrankfurterFxRateProvider _fxRateProvider;
    private readonly XtbWorkbookImportService _xtbImportService;
    private readonly ExportService _exportService;
    private readonly IFileDialogService _fileDialogService;

    private PortfolioDocument _document = new();
    private bool _isInitializing = true;
    private int _expandCollapseCounter;
    private int _activeOperationsCount;
    private CancellationTokenSource? _chartLoadCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _zoomHistoryCts;
    private readonly Stack<(double Position, double Factor)> _zoomUndoStack = new();
    private readonly Stack<(double Position, double Factor)> _zoomRedoStack = new();
    private (double Position, double Factor) _committedZoom = (0.0, 1.0);
    private bool _isApplyingZoomHistory;

    /// <summary>Gets or sets the currently selected position lot in the Positions grid.</summary>
    [ObservableProperty]
    private PositionLot? _selectedLot;

    /// <summary>Gets or sets the currently selected dynamic ticker mapping row in Settings.</summary>
    [ObservableProperty]
    private TickerMapping? _selectedTickerMapping;

    /// <summary>Gets or sets the filtered collection of position lots bound to the Positions grid.</summary>
    [ObservableProperty]
    private ObservableCollection<PositionLot> _filteredLots = [];

    /// <summary>Gets or sets the collection of dynamic ticker mappings bound to the Settings grid.</summary>
    [ObservableProperty]
    private ObservableCollection<TickerMapping> _tickerMappings = [];

    /// <summary>Gets or sets the collection of portfolio dividend records bound to the Dividends grid.</summary>
    [ObservableProperty]
    private ObservableCollection<DividendRecord> _dividends = [];

    /// <summary>Gets or sets the quarterly/current portfolio history snapshots bound to the History tab.</summary>
    [ObservableProperty]
    private ObservableCollection<PortfolioHistoryPoint> _portfolioHistory = [];

    /// <summary>Gets or sets the editable country withholding tax rules bound to the Settings grid.</summary>
    [ObservableProperty]
    private ObservableCollection<CountryTaxRule> _taxRules = [];

    /// <summary>Gets or sets the broker account cash-flow records (deposits, withdrawals, interest).</summary>
    [ObservableProperty]
    private ObservableCollection<AccountCashFlowRecord> _cashFlows = [];

    /// <summary>Gets or sets the historical daily stock price series for <see cref="SelectedLot"/>.</summary>
    [ObservableProperty]
    private IReadOnlyList<StockPricePoint> _selectedLotPriceHistory = [];

    /// <summary>Gets or sets the optional minimum date for the stock price chart X-axis.</summary>
    [ObservableProperty]
    private DateTime? _chartXMinimum;

    /// <summary>Gets or sets the optional maximum date for the stock price chart X-axis.</summary>
    [ObservableProperty]
    private DateTime? _chartXMaximum;

    /// <summary>Gets or sets the normalized X-axis zoom start position (0.0 to 1.0) on the stock price chart.</summary>
    [ObservableProperty]
    private double _chartXZoomPosition;

    /// <summary>Gets or sets the normalized X-axis zoom factor (1.0 = full range) on the stock price chart.</summary>
    [ObservableProperty]
    private double _chartXZoomFactor = 1.0;

    /// <summary>Gets or sets the portfolio allocation breakdown by company/instrument for the Summary doughnut chart.</summary>
    [ObservableProperty]
    private IReadOnlyList<AllocationSlice> _allocationByInstrument = [];

    /// <summary>Gets or sets the portfolio allocation breakdown by country for the Summary doughnut chart.</summary>
    [ObservableProperty]
    private IReadOnlyList<AllocationSlice> _allocationByCountry = [];

    /// <summary>Gets or sets the yearly net dividend totals for the Summary column chart.</summary>
    [ObservableProperty]
    private IReadOnlyList<AllocationSlice> _yearlyDividendsChart = [];

    /// <summary>Gets or sets the active portfolio display/base currency code (e.g. <c>EUR</c>, <c>USD</c>, <c>CZK</c>).</summary>
    [ObservableProperty]
    private string _selectedBaseCurrency = "EUR";

    /// <summary>Gets or sets the active UI language code (<c>EN</c> or <c>SK</c>).</summary>
    [ObservableProperty]
    private string _selectedLanguage = "EN";

    /// <summary>Gets or sets the active Syncfusion visual theme name.</summary>
    [ObservableProperty]
    private string _selectedTheme = "Windows11Dark";

    /// <summary>Gets or sets the active status filter (<c>All</c>, <c>Open Only</c>, or <c>Closed Only</c>).</summary>
    [ObservableProperty]
    private string _selectedStatusFilter = "All";

    /// <summary>Gets or sets the live text search filter applied to the Positions grid.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Gets or sets a value indicating whether an asynchronous background operation is currently running.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Gets or sets the formatted elapsed duration of the current or most recent operation.</summary>
    [ObservableProperty]
    private string _operationTimeText = "0.00 s";

    /// <summary>Gets or sets the human-readable status message displayed in the bottom status bar.</summary>
    [ObservableProperty]
    private string _statusMessage = "Initializing...";

    /// <summary>Gets or sets the broker account number displayed in the top header bar.</summary>
    [ObservableProperty]
    private string _accountNumber = "1576650";

    /// <summary>Gets or sets the UTC timestamp when the last imported XTB report was generated.</summary>
    [ObservableProperty]
    private DateTime _reportDateUtc;

    /// <summary>Gets or sets the full path to the local <c>portfolio.json</c> storage file.</summary>
    [ObservableProperty]
    private string _storageFilePath = string.Empty;

    /// <summary>Gets or sets the Yahoo Finance quote URL for <see cref="SelectedLot"/>.</summary>
    [ObservableProperty]
    private string _selectedLotYahooUrl = "https://finance.yahoo.com";

    /// <summary>Gets or sets the URL currently loaded inside the embedded WebView2 browser sub-tab.</summary>
    [ObservableProperty]
    private string _activeEmbeddedYahooUrl = string.Empty;

    /// <summary>Gets or sets a value indicating whether the embedded Yahoo Finance web sub-tab is currently selected.</summary>
    [ObservableProperty]
    private bool _isYahooWebTabSelected;

    /// <summary>Gets or sets a signed signal counter requesting the Positions grid to expand (&gt; 0) or collapse (&lt; 0) all groups.</summary>
    [ObservableProperty]
    private int _groupExpandRequest;

    // Summary KPIs
    /// <summary>Gets or sets the total purchase cost of all currently open positions in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openInvestedBase;

    /// <summary>Gets or sets the total current market value of all open positions in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openMarketValueBase;

    /// <summary>Gets or sets the total unrealized capital profit/loss of open positions in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _openCapitalProfitBase;

    /// <summary>Gets or sets the total realized capital profit/loss of closed positions in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _closedRealizedProfitBase;

    /// <summary>Gets or sets the combined open + closed capital profit/loss in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalCapitalProfitBase;

    /// <summary>Gets or sets the cumulative gross dividends across all lots in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalGrossDividendsBase;

    /// <summary>Gets or sets the cumulative dividend withholding tax across all lots in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalWithholdingTaxBase;

    /// <summary>Gets or sets the cumulative net dividends received in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalNetDividendsBase;

    /// <summary>Gets or sets the cumulative net free-funds interest earned in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalFreeFundsInterestBase;

    /// <summary>Gets or sets the cumulative cash deposits into the account in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalDepositsBase;

    /// <summary>Gets or sets the overall portfolio profit (capital P/L + net dividends + interest) in <see cref="SelectedBaseCurrency"/>.</summary>
    [ObservableProperty]
    private decimal _totalOverallProfitBase;

    /// <summary>Gets or sets the overall portfolio return percentage relative to total invested/deposited capital.</summary>
    [ObservableProperty]
    private decimal _totalOverallReturnPercent;

    /// <summary>Gets or sets the number of currently open position lots.</summary>
    [ObservableProperty]
    private int _openLotsCount;

    /// <summary>Gets or sets the number of closed position lots.</summary>
    [ObservableProperty]
    private int _closedLotsCount;

    /// <summary>Gets or sets the number of distinct ticker symbols in the portfolio.</summary>
    [ObservableProperty]
    private int _distinctInstrumentsCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class and begins asynchronous portfolio loading.
    /// </summary>
    /// <param name="storageService">Local JSON portfolio persistence service.</param>
    /// <param name="calculationService">Portfolio valuation and tax calculation service.</param>
    /// <param name="marketDataProvider">Yahoo Finance quote and historical chart provider.</param>
    /// <param name="fxRateProvider">Online ECB exchange rate provider.</param>
    /// <param name="xtbImportService">XTB Excel/CSV report importer.</param>
    /// <param name="exportService">Excel/CSV portfolio exporter.</param>
    /// <param name="fileDialogService">UI-decoupled file dialog and browser launcher service.</param>
    public MainViewModel(
        PortfolioStorageService storageService,
        PortfolioCalculationService calculationService,
        YahooFinanceMarketDataProvider marketDataProvider,
        FrankfurterFxRateProvider fxRateProvider,
        XtbWorkbookImportService xtbImportService,
        ExportService exportService,
        IFileDialogService fileDialogService)
    {
        _storageService = storageService;
        _calculationService = calculationService;
        _marketDataProvider = marketDataProvider;
        _fxRateProvider = fxRateProvider;
        _xtbImportService = xtbImportService;
        _exportService = exportService;
        _fileDialogService = fileDialogService;

        _storageFilePath = _storageService.StorageFilePath;
        _ = InitializePortfolioAsync();
    }

    /// <summary>Gets the list of supported portfolio base currencies.</summary>
    public IReadOnlyList<string> AvailableCurrencies => FrankfurterFxRateProvider.SupportedCurrencies;

    /// <summary>Gets the list of supported UI language codes (<c>EN</c>, <c>SK</c>).</summary>
    public IReadOnlyList<string> AvailableLanguages => LocalizationService.SupportedLanguages;

    /// <summary>Gets the list of available Syncfusion visual themes.</summary>
    public IReadOnlyList<string> AvailableThemes { get; } =
    [
        "Windows11Dark",
        "Windows11Light",
        "FluentDark",
        "FluentLight",
        "Material3Dark",
        "Material3Light",
        "MaterialDark",
        "MaterialDarkBlue",
        "MaterialLight",
        "MaterialLightBlue",
        "Office2019Black",
        "Office2019Colorful",
        "Office2019DarkGray",
        "Office2019White",
        "Office2019HighContrast",
        "VisualStudio2015",
        "VisualStudio2013",
        "Metro",
        "Blend",
        "Saffron",
        "Lime"
    ];
    /// <summary>Gets the position status filter options (<c>All</c>, <c>Open Only</c>, <c>Closed Only</c>).</summary>
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["All", "Open Only", "Closed Only"];

    private async Task InitializePortfolioAsync()
    {
        await RunWithStatusTimerAsync(
            "Loading portfolio JSON storage and calculating initial metrics...",
            async (progress, ct) =>
            {
                _document = await _storageService.LoadOrSeedAsync(ct);

                _isInitializing = true;
                SelectedBaseCurrency = string.IsNullOrWhiteSpace(_document.Settings.BaseCurrency)
                    ? "EUR"
                    : _document.Settings.BaseCurrency;
                SelectedLanguage = string.IsNullOrWhiteSpace(_document.Settings.Language)
                    ? "EN"
                    : _document.Settings.Language;
                LocalizationService.Instance.CurrentLanguage = SelectedLanguage;
                SelectedTheme = string.IsNullOrWhiteSpace(_document.Settings.ThemeName)
                    ? "Windows11Dark"
                    : _document.Settings.ThemeName;
                _isInitializing = false;

                progress("Calculating positions, dividends, and portfolio history...");
                await ReloadFromDocumentAsync(reloadSelectedChart: true, ct);

                bool hasMissingPrices = _document.Lots.Any(l =>
                    (!l.IsOpenPriceManual && l.OpenPrice <= 0m) ||
                    (l.CloseDate.HasValue && !l.IsClosePriceManual && (!l.ClosePrice.HasValue || l.ClosePrice <= 0m)) ||
                    (!l.CloseDate.HasValue && !l.IsClosePriceManual && l.CurrentMarketPrice <= 0m));

                if (hasMissingPrices)
                {
                    progress("Fetching missing historical/live prices and dividends from Yahoo Finance...");
                    await _marketDataProvider.PopulateMissingPricesAndDividendsAsync(_document, progress, ct);
                }

                progress("Checking online ECB exchange rates for current day and transaction dates...");
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);

                progress("Recalculating portfolio with online ECB exchange rates...");
                await RecalculateAndRefreshAsync(reloadSelectedChart: false, ct);
                await _storageService.SaveAsync(_document, ct);

                return $"Portfolio loaded: {_document.Lots.Count} lots ({OpenLotsCount} open, {ClosedLotsCount} closed), {_document.TickerMappings.Count} tickers";
            });
    }

    partial void OnSelectedBaseCurrencyChanged(string value)
    {
        if (_isInitializing || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = SwitchBaseCurrencyAsync(value);
    }

    private async Task SwitchBaseCurrencyAsync(string currency)
    {
        await RunWithStatusTimerAsync(
            $"Switching base currency to {currency} and recalculating all positions, dividends, and history...",
            async (progress, ct) =>
            {
                _document.Settings.BaseCurrency = currency;
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                progress($"Recalculating all metrics in {currency}...");
                await RecalculateAndRefreshAsync(reloadSelectedChart: false, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Base currency switched to {currency}. All positions, dividends, and history recalculated";
            });
    }

    partial void OnSelectedThemeChanged(string value)
    {
        if (_isInitializing || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _document.Settings.ThemeName = value;
        _ = _storageService.SaveAsync(_document);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        LocalizationService.Instance.CurrentLanguage = value;
        if (_isInitializing)
        {
            return;
        }

        _document.Settings.Language = value;
        _ = _storageService.SaveAsync(_document);
    }

    partial void OnSelectedStatusFilterChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = TriggerFilterPositionsAsync(debounceMs: 10);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_isInitializing)
        {
            return;
        }

        _ = TriggerFilterPositionsAsync(debounceMs: 90);
    }

    private async Task TriggerFilterPositionsAsync(int debounceMs)
    {
        _filterCts?.Cancel();
        _filterCts?.Dispose();
        CancellationTokenSource cts = new();
        _filterCts = cts;
        CancellationToken token = cts.Token;

        try
        {
            if (debounceMs > 0)
            {
                await Task.Delay(debounceMs, token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunWithStatusTimerAsync(
            $"Filtering positions ('{SelectedStatusFilter}', search: '{SearchText}')...",
            async (_, ct) =>
            {
                await ApplyPositionFilterAsync(reloadSelectedChart: true, ct);
                return $"Showing {FilteredLots.Count} matching position lots";
            },
            token);
    }

    partial void OnIsYahooWebTabSelectedChanged(bool value)
    {
        if (value && !string.IsNullOrWhiteSpace(SelectedLotYahooUrl))
        {
            ActiveEmbeddedYahooUrl = SelectedLotYahooUrl;
        }
    }

    partial void OnSelectedLotChanged(PositionLot? value)
    {
        _chartLoadCts?.Cancel();
        _chartLoadCts?.Dispose();
        _chartLoadCts = null;

        _zoomHistoryCts?.Cancel();
        _zoomUndoStack.Clear();
        _zoomRedoStack.Clear();
        _isApplyingZoomHistory = true;
        ChartXZoomPosition = 0.0;
        ChartXZoomFactor = 1.0;
        _committedZoom = (0.0, 1.0);
        _isApplyingZoomHistory = false;
        NotifyZoomCommandsCanExecuteChanged();

        if (value == null)
        {
            ChartXMinimum = null;
            ChartXMaximum = null;
            SelectedLotPriceHistory = [];
            SelectedLotYahooUrl = "https://finance.yahoo.com";
            return;
        }

        ChartXMinimum = value.OpenDate.Date;
        DateTime endRange = (value.CloseDate ?? DateTime.UtcNow).Date;
        ChartXMaximum = endRange > value.OpenDate.Date ? endRange : value.OpenDate.Date.AddDays(1);

        string sym = string.IsNullOrWhiteSpace(value.YahooSymbol)
            ? TickerMappingHelper.Resolve(value.Ticker, value.Category, _document.TickerMappings).YahooSymbol
            : value.YahooSymbol;
        SelectedLotYahooUrl = $"https://finance.yahoo.com/quote/{Uri.EscapeDataString(sym)}";
        if (IsYahooWebTabSelected)
        {
            ActiveEmbeddedYahooUrl = SelectedLotYahooUrl;
        }

        CancellationTokenSource cts = new();
        _chartLoadCts = cts;
        _ = LoadSelectedLotChartAsync(value, sym, cts.Token);
    }

    partial void OnChartXZoomPositionChanged(double value) => ScheduleZoomHistoryPush();

    partial void OnChartXZoomFactorChanged(double value) => ScheduleZoomHistoryPush();

    private void ScheduleZoomHistoryPush()
    {
        if (_isApplyingZoomHistory)
        {
            return;
        }

        _zoomHistoryCts?.Cancel();
        _zoomHistoryCts?.Dispose();
        CancellationTokenSource cts = new();
        _zoomHistoryCts = cts;
        _ = CommitZoomSnapshotAsync(cts.Token);
    }

    private async Task CommitZoomSnapshotAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(60, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        (double Position, double Factor) current = (Math.Round(ChartXZoomPosition, 6), Math.Round(ChartXZoomFactor, 6));
        (double Position, double Factor) prev = (Math.Round(_committedZoom.Position, 6), Math.Round(_committedZoom.Factor, 6));
        if (current == prev)
        {
            return;
        }

        _zoomUndoStack.Push(_committedZoom);
        _zoomRedoStack.Clear();
        _committedZoom = (ChartXZoomPosition, ChartXZoomFactor);
        NotifyZoomCommandsCanExecuteChanged();
    }

    private void NotifyZoomCommandsCanExecuteChanged()
    {
        UndoChartZoomCommand.NotifyCanExecuteChanged();
        RedoChartZoomCommand.NotifyCanExecuteChanged();
        ResetChartZoom100Command.NotifyCanExecuteChanged();
    }

    private bool CanUndoChartZoom() => _zoomUndoStack.Count > 0;
    private bool CanRedoChartZoom() => _zoomRedoStack.Count > 0;
    private bool CanResetChartZoom100() =>
        SelectedLot != null &&
        (Math.Abs(ChartXZoomPosition) > 0.0001 || Math.Abs(ChartXZoomFactor - 1.0) > 0.0001);

    /// <summary>Resets the stock price chart horizontal zoom back to 100% (full visible date range).</summary>
    [RelayCommand(CanExecute = nameof(CanResetChartZoom100))]
    private void ResetChartZoom100()
    {
        if (SelectedLot == null)
        {
            return;
        }

        _zoomHistoryCts?.Cancel();
        if (Math.Abs(_committedZoom.Position) > 0.0001 || Math.Abs(_committedZoom.Factor - 1.0) > 0.0001)
        {
            _zoomUndoStack.Push(_committedZoom);
            _zoomRedoStack.Clear();
        }

        _isApplyingZoomHistory = true;
        ChartXMinimum = SelectedLot.OpenDate.Date;
        DateTime endRange = (SelectedLot.CloseDate ?? DateTime.UtcNow).Date;
        ChartXMaximum = endRange > SelectedLot.OpenDate.Date ? endRange : SelectedLot.OpenDate.Date.AddDays(1);
        ChartXZoomPosition = 0.0;
        ChartXZoomFactor = 1.0;
        _committedZoom = (0.0, 1.0);
        _isApplyingZoomHistory = false;
        NotifyZoomCommandsCanExecuteChanged();
    }

    /// <summary>Restores the previous horizontal zoom state on the stock price chart from the undo stack.</summary>
    [RelayCommand(CanExecute = nameof(CanUndoChartZoom))]
    private void UndoChartZoom()
    {
        if (_zoomUndoStack.Count == 0)
        {
            return;
        }

        _zoomHistoryCts?.Cancel();
        _zoomRedoStack.Push(_committedZoom);
        (double pos, double factor) = _zoomUndoStack.Pop();

        _isApplyingZoomHistory = true;
        ChartXZoomPosition = pos;
        ChartXZoomFactor = factor;
        _committedZoom = (pos, factor);
        _isApplyingZoomHistory = false;
        NotifyZoomCommandsCanExecuteChanged();
    }

    /// <summary>Reapplies the most recently undone horizontal zoom state on the stock price chart.</summary>
    [RelayCommand(CanExecute = nameof(CanRedoChartZoom))]
    private void RedoChartZoom()
    {
        if (_zoomRedoStack.Count == 0)
        {
            return;
        }

        _zoomHistoryCts?.Cancel();
        _zoomUndoStack.Push(_committedZoom);
        (double pos, double factor) = _zoomRedoStack.Pop();

        _isApplyingZoomHistory = true;
        ChartXZoomPosition = pos;
        ChartXZoomFactor = factor;
        _committedZoom = (pos, factor);
        _isApplyingZoomHistory = false;
        NotifyZoomCommandsCanExecuteChanged();
    }

    private async Task LoadSelectedLotChartAsync(PositionLot lot, string yahooSym, CancellationToken cancellationToken)
    {
        try
        {
            // Short debounce so rapid keyboard/mouse row navigation cancels immediately without starting I/O
            await Task.Delay(35, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RunWithStatusTimerAsync(
            $"Loading historical price chart for {lot.CompanyName} ({yahooSym})...",
            async (_, ct) =>
            {
                List<StockPricePoint> series = await _marketDataProvider.GetHistoricalSeriesAsync(lot, ct);
                ct.ThrowIfCancellationRequested();
                SelectedLotPriceHistory = series;
                return $"Selected {lot.CompanyName} ({yahooSym}) — loaded {series.Count} historical price points";
            },
            cancellationToken);
    }

    /// <summary>
    /// Synchronizes dynamic ticker mappings and tax rules from the current document and recalculates all portfolio collections.
    /// </summary>
    /// <param name="reloadSelectedChart">Whether to reload the price chart for the currently selected lot.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReloadFromDocumentAsync(bool reloadSelectedChart = true, CancellationToken cancellationToken = default)
    {
        AccountNumber = _document.AccountNumber;
        ReportDateUtc = _document.ReportGeneratedUtc;

        List<TickerMapping> sortedMappings = await Task.Run(() =>
        {
            TickerMappingHelper.SynchronizeDocumentMappings(_document);
            return _document.TickerMappings
                .OrderBy(m => m.Ticker, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);

        TickerMappings = new ObservableCollection<TickerMapping>(sortedMappings);
        TaxRules = new ObservableCollection<CountryTaxRule>(_document.TaxRules);

        await RecalculateAndRefreshAsync(reloadSelectedChart, cancellationToken);
    }

    private async Task RecalculateAndRefreshAsync(bool reloadSelectedChart = true, CancellationToken cancellationToken = default)
    {
        string statusFilter = SelectedStatusFilter;
        string search = SearchText;

        var computed = await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _calculationService.RecalculateAll(_document);

            List<PositionLot> filtered = FilterLotsInternal(_document.Lots, statusFilter, search);
            List<DividendRecord> sortedDivs = _document.Dividends.OrderByDescending(x => x.PayDate).ToList();
            List<AccountCashFlowRecord> sortedCash = _document.CashFlows.OrderByDescending(x => x.TimeUtc).ToList();
            List<PortfolioHistoryPoint> historyPoints = _calculationService.BuildPortfolioHistory(_document);
            SummarySnapshot summary = ComputeSummarySnapshot(_document);

            return (filtered, sortedDivs, sortedCash, historyPoints, summary);
        }, cancellationToken);

        Guid? prevId = SelectedLot?.Id;
        FilteredLots = new ObservableCollection<PositionLot>(computed.filtered);
        Dividends = new ObservableCollection<DividendRecord>(computed.sortedDivs);
        CashFlows = new ObservableCollection<AccountCashFlowRecord>(computed.sortedCash);
        PortfolioHistory = new ObservableCollection<PortfolioHistoryPoint>(computed.historyPoints);

        ApplySummarySnapshot(computed.summary);

        PositionLot? nextLot = FilteredLots.FirstOrDefault(l => l.Id == prevId) ?? FilteredLots.FirstOrDefault();
        if (ReferenceEquals(SelectedLot, nextLot))
        {
            if (reloadSelectedChart && nextLot != null)
            {
                OnSelectedLotChanged(nextLot);
            }
        }
        else
        {
            SelectedLot = nextLot;
        }
    }

    private async Task ApplyPositionFilterAsync(bool reloadSelectedChart = true, CancellationToken cancellationToken = default)
    {
        string statusFilter = SelectedStatusFilter;
        string search = SearchText;
        Guid? prevId = SelectedLot?.Id;

        List<PositionLot> filtered = await Task.Run(
            () => FilterLotsInternal(_document.Lots, statusFilter, search),
            cancellationToken);

        FilteredLots = new ObservableCollection<PositionLot>(filtered);

        PositionLot? nextLot = FilteredLots.FirstOrDefault(l => l.Id == prevId) ?? FilteredLots.FirstOrDefault();
        if (ReferenceEquals(SelectedLot, nextLot))
        {
            if (reloadSelectedChart && nextLot != null)
            {
                OnSelectedLotChanged(nextLot);
            }
        }
        else
        {
            SelectedLot = nextLot;
        }
    }

    private static List<PositionLot> FilterLotsInternal(
        List<PositionLot> allLots,
        string statusFilter,
        string searchText)
    {
        IEnumerable<PositionLot> query = allLots
            .OrderBy(l => l.CompanyName)
            .ThenBy(l => l.OpenDate);

        if (statusFilter == "Open Only")
        {
            query = query.Where(l => !l.CloseDate.HasValue);
        }
        else if (statusFilter == "Closed Only")
        {
            query = query.Where(l => l.CloseDate.HasValue);
        }

        if (!string.IsNullOrWhiteSpace(searchText))
        {
            string s = searchText.Trim();
            query = query.Where(l =>
                l.CompanyName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                l.Ticker.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                l.YahooSymbol.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                l.ExternalPositionId.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                l.Comment.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return query.ToList();
    }

    private sealed record SummarySnapshot(
        int OpenLotsCount,
        int ClosedLotsCount,
        int DistinctInstrumentsCount,
        decimal OpenInvestedBase,
        decimal OpenMarketValueBase,
        decimal OpenCapitalProfitBase,
        decimal ClosedRealizedProfitBase,
        decimal TotalCapitalProfitBase,
        decimal TotalGrossDividendsBase,
        decimal TotalWithholdingTaxBase,
        decimal TotalNetDividendsBase,
        decimal TotalFreeFundsInterestBase,
        decimal TotalDepositsBase,
        decimal TotalOverallProfitBase,
        decimal TotalOverallReturnPercent,
        List<AllocationSlice> ByInstrument,
        List<AllocationSlice> ByCountry,
        List<AllocationSlice> YearlyDividends);

    private static SummarySnapshot ComputeSummarySnapshot(PortfolioDocument doc)
    {
        List<PositionLot> openLots = doc.Lots.Where(l => !l.CloseDate.HasValue).ToList();
        List<PositionLot> closedLots = doc.Lots.Where(l => l.CloseDate.HasValue).ToList();

        int openCount = openLots.Count;
        int closedCount = closedLots.Count;
        int distinctTickers = doc.Lots
            .Select(l => l.Ticker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        decimal openInvested = Math.Round(openLots.Sum(l => l.OpenCostBase), 2);
        decimal openMarket = Math.Round(openLots.Sum(l => l.CloseOrCurrentValueBase), 2);
        decimal openCapProfit = Math.Round(openLots.Sum(l => l.CapitalProfitLossBase), 2);
        decimal closedProfit = Math.Round(closedLots.Sum(l => l.CapitalProfitLossBase), 2);
        decimal totalCapProfit = Math.Round(openCapProfit + closedProfit, 2);

        decimal grossDiv = Math.Round(doc.Dividends.Sum(d => d.GrossAmountBase), 2);
        decimal whtDiv = Math.Round(doc.Dividends.Sum(d => d.WithholdingTaxBase), 2);
        decimal netDiv = Math.Round(doc.Dividends.Sum(d => d.NetAmountBase), 2);

        decimal freeFundsInt = Math.Round(
            doc.CashFlows
                .Where(c => c.OperationType == CashOperationType.FreeFundsInterest ||
                            c.OperationType == CashOperationType.FreeFundsInterestTax)
                .Sum(c => c.AmountBase), 2);

        decimal deposits = Math.Round(
            doc.CashFlows
                .Where(c => c.OperationType == CashOperationType.Deposit)
                .Sum(c => c.AmountBase), 2);

        decimal spinoffsBase = Math.Round(doc.Lots.Sum(l => l.SpinoffProceedsBase), 2);
        decimal overallProfit = Math.Round(totalCapProfit + netDiv + freeFundsInt + spinoffsBase, 2);
        decimal overallReturnPct = openInvested > 0
            ? Math.Round(overallProfit / openInvested * 100m, 2)
            : 0m;

        double totalOpenVal = (double)Math.Max(1m, openMarket);
        List<AllocationSlice> byInstrument = openLots
            .GroupBy(l => l.CompanyName)
            .Select(g => new { Name = g.Key, Val = (double)g.Sum(x => x.CloseOrCurrentValueBase) })
            .OrderByDescending(x => x.Val)
            .Take(14)
            .Select(grp => new AllocationSlice
            {
                Label = grp.Name,
                ValueBase = Math.Round(grp.Val, 2),
                SharePercent = Math.Round(grp.Val / totalOpenVal * 100.0, 2)
            })
            .ToList();

        List<AllocationSlice> byCountry = openLots
            .GroupBy(l => l.CountryCode)
            .Select(g => new { Country = g.Key, Val = (double)g.Sum(x => x.CloseOrCurrentValueBase) })
            .OrderByDescending(x => x.Val)
            .Select(grp => new AllocationSlice
            {
                Label = grp.Country,
                ValueBase = Math.Round(grp.Val, 2),
                SharePercent = Math.Round(grp.Val / totalOpenVal * 100.0, 2)
            })
            .ToList();

        List<AllocationSlice> yearlyDivs = doc.Dividends
            .GroupBy(d => d.PayDate.Year)
            .OrderBy(g => g.Key)
            .Select(grp => new AllocationSlice
            {
                Label = grp.Key.ToString(),
                ValueBase = (double)Math.Round(grp.Sum(x => x.NetAmountBase), 2),
                SharePercent = (double)Math.Round(grp.Sum(x => x.WithholdingTaxBase), 2)
            })
            .ToList();

        return new SummarySnapshot(
            openCount,
            closedCount,
            distinctTickers,
            openInvested,
            openMarket,
            openCapProfit,
            closedProfit,
            totalCapProfit,
            grossDiv,
            whtDiv,
            netDiv,
            freeFundsInt,
            deposits,
            overallProfit,
            overallReturnPct,
            byInstrument,
            byCountry,
            yearlyDivs);
    }

    private void ApplySummarySnapshot(SummarySnapshot s)
    {
        OpenLotsCount = s.OpenLotsCount;
        ClosedLotsCount = s.ClosedLotsCount;
        DistinctInstrumentsCount = s.DistinctInstrumentsCount;
        OpenInvestedBase = s.OpenInvestedBase;
        OpenMarketValueBase = s.OpenMarketValueBase;
        OpenCapitalProfitBase = s.OpenCapitalProfitBase;
        ClosedRealizedProfitBase = s.ClosedRealizedProfitBase;
        TotalCapitalProfitBase = s.TotalCapitalProfitBase;
        TotalGrossDividendsBase = s.TotalGrossDividendsBase;
        TotalWithholdingTaxBase = s.TotalWithholdingTaxBase;
        TotalNetDividendsBase = s.TotalNetDividendsBase;
        TotalFreeFundsInterestBase = s.TotalFreeFundsInterestBase;
        TotalDepositsBase = s.TotalDepositsBase;
        TotalOverallProfitBase = s.TotalOverallProfitBase;
        TotalOverallReturnPercent = s.TotalOverallReturnPercent;
        AllocationByInstrument = s.ByInstrument;
        AllocationByCountry = s.ByCountry;
        YearlyDividendsChart = s.YearlyDividends;
    }

    /// <summary>Saves manual edits on <see cref="SelectedLot"/>, fetches any cleared/missing prices and dividends, recalculates the portfolio, and persists to JSON.</summary>
    [RelayCommand]
    private async Task SaveSelectedLotOverridesAsync()
    {
        if (SelectedLot == null)
        {
            return;
        }

        PositionLot lot = SelectedLot;
        await RunWithStatusTimerAsync(
            $"Saving overrides for '{lot.CompanyName}' ({lot.Ticker}) and recalculating portfolio...",
            async (progress, ct) =>
            {
                TickerMapping mapping = TickerMappingHelper.ResolveOrUpsert(
                    _document.TickerMappings,
                    lot.Ticker,
                    lot.CompanyName,
                    lot.Category,
                    lot.StockCurrency);

                if (!string.IsNullOrWhiteSpace(lot.YahooSymbol))
                {
                    mapping.YahooSymbol = lot.YahooSymbol.Trim();
                }
                else
                {
                    lot.YahooSymbol = mapping.YahooSymbol;
                }

                if (!string.IsNullOrWhiteSpace(lot.CountryCode))
                {
                    mapping.CountryCode = lot.CountryCode.Trim().ToUpperInvariant();
                }

                if (!string.IsNullOrWhiteSpace(lot.StockCurrency))
                {
                    mapping.Currency = lot.StockCurrency.Trim().ToUpperInvariant();
                }

                if (!string.IsNullOrWhiteSpace(lot.CompanyName))
                {
                    mapping.CompanyName = lot.CompanyName.Trim();
                }

                mapping.IsManualOverride = true;

                // If the lot was closed manually without entering a close price, clear broker sale overrides so Yahoo historical price is used.
                if (lot.CloseDate.HasValue && !lot.IsClosePriceManual && (!lot.ClosePrice.HasValue || lot.ClosePrice <= 0m))
                {
                    lot.BrokerSaleOrCurrentValueEur = null;
                    lot.CloseConversionRate = null;
                }

                // Populate any cleared/missing prices and dividends from Yahoo Finance first, then fetch ECB FX rates (including new dividend PayDates).
                (int filledPrices, int fetchedDivs) = await _marketDataProvider.PopulateMissingPricesAndDividendsAsync(_document, progress, ct);
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                progress($"Recalculating portfolio and updating ticker mapping for {lot.Ticker}...");
                await ReloadFromDocumentAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return filledPrices > 0 || fetchedDivs > 0
                    ? $"Saved '{lot.CompanyName}' ({lot.Ticker}): filled {filledPrices} price(s), {fetchedDivs} dividend(s)"
                    : $"Saved changes and updated ticker mapping for '{lot.CompanyName}' ({lot.Ticker})";
            });
    }

    /// <summary>Adds a new manual position lot with blank prices (ready for automatic online price and dividend fetch on save).</summary>
    [RelayCommand]
    private async Task AddNewPositionAsync()
    {
        await RunWithStatusTimerAsync(
            "Adding new position lot...",
            async (_, ct) =>
            {
                PositionLot newLot = new()
                {
                    ExternalPositionId = $"MAN-{DateTime.UtcNow:yyyyMMddHHmmss}",
                    CompanyName = "New Position",
                    Ticker = "AAPL.US",
                    YahooSymbol = "AAPL",
                    Category = "STOCK",
                    CountryCode = "US",
                    StockCurrency = "USD",
                    Quantity = 10m,
                    OpenDate = DateTime.UtcNow.Date,
                    OpenPrice = 0m,
                    CurrentMarketPrice = 0m,
                    IsOpenPriceManual = false,
                    IsClosePriceManual = false,
                    Source = DataSource.ManualEntry
                };

                _document.Lots.Add(newLot);
                await ReloadFromDocumentAsync(reloadSelectedChart: false, ct);
                SelectedLot = newLot;
                await _storageService.SaveAsync(_document, ct);
                return "Added new position lot. Enter Company Name, Ticker, Open Date, Quantity (leave prices blank for auto-fetch) and click Save";
            });
    }

    /// <summary>Deletes <see cref="SelectedLot"/> from the portfolio, recalculates metrics, and saves to JSON.</summary>
    [RelayCommand]
    private async Task DeleteSelectedPositionAsync()
    {
        if (SelectedLot == null)
        {
            return;
        }

        PositionLot toRemove = SelectedLot;
        string title = $"{toRemove.CompanyName} ({toRemove.Ticker})";
        await RunWithStatusTimerAsync(
            $"Deleting position lot {title} and recalculating portfolio...",
            async (_, ct) =>
            {
                _document.Lots.Remove(toRemove);
                await RecalculateAndRefreshAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Removed position lot {title}";
            });
    }

    /// <summary>Saves edited country withholding tax rules, recalculates dividend taxes, and persists to JSON.</summary>
    [RelayCommand]
    private async Task SaveTaxRulesAsync()
    {
        await RunWithStatusTimerAsync(
            "Saving country withholding tax rules and recalculating portfolio...",
            async (_, ct) =>
            {
                _document.TaxRules = TaxRules.ToList();
                await RecalculateAndRefreshAsync(reloadSelectedChart: false, ct);
                await _storageService.SaveAsync(_document, ct);
                return "Saved country withholding tax rules and recalculated portfolio";
            });
    }

    /// <summary>Adds a new editable dynamic ticker mapping row in Settings and persists to JSON.</summary>
    [RelayCommand]
    private async Task AddTickerMappingAsync()
    {
        await RunWithStatusTimerAsync(
            "Adding new dynamic ticker mapping...",
            async (_, ct) =>
            {
                TickerMapping newMapping = new()
                {
                    Ticker = "NEW.US",
                    CompanyName = "New Instrument",
                    YahooSymbol = "NEW",
                    Category = "STOCK",
                    CountryCode = "US",
                    Currency = "USD",
                    IsManualOverride = true
                };

                _document.TickerMappings.Add(newMapping);
                TickerMappings.Insert(0, newMapping);
                SelectedTickerMapping = newMapping;
                await _storageService.SaveAsync(_document, ct);
                return "Added new ticker mapping row. Edit its fields and click 'Save Tickers & Recalculate'";
            });
    }

    /// <summary>Deletes <see cref="SelectedTickerMapping"/> from the dynamic ticker mapping table and persists to JSON.</summary>
    [RelayCommand]
    private async Task DeleteSelectedTickerMappingAsync()
    {
        if (SelectedTickerMapping == null)
        {
            return;
        }

        TickerMapping toDelete = SelectedTickerMapping;
        string ticker = toDelete.Ticker;
        await RunWithStatusTimerAsync(
            $"Deleting ticker mapping '{ticker}'...",
            async (_, ct) =>
            {
                _document.TickerMappings.Remove(toDelete);
                TickerMappings.Remove(toDelete);
                TickerMappingHelper.RefreshTaxRuleSampleTickers(_document);
                await _storageService.SaveAsync(_document, ct);
                return $"Deleted ticker mapping '{ticker}'";
            });
    }

    /// <summary>Saves all dynamic ticker mapping edits, synchronizes lots, recalculates the portfolio, and persists to JSON.</summary>
    [RelayCommand]
    private async Task SaveTickerMappingsAsync()
    {
        await RunWithStatusTimerAsync(
            "Saving dynamic ticker mappings to JSON and recalculating portfolio...",
            async (progress, ct) =>
            {
                foreach (TickerMapping m in TickerMappings)
                {
                    m.IsManualOverride = true;
                }

                _document.TickerMappings = TickerMappings.ToList();
                TickerMappingHelper.SynchronizeDocumentMappings(_document);
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                await RecalculateAndRefreshAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Saved {_document.TickerMappings.Count} dynamic ticker mappings to JSON and recalculated portfolio";
            });
    }

    /// <summary>Synchronizes live open-lot prices, missing historical prices, Yahoo dividends, and online ECB FX rates, then recalculates through today.</summary>
    [RelayCommand]
    private async Task RefreshMarketDataAsync()
    {
        await RunWithStatusTimerAsync(
            "Syncing Yahoo Finance prices & dividends and online ECB exchange rates...",
            async (progress, ct) =>
            {
                (int updatedPrices, int fetchedDividends) = await _marketDataProvider.RefreshAllMarketDataAsync(_document, progress, ct);
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                _document.Settings.LastMarketSyncUtc = DateTime.UtcNow;

                progress("Recalculating portfolio, dividends, and history through today...");
                await RecalculateAndRefreshAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Market sync finished: updated {updatedPrices} price(s), {fetchedDividends} Yahoo dividend(s), and online ECB exchange rates";
            });
    }

    /// <summary>Resets local portfolio storage by re-seeding from the bundled XTB seed report.</summary>
    [RelayCommand]
    private async Task ResetToBundledXtbReportAsync()
    {
        await RunWithStatusTimerAsync(
            "Reloading bundled XTB seed report and rebuilding dynamic ticker mappings...",
            async (progress, ct) =>
            {
                PortfolioDocument seeded = await _storageService.CreateSeededDocumentAsync(ct);
                seeded.Settings.BaseCurrency = SelectedBaseCurrency;
                seeded.Settings.ThemeName = SelectedTheme;
                _document = seeded;

                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                progress("Recalculating seeded portfolio...");
                await ReloadFromDocumentAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Reloaded bundled XTB report ({_document.Lots.Count} lots, {_document.Dividends.Count} dividends)";
            });
    }

    /// <summary>Opens a file picker to import an XTB Excel (<c>.xls</c>/<c>.xlsx</c>) or CSV report while preserving manual lots and notes.</summary>
    [RelayCommand]
    private async Task ImportXtbAsync()
    {
        string? filePath = _fileDialogService.ShowOpenXtbFileDialog();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            await ImportXtbFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Import Error", $"Failed to import XTB file:\n{ex.Message}");
        }
    }

    /// <summary>Opens a save dialog to export the current portfolio positions, dividends, and history to Excel (<c>.xlsx</c>) or CSV.</summary>
    [RelayCommand]
    private async Task ExportPortfolioAsync()
    {
        string defaultFileName = $"StockMonitor_{SelectedBaseCurrency}_{DateTime.Now:yyyyMMdd}";
        string? filePath = _fileDialogService.ShowSaveExportFileDialog(defaultFileName);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            await ExportPortfolioFileAsync(filePath);
        }
        catch (Exception ex)
        {
            _fileDialogService.ShowError("Export Error", $"Failed to export portfolio:\n{ex.Message}");
        }
    }

    /// <summary>Signals the Positions grid to expand all company groups.</summary>
    [RelayCommand]
    private void ExpandAllGroups()
    {
        _expandCollapseCounter++;
        GroupExpandRequest = _expandCollapseCounter;
    }

    /// <summary>Signals the Positions grid to collapse all company groups.</summary>
    [RelayCommand]
    private void CollapseAllGroups()
    {
        _expandCollapseCounter++;
        GroupExpandRequest = -_expandCollapseCounter;
    }

    /// <summary>Launches <see cref="SelectedLotYahooUrl"/> in the user's default external web browser.</summary>
    [RelayCommand]
    private void OpenYahooInExternalBrowser()
    {
        if (!string.IsNullOrWhiteSpace(SelectedLotYahooUrl))
        {
            _fileDialogService.OpenUrlInExternalBrowser(SelectedLotYahooUrl);
        }
    }

    /// <summary>Loads or reloads <see cref="SelectedLotYahooUrl"/> inside the embedded WebView2 browser sub-tab.</summary>
    [RelayCommand]
    private void LoadYahooInEmbeddedBrowser()
    {
        if (!string.IsNullOrWhiteSpace(SelectedLotYahooUrl))
        {
            ActiveEmbeddedYahooUrl = string.Empty;
            ActiveEmbeddedYahooUrl = SelectedLotYahooUrl;
        }
    }

    /// <summary>
    /// Imports an XTB Excel or CSV report asynchronously, fetches online ECB FX rates, and recalculates the portfolio.
    /// </summary>
    /// <param name="filePath">Path to the XTB report file.</param>
    public async Task ImportXtbFileAsync(string filePath)
    {
        string fileName = System.IO.Path.GetFileName(filePath);
        await RunWithStatusTimerAsync(
            $"Importing XTB file '{fileName}' and updating dynamic ticker mappings...",
            async (progress, ct) =>
            {
                await Task.Run(() => _xtbImportService.ImportFile(filePath, _document, replaceExistingXtbData: true), ct);
                await _fxRateProvider.EnsurePortfolioRatesAsync(_document, progress, ct);
                progress($"Recalculating imported portfolio from '{fileName}'...");
                await ReloadFromDocumentAsync(reloadSelectedChart: true, ct);
                await _storageService.SaveAsync(_document, ct);
                return $"Imported XTB file '{fileName}': {_document.Lots.Count} lots ({OpenLotsCount} open, {ClosedLotsCount} closed), {_document.Dividends.Count} dividends";
            });
    }

    /// <summary>
    /// Exports the portfolio positions, dividends, history, and tax rules asynchronously to Excel or CSV.
    /// </summary>
    /// <param name="filePath">Destination file path.</param>
    public async Task ExportPortfolioFileAsync(string filePath)
    {
        string fileName = System.IO.Path.GetFileName(filePath);
        List<PortfolioHistoryPoint> historySnapshot = PortfolioHistory.ToList();
        await RunWithStatusTimerAsync(
            $"Exporting portfolio report to '{fileName}'...",
            async (_, ct) =>
            {
                await Task.Run(() => _exportService.ExportPortfolio(_document, historySnapshot, filePath), ct);
                return $"Exported portfolio report to '{filePath}'";
            });
    }

    private async Task RunWithStatusTimerAsync(
        string inProgressMessage,
        Func<Action<string>, CancellationToken, Task<string>> operation,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        Interlocked.Increment(ref _activeOperationsCount);
        IsBusy = true;
        StatusMessage = inProgressMessage;
        OperationTimeText = "0.00 s";

        using CancellationTokenSource timerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task timerTask = UpdateElapsedTimerLoopAsync(sw, timerCts.Token);

        try
        {
            string completedSummary = await operation(msg => StatusMessage = msg, cancellationToken);
            sw.Stop();
            await timerCts.CancelAsync();
            OperationTimeText = $"{sw.Elapsed.TotalSeconds:F2} s";
            StatusMessage = $"{completedSummary} (in {sw.Elapsed.TotalSeconds:F2} s).";
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            await timerCts.CancelAsync();
        }
        catch (Exception ex)
        {
            sw.Stop();
            await timerCts.CancelAsync();
            OperationTimeText = $"{sw.Elapsed.TotalSeconds:F2} s";
            StatusMessage = $"Operation finished with warning: {ex.Message} ({sw.Elapsed.TotalSeconds:F2} s).";
        }
        finally
        {
            try
            {
                await timerTask;
            }
            catch
            {
                // Ignore timer cancellation
            }

            if (Interlocked.Decrement(ref _activeOperationsCount) <= 0)
            {
                Interlocked.Exchange(ref _activeOperationsCount, 0);
                IsBusy = false;
            }
        }
    }

    private async Task UpdateElapsedTimerLoopAsync(Stopwatch sw, CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
        {
            OperationTimeText = $"{sw.Elapsed.TotalSeconds:F2} s";
        }
    }
}

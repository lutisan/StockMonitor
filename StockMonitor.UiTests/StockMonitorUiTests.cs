using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using StockMonitor.UiTests.Fixtures;

namespace StockMonitor.UiTests;

/// <summary>
/// End-to-end FlaUI (UIA3) desktop automation tests for the <c>StockMonitor</c> WPF application.
/// Each test runs against an isolated temporary <c>STOCKMONITOR_DATA_DIR</c> so user data is never modified.
/// </summary>
[Collection("FlaUI")]
public sealed class StockMonitorUiTests
{
    private readonly StockMonitorAppFixture _fixture;

    /// <summary>
    /// Initializes a new instance of the <see cref="StockMonitorUiTests"/> class.
    /// </summary>
    public StockMonitorUiTests(StockMonitorAppFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Verifies that StockMonitor launches cleanly without modal crash dialogs, seeds the initial portfolio,
    /// displays positive open/closed/ticker counts, and points local JSON storage to the isolated test folder.
    /// </summary>
    [Fact]
    public void AppStartsCleanly_LoadsSeededPortfolio_AndUsesIsolatedDataDirectory()
    {
        // Assert: no error popup and window title is populated
        _fixture.AssertNoErrorDialogOpen();
        Assert.Contains("StockMonitor", _fixture.MainWindow.Title, StringComparison.OrdinalIgnoreCase);

        // Assert: status bar shows the isolated temporary portfolio.json path
        string storageText = _fixture.FindById("TxtStorageFilePath").Name;
        Assert.Contains(_fixture.IsolatedDataDir, storageText, StringComparison.OrdinalIgnoreCase);

        // Assert: header counters show seeded open & closed lots
        int openLots = int.Parse(_fixture.FindById("TxtOpenLotsCount").Name);
        int closedLots = int.Parse(_fixture.FindById("TxtClosedLotsCount").Name);
        int tickers = int.Parse(_fixture.FindById("TxtDistinctInstrumentsCount").Name);

        Assert.True(openLots > 0, $"Expected seeded open lots > 0, got {openLots}.");
        Assert.True(closedLots > 0, $"Expected seeded closed lots > 0, got {closedLots}.");
        Assert.True(tickers > 0, $"Expected distinct tickers > 0, got {tickers}.");
    }

    /// <summary>
    /// Verifies that the user can click through all 5 main Syncfusion tabs
    /// (Positions, Summary &amp; Overview, Dividends, Portfolio History, Settings) and return to Positions
    /// without triggering any unhandled exceptions or error dialogs.
    /// </summary>
    [Fact]
    public void MainTabs_CanNavigateAcrossAllFiveTabs_WithoutErrors()
    {
        int[] tabOrder = [1, 2, 3, 4, 0];

        foreach (int tabIndex in tabOrder)
        {
            _fixture.SelectMainTab(tabIndex);
            _fixture.AssertNoErrorDialogOpen();
        }

        // Verify Positions view controls are visible again when back on Tab 1 (index 0)
        AutomationElement searchBox = _fixture.FindById("TxtSearchBox");
        Assert.False(searchBox.IsOffscreen);
    }

    /// <summary>
    /// Verifies that changing the Status Filter combo box (<c>Open Only</c> / <c>Closed Only</c> / <c>All</c>)
    /// and typing into the Search box works smoothly without UI errors.
    /// </summary>
    [Fact]
    public void PositionsToolbar_FilterAndSearch_UpdatesCleanly()
    {
        // Ensure we are on the Positions tab (index 0)
        _fixture.SelectMainTab(0);

        // Act: cycle through status filters
        _fixture.SelectComboBoxItem("CmbStatusFilter", "Open Only");
        _fixture.AssertNoErrorDialogOpen();

        _fixture.SelectComboBoxItem("CmbStatusFilter", "Closed Only");
        _fixture.AssertNoErrorDialogOpen();

        _fixture.SelectComboBoxItem("CmbStatusFilter", "All");
        _fixture.AssertNoErrorDialogOpen();

        // Act: enter a search query via UIA ValuePattern and then clear it
        TextBox searchBox = _fixture.FindById("TxtSearchBox").AsTextBox();
        searchBox.Text = "LYPG";
        _fixture.WaitUntilNotBusy();
        Assert.Equal("LYPG", searchBox.Text);

        searchBox.Text = string.Empty;
        _fixture.WaitUntilNotBusy();
        _fixture.AssertNoErrorDialogOpen();
    }

    /// <summary>
    /// Verifies the full manual position lifecycle in the UI:
    /// clicking <c>+ Add Position</c> increments the open-lot counter, editing the Company Name &amp; Comment
    /// and clicking <c>Save</c> updates status, and clicking <c>Delete Selected</c> restores the original count.
    /// </summary>
    [Fact]
    public void AddEditSaveAndDeletePosition_RoundTripsThroughUi()
    {
        // Ensure we are on Tab 1 (Positions, index 0)
        _fixture.SelectMainTab(0);

        int initialOpenCount = int.Parse(_fixture.FindById("TxtOpenLotsCount").Name);

        // Act 1: click "+ Add Position"
        _fixture.FindById("BtnAddPosition").AsButton().Invoke();
        _fixture.WaitUntilNotBusy();

        // Assert: open lot count increased by 1
        Retry.WhileFalse(
            () => int.Parse(_fixture.FindById("TxtOpenLotsCount").Name) == initialOpenCount + 1,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150));
        Assert.Equal(initialOpenCount + 1, int.Parse(_fixture.FindById("TxtOpenLotsCount").Name));

        // Act 2: edit Company Name and Comment via UIA ValuePattern, then click Save
        TextBox companyBox = _fixture.FindById("TxtEditCompanyName").AsTextBox();
        companyBox.Text = "FlaUI Test Corp";

        TextBox commentBox = _fixture.FindById("TxtEditComment").AsTextBox();
        commentBox.Text = "Created by FlaUI automation test";

        _fixture.FindById("BtnSaveLotOverrides").AsButton().Invoke();
        _fixture.WaitUntilNotBusy(TimeSpan.FromSeconds(30));
        _fixture.AssertNoErrorDialogOpen();

        // Act 3: delete the newly added lot
        _fixture.FindById("BtnDeletePosition").AsButton().Invoke();
        _fixture.WaitUntilNotBusy();

        // Assert: open lot count returned to initial value
        Retry.WhileFalse(
            () => int.Parse(_fixture.FindById("TxtOpenLotsCount").Name) == initialOpenCount,
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150));
        Assert.Equal(initialOpenCount, int.Parse(_fixture.FindById("TxtOpenLotsCount").Name));
    }

    /// <summary>
    /// Verifies that switching UI language (<c>EN</c> → <c>SK</c> → <c>EN</c>) translates live button captions
    /// and switching Base Currency (<c>EUR</c> → <c>USD</c> → <c>EUR</c>) recalculates without errors.
    /// </summary>
    [Fact]
    public void LanguageAndCurrencySwitch_UpdatesUiLiveAndRestoresDefaults()
    {
        // Capture English caption of the Sync button
        string enSyncCaption = _fixture.FindById("BtnSyncMarket").Name;

        // Act: switch language to Slovak
        _fixture.SelectComboBoxItem("CmbLanguage", "SK");
        string skSyncCaption = _fixture.FindById("BtnSyncMarket").Name;

        // Assert: caption changed to Slovak translation
        Assert.NotEqual(enSyncCaption, skSyncCaption);
        _fixture.AssertNoErrorDialogOpen();

        // Restore English
        _fixture.SelectComboBoxItem("CmbLanguage", "EN");
        Assert.Equal(enSyncCaption, _fixture.FindById("BtnSyncMarket").Name);

        // Act: switch base currency to USD and back to EUR
        _fixture.SelectComboBoxItem("CmbBaseCurrency", "USD");
        _fixture.AssertNoErrorDialogOpen();

        _fixture.SelectComboBoxItem("CmbBaseCurrency", "EUR");
        _fixture.AssertNoErrorDialogOpen();
    }
}

using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.UIA3;

namespace StockMonitor.UiTests.Fixtures;

/// <summary>
/// xUnit collection fixture that launches <c>StockMonitor.exe</c> under FlaUI UIA3 using an isolated
/// temporary data directory (<c>STOCKMONITOR_DATA_DIR</c>) so UI automation never modifies the user's
/// real <c>%LocalAppData%\StockMonitor\portfolio.json</c>.
/// </summary>
public sealed class StockMonitorAppFixture : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly Application _app;

    /// <summary>Gets the isolated temporary folder used for <c>portfolio.json</c> and caches during the UI test run.</summary>
    public string IsolatedDataDir { get; }

    /// <summary>Gets the active FlaUI <see cref="Window"/> handle for <c>MainWindow</c>.</summary>
    public Window MainWindow { get; }

    /// <summary>
    /// Locates the built <c>StockMonitor.exe</c>, starts it in an isolated data directory,
    /// and waits for the main window and initial portfolio load to settle.
    /// </summary>
    public StockMonitorAppFixture()
    {
        IsolatedDataDir = Path.Combine(Path.GetTempPath(), "StockMonitorUiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(IsolatedDataDir);

        string exePath = ResolveStockMonitorExecutablePath();
        ProcessStartInfo psi = new()
        {
            FileName = exePath,
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false
        };
        psi.Environment["STOCKMONITOR_DATA_DIR"] = IsolatedDataDir;

        _automation = new UIA3Automation();
        _app = Application.Launch(psi);

        Window? mainWindow = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(25));
        MainWindow = mainWindow ?? throw new InvalidOperationException("StockMonitor main window did not appear within 25 seconds.");

        // Wait until initial async portfolio seeding & recalculation finishes
        WaitUntilNotBusy(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Finds a descendant UI element by its <c>AutomationProperties.AutomationId</c>, retrying briefly for async layout updates.
    /// </summary>
    public AutomationElement FindById(string automationId, TimeSpan? timeout = null)
    {
        RetryResult<AutomationElement?> result = Retry.WhileNull(
            () => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            timeout ?? TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150));

        return result.Result ?? throw new InvalidOperationException($"UI element with AutomationId '{automationId}' was not found.");
    }

    /// <summary>
    /// Polls the status bar until the indeterminate <c>StatusProgressBar</c> is hidden (i.e. <c>IsBusy == false</c>).
    /// </summary>
    public void WaitUntilNotBusy(TimeSpan? timeout = null)
    {
        Retry.WhileTrue(
            () =>
            {
                AutomationElement? progress = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("StatusProgressBar"));
                return progress != null && !progress.IsOffscreen;
            },
            timeout ?? TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(200));
    }

    /// <summary>
    /// Selects an item by exact text in a WPF <see cref="ComboBox"/> identified by <paramref name="automationId"/>.
    /// </summary>
    public void SelectComboBoxItem(string automationId, string itemText)
    {
        ComboBox combo = FindById(automationId).AsComboBox();
        combo.Expand();
        Retry.WhileNull(
            () => combo.Items.FirstOrDefault(i => string.Equals(i.Text, itemText, StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromMilliseconds(100));

        ComboBoxItem? match = combo.Items.FirstOrDefault(i =>
            string.Equals(i.Text, itemText, StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            combo.Collapse();
            throw new InvalidOperationException($"ComboBox '{automationId}' does not contain item '{itemText}'.");
        }

        match.Select();
        combo.Collapse();
        WaitUntilNotBusy();
    }

    /// <summary>
    /// Selects a main tab in Syncfusion <c>TabControlExt</c> (<c>MainTabControl</c>) by zero-based index
    /// (0 = Positions, 1 = Summary &amp; Overview, 2 = Dividends, 3 = History, 4 = Settings).
    /// </summary>
    public void SelectMainTab(int tabIndex)
    {
        AutomationElement tabControl = FindById("MainTabControl");
        AutomationElement[]? tabItems = null;

        Retry.WhileTrue(
            () =>
            {
                tabItems = tabControl.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.TabItem));
                return tabItems == null || tabItems.Length <= tabIndex;
            },
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMilliseconds(150));

        if (tabItems == null || tabItems.Length <= tabIndex)
        {
            throw new InvalidOperationException($"MainTabControl only exposed {tabItems?.Length ?? 0} TabItems; cannot select index {tabIndex}.");
        }

        // Use pure UIA SelectionItemPattern (does not require Win32 SendInput or hijack the mouse cursor)
        AutomationElement targetTab = tabItems[tabIndex];
        if (targetTab.Patterns.SelectionItem.IsSupported)
        {
            targetTab.Patterns.SelectionItem.Pattern.Select();
        }
        else if (targetTab.Patterns.Invoke.IsSupported)
        {
            targetTab.Patterns.Invoke.Pattern.Invoke();
        }
        else
        {
            targetTab.Click();
        }

        WaitUntilNotBusy();
    }

    /// <summary>
    /// Verifies that no unexpected modal error dialog is currently open on top of the main window.
    /// </summary>
    public void AssertNoErrorDialogOpen()
    {
        Window[] modals = MainWindow.ModalWindows;
        Assert.Empty(modals);
    }

    private static string ResolveStockMonitorExecutablePath()
    {
        // Primary: output directory alongside test binary (copied via ProjectReference)
        string localExe = Path.Combine(AppContext.BaseDirectory, "StockMonitor.exe");
        if (File.Exists(localExe))
        {
            return localExe;
        }

        // Fallback: StockMonitor project's Debug output folder
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "StockMonitor", "bin", "Debug", "net10.0-windows", "StockMonitor.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate StockMonitor.exe for FlaUI testing.", localExe);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (!_app.HasExited)
            {
                _app.Close();
                using Process proc = Process.GetProcessById(_app.ProcessId);
                if (!proc.WaitForExit(3000))
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
        }
        catch
        {
            try
            {
                _app.Kill();
            }
            catch
            {
                // Ignore shutdown races
            }
        }
        finally
        {
            _app.Dispose();
            _automation.Dispose();

            try
            {
                if (Directory.Exists(IsolatedDataDir))
                {
                    Directory.Delete(IsolatedDataDir, recursive: true);
                }
            }
            catch
            {
                // Ignore temp cleanup lock
            }
        }
    }
}

/// <summary>
/// xUnit collection definition ensuring all FlaUI desktop UI tests share one sequential
/// <see cref="StockMonitorAppFixture"/> instance and never contend for desktop focus.
/// </summary>
[CollectionDefinition("FlaUI")]
public sealed class FlaUiCollection : ICollectionFixture<StockMonitorAppFixture>
{
}

using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Charts;
using Syncfusion.UI.Xaml.Grid;

namespace StockMonitor.Behaviors;

/// <summary>
/// Attached MVVM behaviors for Syncfusion Theme, SfDataGrid group expand/collapse, and modern Chromium WebView2 navigation.
/// Keeps MainWindow.xaml.cs free of event handlers and UI logic while eliminating legacy IE Script Error popups.
/// </summary>
public static class MvvmBehaviors
{
    // 1. Syncfusion Theme attached property for Window
    /// <summary>
    /// Identifies the <c>ThemeName</c> attached dependency property for applying Syncfusion skins.
    /// </summary>
    public static readonly DependencyProperty ThemeNameProperty =
        DependencyProperty.RegisterAttached(
            "ThemeName",
            typeof(string),
            typeof(MvvmBehaviors),
            new PropertyMetadata(null, OnThemeNameChanged));

    /// <summary>
    /// Gets the Syncfusion theme name applied to the element.
    /// </summary>
    /// <param name="obj">The target dependency object.</param>
    /// <returns>The theme name.</returns>
    public static string? GetThemeName(DependencyObject obj) => (string?)obj.GetValue(ThemeNameProperty);

    /// <summary>
    /// Sets the Syncfusion theme name applied to the element.
    /// </summary>
    /// <param name="obj">The target dependency object.</param>
    /// <param name="value">The Syncfusion theme name.</param>
    public static void SetThemeName(DependencyObject obj, string? value) => obj.SetValue(ThemeNameProperty, value);

    private static void OnThemeNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element && e.NewValue is string themeName && !string.IsNullOrWhiteSpace(themeName))
        {
            try
            {
                SfSkinManager.SetTheme(element, new Theme(themeName));
            }
            catch
            {
                SfSkinManager.SetTheme(element, new Theme("Windows11Dark"));
            }
        }
    }

    // 2. Modern Chromium WebView2 / Fallback silent WebBrowser bindable URL attached property
    /// <summary>
    /// Identifies the <c>BindableSource</c> attached dependency property for navigating an embedded browser.
    /// </summary>
    public static readonly DependencyProperty BindableSourceProperty =
        DependencyProperty.RegisterAttached(
            "BindableSource",
            typeof(string),
            typeof(MvvmBehaviors),
            new PropertyMetadata(null, OnBindableSourceChanged));

    /// <summary>
    /// Gets the bound URL for the embedded browser host.
    /// </summary>
    /// <param name="obj">The target browser host control.</param>
    /// <returns>The bound URL string.</returns>
    public static string? GetBindableSource(DependencyObject obj) => (string?)obj.GetValue(BindableSourceProperty);

    /// <summary>
    /// Sets the bound URL for the embedded browser host.
    /// </summary>
    /// <param name="obj">The target browser host control.</param>
    /// <param name="value">The URL to navigate to.</param>
    public static void SetBindableSource(DependencyObject obj, string? value) => obj.SetValue(BindableSourceProperty, value);

    private static void OnBindableSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (d is ContentControl host)
        {
            _ = NavigateHostControlAsync(host, url);
        }
        else if (d is WebBrowser legacyBrowser)
        {
            AttachSilentScriptHandler(legacyBrowser);
            TryNavigateLegacyBrowser(legacyBrowser, url);
        }
    }

    private static async Task NavigateHostControlAsync(ContentControl host, string url)
    {
        try
        {
            if (host.Content is not WebView2 webView)
            {
                webView = new WebView2();
                host.Content = webView;

                string userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "StockMonitor",
                    "WebView2Data");
                Directory.CreateDirectory(userDataFolder);

                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: userDataFolder);

                await webView.EnsureCoreWebView2Async(env);
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                }
            }
            else if (webView.CoreWebView2 == null)
            {
                await webView.EnsureCoreWebView2Async();
            }

            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.Navigate(url);
            }
            else
            {
                webView.Source = new Uri(url);
            }
        }
        catch
        {
            // Fallback to silent WebBrowser if WebView2 runtime fails to initialize
            if (host.Content is not WebBrowser fallbackBrowser)
            {
                fallbackBrowser = new WebBrowser();
                AttachSilentScriptHandler(fallbackBrowser);
                host.Content = fallbackBrowser;
            }

            TryNavigateLegacyBrowser(fallbackBrowser, url);
        }
    }

    private static void AttachSilentScriptHandler(WebBrowser browser)
    {
        browser.Navigating -= OnLegacyBrowserNavigating;
        browser.Navigating += OnLegacyBrowserNavigating;
        browser.Navigated -= OnLegacyBrowserNavigated;
        browser.Navigated += OnLegacyBrowserNavigated;
        SetLegacyBrowserSilent(browser);
    }

    private static void OnLegacyBrowserNavigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        if (sender is WebBrowser browser)
        {
            SetLegacyBrowserSilent(browser);
        }
    }

    private static void OnLegacyBrowserNavigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
    {
        if (sender is WebBrowser browser)
        {
            SetLegacyBrowserSilent(browser);
        }
    }

    private static void SetLegacyBrowserSilent(WebBrowser browser)
    {
        try
        {
            FieldInfo? axField = typeof(WebBrowser).GetField(
                "_axIWebBrowser2",
                BindingFlags.Instance | BindingFlags.NonPublic);
            object? comBrowser = axField?.GetValue(browser);
            comBrowser?.GetType().InvokeMember(
                "Silent",
                BindingFlags.SetProperty,
                null,
                comBrowser,
                [true]);
        }
        catch
        {
            // Ignore reflection errors
        }
    }

    private static void TryNavigateLegacyBrowser(WebBrowser browser, string url)
    {
        try
        {
            SetLegacyBrowserSilent(browser);
            browser.Navigate(new Uri(url));
        }
        catch
        {
            // Ignore malformed or blocked navigation errors
        }
    }

    // 3. SfDataGrid Group Expand/Collapse request attached property (+N = expand all, -N = collapse all)
    /// <summary>
    /// Identifies the <c>GroupExpandRequest</c> attached dependency property for expanding/collapsing <see cref="SfDataGrid"/> groups.
    /// </summary>
    public static readonly DependencyProperty GroupExpandRequestProperty =
        DependencyProperty.RegisterAttached(
            "GroupExpandRequest",
            typeof(int),
            typeof(MvvmBehaviors),
            new PropertyMetadata(0, OnGroupExpandRequestChanged));

    /// <summary>
    /// Gets the group expand/collapse request counter.
    /// </summary>
    /// <param name="obj">The target <see cref="SfDataGrid"/>.</param>
    /// <returns>Positive values expand all groups; negative values collapse all groups.</returns>
    public static int GetGroupExpandRequest(DependencyObject obj) => (int)obj.GetValue(GroupExpandRequestProperty);

    /// <summary>
    /// Sets the group expand/collapse request counter.
    /// </summary>
    /// <param name="obj">The target <see cref="SfDataGrid"/>.</param>
    /// <param name="value">Positive to expand all groups; negative to collapse all groups.</param>
    public static void SetGroupExpandRequest(DependencyObject obj, int value) => obj.SetValue(GroupExpandRequestProperty, value);

    private static void OnGroupExpandRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SfDataGrid grid && e.NewValue is int req && req != 0)
        {
            if (req > 0)
            {
                grid.ExpandAllGroup();
            }
            else
            {
                grid.CollapseAllGroup();
            }
        }
    }

    // 4. SfDataGrid ClearFilters request attached property (incrementing positive value clears all column filters)
    /// <summary>
    /// Identifies the <c>ClearFiltersRequest</c> attached dependency property for clearing all column filters on an <see cref="SfDataGrid"/>.
    /// </summary>
    public static readonly DependencyProperty ClearFiltersRequestProperty =
        DependencyProperty.RegisterAttached(
            "ClearFiltersRequest",
            typeof(int),
            typeof(MvvmBehaviors),
            new PropertyMetadata(0, OnClearFiltersRequestChanged));

    /// <summary>
    /// Gets the clear-filters request counter.
    /// </summary>
    /// <param name="obj">The target <see cref="SfDataGrid"/>.</param>
    /// <returns>The request counter value.</returns>
    public static int GetClearFiltersRequest(DependencyObject obj) => (int)obj.GetValue(ClearFiltersRequestProperty);

    /// <summary>
    /// Sets the clear-filters request counter.
    /// </summary>
    /// <param name="obj">The target <see cref="SfDataGrid"/>.</param>
    /// <param name="value">Incrementing positive counter value to trigger <see cref="SfDataGrid.ClearFilters"/>.</param>
    public static void SetClearFiltersRequest(DependencyObject obj, int value) => obj.SetValue(ClearFiltersRequestProperty, value);

    private static void OnClearFiltersRequestChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SfDataGrid grid && e.NewValue is int req && req > 0)
        {
            try
            {
                grid.ClearFilters();
            }
            catch
            {
                // Ignore if grid view is not yet initialized (e.g., tab not yet visited)
            }
        }
    }

    // 5. SfChart NumericalAxis space thousand-separator label formatter
    /// <summary>
    /// Identifies the <c>UseSpaceNumberAxisLabels</c> attached dependency property for formatting
    /// <see cref="NumericalAxis"/> labels with a space as the thousands separator.
    /// </summary>
    public static readonly DependencyProperty UseSpaceNumberAxisLabelsProperty =
        DependencyProperty.RegisterAttached(
            "UseSpaceNumberAxisLabels",
            typeof(bool),
            typeof(MvvmBehaviors),
            new PropertyMetadata(false, OnUseSpaceNumberAxisLabelsChanged));

    /// <summary>Gets whether space thousand-separator formatting is enabled on the axis.</summary>
    public static bool GetUseSpaceNumberAxisLabels(DependencyObject obj) => (bool)obj.GetValue(UseSpaceNumberAxisLabelsProperty);

    /// <summary>Sets whether space thousand-separator formatting is enabled on the axis.</summary>
    public static void SetUseSpaceNumberAxisLabels(DependencyObject obj, bool value) => obj.SetValue(UseSpaceNumberAxisLabelsProperty, value);

    private static void OnUseSpaceNumberAxisLabelsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ChartAxisBase2D axis)
        {
            return;
        }

        axis.LabelCreated -= OnNumericalAxisLabelCreated;
        if (e.NewValue is true)
        {
            axis.LabelCreated += OnNumericalAxisLabelCreated;
        }
    }

    private static void OnNumericalAxisLabelCreated(object? sender, LabelCreatedEventArgs e)
    {
        if (e.AxisLabel == null)
        {
            return;
        }

        double val = e.AxisLabel.Position;
        if (double.IsNaN(val) || double.IsInfinity(val))
        {
            return;
        }

        string format = Math.Abs(val - Math.Round(val)) < 1e-6 ? "#,##0" : "#,##0.##";
        e.AxisLabel.LabelContent = SpaceNumberConverter.FormatNumber(val, format);
    }
}

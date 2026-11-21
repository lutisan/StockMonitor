using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using StockMonitor.Services;
using StockMonitor.ViewModels;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;

namespace StockMonitor;

/// <summary>
/// Application entry point configuring Syncfusion licensing, theming, and dependency injection.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Gets the root dependency injection service provider for the application.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>
    /// Configures Syncfusion licensing, theming, and dependency injection, then displays <see cref="MainWindow"/>.
    /// </summary>
    /// <param name="e">Startup event arguments.</param>
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        string? licenseKey = ResolveSyncfusionLicenseKey();
        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
        }

        SfSkinManager.ApplyThemeAsDefaultStyle = true;

        ServiceCollection services = new();
        services.AddSingleton<IFileDialogService, WpfFileDialogService>();
        services.AddSingleton<AppPreferencesService>();
        services.AddSingleton<FrankfurterFxRateProvider>();
        services.AddSingleton<YahooFinanceMarketDataProvider>();
        services.AddSingleton<XtbWorkbookImportService>();
        services.AddSingleton<PortfolioStorageService>();
        services.AddSingleton<PortfolioCalculationService>();
        ExportPluginLoader.RegisterExportPlugins(services);
        services.AddSingleton<MainViewModel>();
        services.AddTransient<MainWindow>();

        Services = services.BuildServiceProvider();

        base.OnStartup(e);

        MainWindow window = Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string logPath = LogCrash("DispatcherUnhandledException", e.Exception);
        ShowErrorWithLogDialog(e.Exception.Message, logPath);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash("AppDomainUnhandledException", ex);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void ShowErrorWithLogDialog(string errorMessage, string logPath)
    {
        LocalizationService loc = LocalizationService.Instance;
        Window dialog = new()
        {
            Title = loc.Get("Err_DialogTitle"),
            Width = 540,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Owner = Current?.MainWindow is { IsVisible: true } owner ? owner : null
        };

        StackPanel root = new() { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = errorMessage,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        root.Children.Add(new TextBlock
        {
            Text = $"{loc.Get("Err_LogSavedTo")} {logPath}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
            Margin = new Thickness(0, 0, 0, 14)
        });

        StackPanel buttons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        Button openLogBtn = new()
        {
            Content = loc.Get("Btn_OpenLogInExplorer"),
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 8, 0)
        };
        openLogBtn.Click += (_, _) =>
        {
            try
            {
                if (File.Exists(logPath))
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{logPath}\"") { UseShellExecute = true });
                }
                else
                {
                    string? dir = Path.GetDirectoryName(logPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
                    }
                }
            }
            catch
            {
                // Ignore explorer launch errors
            }
        };

        Button okBtn = new()
        {
            Content = loc.Get("Btn_Ok"),
            Width = 85,
            Padding = new Thickness(12, 5, 12, 5),
            IsDefault = true,
            IsCancel = true
        };
        okBtn.Click += (_, _) => dialog.Close();

        buttons.Children.Add(openLogBtn);
        buttons.Children.Add(okBtn);
        root.Children.Add(buttons);
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private static string LogCrash(string source, Exception ex)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "StockMonitor");
        string path = Path.Combine(dir, "crash.log");

        try
        {
            Directory.CreateDirectory(dir);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n\n");
        }
        catch
        {
            // Ignore logging errors
        }

        return path;
    }

    private static string? ResolveSyncfusionLicenseKey()
    {
        string? envKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            return envKey.Trim();
        }

        string? embeddedKey = typeof(App).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "SyncfusionLicenseKey", StringComparison.Ordinal))
            ?.Value;

        return !string.IsNullOrWhiteSpace(embeddedKey) ? embeddedKey.Trim() : null;
    }
}

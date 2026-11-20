using System.IO;
using System.Text.Json;
using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Persists the PortfolioDocument to local JSON storage and seeds initial data from the XTB report asynchronously.
/// </summary>
public sealed class PortfolioStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly XtbWorkbookImportService _xtbImportService;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PortfolioStorageService"/> class.
    /// </summary>
    /// <param name="xtbImportService">Importer used to seed initial data from <c>xtb_seed.csv</c>.</param>
    public PortfolioStorageService(XtbWorkbookImportService xtbImportService)
        : this(xtbImportService, Path.Combine(ResolveDefaultDataDirectory(), "portfolio.json"))
    {
    }

    internal static string ResolveDefaultDataDirectory()
    {
        string? envOverride = Environment.GetEnvironmentVariable("STOCKMONITOR_DATA_DIR");
        return !string.IsNullOrWhiteSpace(envOverride)
            ? envOverride.Trim()
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockMonitor");
    }

    internal PortfolioStorageService(XtbWorkbookImportService xtbImportService, string storageFilePath)
    {
        _xtbImportService = xtbImportService;
        string? dir = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        StorageFilePath = storageFilePath;
    }

    /// <summary>
    /// Gets the full path to the local JSON persistence file (<c>portfolio.json</c>).
    /// </summary>
    public string StorageFilePath { get; }

    /// <summary>
    /// Asynchronously loads the portfolio document from disk, or seeds it from the bundled XTB report on first run.
    /// </summary>
    public Task<PortfolioDocument> LoadOrSeedAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            if (File.Exists(StorageFilePath))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(StorageFilePath, cancellationToken).ConfigureAwait(false);
                    PortfolioDocument? loaded = JsonSerializer.Deserialize<PortfolioDocument>(json, JsonOptions);
                    if (loaded is { Lots.Count: > 0 })
                    {
                        if (loaded.TaxRules.Count == 0)
                        {
                            loaded.TaxRules.AddRange(TickerMappingHelper.CreateDefaultTaxRules());
                        }

                        bool hadMappings = loaded.TickerMappings.Count > 0;
                        TickerMappingHelper.SynchronizeDocumentMappings(loaded);
                        if (!hadMappings)
                        {
                            await SaveAsync(loaded, cancellationToken).ConfigureAwait(false);
                        }

                        return loaded;
                    }
                }
                catch
                {
                    // Fall back to fresh seed if JSON is corrupted
                }
            }

            return await CreateSeededDocumentAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously rebuilds a fresh portfolio document from the bundled XTB seed file.
    /// </summary>
    public Task<PortfolioDocument> CreateSeededDocumentAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            PortfolioDocument doc = new()
            {
                TaxRules = TickerMappingHelper.CreateDefaultTaxRules()
            };

            string seedPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "xtb_seed.csv");
            if (!File.Exists(seedPath))
            {
                string repoSeedPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "xtb_seed.csv");
                if (File.Exists(repoSeedPath))
                {
                    seedPath = repoSeedPath;
                }
            }

            if (File.Exists(seedPath))
            {
                _xtbImportService.ImportFile(seedPath, doc, replaceExistingXtbData: true);
            }

            await SaveAsync(doc, cancellationToken).ConfigureAwait(false);
            return doc;
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves the portfolio document atomically to disk on a background thread.
    /// </summary>
    public async Task SaveAsync(PortfolioDocument document, CancellationToken cancellationToken = default)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                string tempPath = StorageFilePath + ".tmp";
                string json = JsonSerializer.Serialize(document, JsonOptions);
                await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, StorageFilePath, overwrite: true);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }
}

using System.IO;
using System.Text.Json;
using StockMonitor.Models;

namespace StockMonitor.Services;

/// <summary>
/// Persists <see cref="PortfolioDocument"/> instances to JSON files (plain or AES-256-GCM encrypted)
/// and supports creating empty or XTB-seeded documents.
/// </summary>
public sealed class PortfolioStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly IImportService? _seedImportService;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PortfolioStorageService"/> class.
    /// </summary>
    /// <param name="importServices">Discovered broker import providers used to seed initial data from <c>xtb_seed.csv</c>.</param>
    public PortfolioStorageService(IEnumerable<IImportService> importServices)
        : this(
            importServices.FirstOrDefault(s => s.Id.Equals("xtb", StringComparison.OrdinalIgnoreCase))
                ?? importServices.FirstOrDefault(),
            Path.Combine(ResolveDefaultDataDirectory(), "portfolio.json"))
    {
    }

    internal static string ResolveDefaultDataDirectory()
    {
        string? envOverride = Environment.GetEnvironmentVariable("STOCKMONITOR_DATA_DIR");
        return !string.IsNullOrWhiteSpace(envOverride)
            ? envOverride.Trim()
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StockMonitor");
    }

    internal PortfolioStorageService(IImportService? seedImportService, string storageFilePath)
    {
        _seedImportService = seedImportService;
        string? dir = Path.GetDirectoryName(storageFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        StorageFilePath = storageFilePath;
        CurrentFilePath = storageFilePath;
    }

    /// <summary>
    /// Gets the default local JSON persistence file path (<c>portfolio.json</c>).
    /// </summary>
    public string StorageFilePath { get; }

    /// <summary>
    /// Gets or sets the path of the currently active portfolio document, or <see langword="null"/> for a new unsaved document.
    /// </summary>
    public string? CurrentFilePath { get; set; }

    /// <summary>
    /// Creates a brand-new empty portfolio document with default country tax rules, ready for XTB import or manual entry.
    /// </summary>
    public PortfolioDocument CreateEmptyDocument()
    {
        return new PortfolioDocument
        {
            AccountNumber = string.Empty,
            ReportGeneratedUtc = DateTime.UtcNow,
            TaxRules = TickerMappingHelper.CreateDefaultTaxRules(),
            TickerMappings = [],
            Lots = [],
            Dividends = [],
            CashFlows = []
        };
    }

    /// <summary>
    /// Checks asynchronously whether the specified JSON file is encrypted with a password.
    /// </summary>
    public Task<bool> IsFileEncryptedAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return false;
            }

            string content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            return DocumentCryptoService.IsEncryptedJson(content);
        }, cancellationToken);
    }

    /// <summary>
    /// Loads a <see cref="PortfolioDocument"/> from an explicit file path, decrypting it first if it is encrypted.
    /// Throws <see cref="EncryptedDocumentPasswordRequiredException"/> or <see cref="InvalidDocumentPasswordException"/>
    /// if the file is encrypted and the password is missing or incorrect.
    /// </summary>
    public async Task<PortfolioDocument> LoadFromFileAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        (PortfolioDocument doc, DocumentEncryptionKey? key) =
            await LoadFromFileWithKeyAsync(filePath, password, cancellationToken).ConfigureAwait(false);
        key?.Dispose();
        return doc;
    }

    /// <summary>
    /// Loads a <see cref="PortfolioDocument"/> from an explicit file path and, if the file is encrypted,
    /// returns the derived <see cref="DocumentEncryptionKey"/> so the caller can cache only the key bytes
    /// and discard the plaintext password immediately.
    /// </summary>
    public Task<(PortfolioDocument Document, DocumentEncryptionKey? EncryptionKey)> LoadFromFileWithKeyAsync(
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("File path must not be empty.", nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Portfolio file not found: {filePath}", filePath);
            }

            string content = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            string json;
            DocumentEncryptionKey? derivedKey = null;

            if (DocumentCryptoService.IsEncryptedJson(content))
            {
                (json, derivedKey) = DocumentCryptoService.DecryptWithKey(content, password ?? string.Empty);
            }
            else
            {
                json = content;
            }

            PortfolioDocument? loaded;
            try
            {
                loaded = JsonSerializer.Deserialize<PortfolioDocument>(json, JsonOptions);
            }
            catch
            {
                derivedKey?.Dispose();
                throw;
            }

            if (loaded == null)
            {
                derivedKey?.Dispose();
                throw new InvalidDataException($"File '{filePath}' does not contain a valid StockMonitor portfolio document.");
            }

            HydrateDocumentDefaults(loaded);
            CurrentFilePath = filePath;
            return (loaded, derivedKey);
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously loads the portfolio document from the default <see cref="StorageFilePath"/>,
    /// or seeds it from the bundled XTB report on first run.
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
                    if (!DocumentCryptoService.IsEncryptedJson(json))
                    {
                        PortfolioDocument? loaded = JsonSerializer.Deserialize<PortfolioDocument>(json, JsonOptions);
                        if (loaded is { Lots.Count: > 0 })
                        {
                            bool hadMappings = loaded.TickerMappings.Count > 0;
                            HydrateDocumentDefaults(loaded);
                            CurrentFilePath = StorageFilePath;
                            if (!hadMappings)
                            {
                                await SaveToFileAsync(loaded, StorageFilePath, password: null, cancellationToken).ConfigureAwait(false);
                            }

                            return loaded;
                        }
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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="saveToDisk">Whether to immediately persist the seeded document to disk.</param>
    public Task<PortfolioDocument> CreateSeededDocumentAsync(
        CancellationToken cancellationToken = default,
        bool saveToDisk = true)
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

            if (File.Exists(seedPath) && _seedImportService != null)
            {
                _seedImportService.ImportFile(seedPath, doc, replaceExistingBrokerData: true);
            }

            if (saveToDisk)
            {
                string targetPath = CurrentFilePath ?? StorageFilePath;
                await SaveToFileAsync(doc, targetPath, password: null, cancellationToken).ConfigureAwait(false);
                CurrentFilePath = targetPath;
            }

            return doc;
        }, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves the portfolio document atomically to the active file path (or default <see cref="StorageFilePath"/>).
    /// </summary>
    public Task SaveAsync(PortfolioDocument document, CancellationToken cancellationToken = default)
    {
        string targetPath = CurrentFilePath ?? StorageFilePath;
        return SaveToFileAsync(document, targetPath, password: null, cancellationToken);
    }

    /// <summary>
    /// Asynchronously saves the portfolio document atomically to <paramref name="filePath"/>.
    /// When <paramref name="password"/> is non-empty, the JSON payload is encrypted using AES-256-GCM before writing.
    /// </summary>
    public async Task SaveToFileAsync(
        PortfolioDocument document,
        string filePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        using DocumentEncryptionKey? key = !string.IsNullOrEmpty(password)
            ? DocumentEncryptionKey.DeriveFromPassword(password)
            : null;
        await SaveToFileAsync(document, filePath, key, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously saves the portfolio document atomically to <paramref name="filePath"/>.
    /// When <paramref name="encryptionKey"/> is non-null, the JSON payload is encrypted using AES-256-GCM
    /// with a fresh random nonce before writing.
    /// </summary>
    public async Task SaveToFileAsync(
        PortfolioDocument document,
        string filePath,
        DocumentEncryptionKey? encryptionKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Target file path must not be empty.", nameof(filePath));
        }

        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                string? dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string tempPath = filePath + ".tmp";
                string json = JsonSerializer.Serialize(document, JsonOptions);
                string payload = encryptionKey != null
                    ? DocumentCryptoService.Encrypt(json, encryptionKey)
                    : json;

                await File.WriteAllTextAsync(tempPath, payload, cancellationToken).ConfigureAwait(false);
                File.Move(tempPath, filePath, overwrite: true);
                CurrentFilePath = filePath;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private static void HydrateDocumentDefaults(PortfolioDocument doc)
    {
        doc.Settings ??= new AppSettings();
        doc.TaxRules ??= [];
        doc.TickerMappings ??= [];
        doc.Lots ??= [];
        doc.Dividends ??= [];
        doc.CashFlows ??= [];

        if (doc.TaxRules.Count == 0)
        {
            doc.TaxRules.AddRange(TickerMappingHelper.CreateDefaultTaxRules());
        }

        TickerMappingHelper.SynchronizeDocumentMappings(doc);
    }
}

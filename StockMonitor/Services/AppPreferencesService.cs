using System.IO;
using System.Text.Json;

namespace StockMonitor.Services;

/// <summary>
/// Persists lightweight user shell preferences (last opened/saved document path and directory).
/// Passwords are never stored here.
/// </summary>
public sealed class AppPreferences
{
    /// <summary>Gets or sets the full path of the most recently opened or saved portfolio JSON file.</summary>
    public string? LastFilePath { get; set; }

    /// <summary>Gets or sets the most recently used folder for opening or saving portfolio JSON files.</summary>
    public string? LastDirectory { get; set; }
}

/// <summary>
/// Reads and writes <see cref="AppPreferences"/> in the local StockMonitor data directory.
/// </summary>
public sealed class AppPreferencesService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _preferencesFilePath;
    private readonly string _defaultInitialDirectory;
    private readonly object _lock = new();
    private AppPreferences _current;

    /// <summary>
    /// Initializes a new instance of <see cref="AppPreferencesService"/> using the default app data folder
    /// and the user's Documents folder as the initial file dialog directory.
    /// </summary>
    public AppPreferencesService()
        : this(
            Path.Combine(PortfolioStorageService.ResolveDefaultDataDirectory(), "app_preferences.json"),
            ResolveDefaultDocumentsDirectory())
    {
    }

    internal AppPreferencesService(string preferencesFilePath, string defaultInitialDirectory)
    {
        _preferencesFilePath = preferencesFilePath;
        _defaultInitialDirectory = defaultInitialDirectory;
        _current = LoadInternal();
    }

    /// <summary>
    /// Resolves the initial default directory (<c>&lt;user&gt;\Documents</c>).
    /// </summary>
    public static string ResolveDefaultDocumentsDirectory()
    {
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(docs))
        {
            return docs;
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    /// <summary>
    /// Gets the last opened or saved file path, or <see langword="null"/> if none has been recorded.
    /// </summary>
    public string? GetLastFilePath()
    {
        lock (_lock)
        {
            return string.IsNullOrWhiteSpace(_current.LastFilePath) ? null : _current.LastFilePath;
        }
    }

    /// <summary>
    /// Gets the predefined directory for Open/Save dialogs: the last used directory if it exists,
    /// otherwise the user's Documents folder.
    /// </summary>
    public string GetInitialDirectory()
    {
        lock (_lock)
        {
            if (!string.IsNullOrWhiteSpace(_current.LastDirectory) && Directory.Exists(_current.LastDirectory))
            {
                return _current.LastDirectory;
            }

            if (!string.IsNullOrWhiteSpace(_current.LastFilePath))
            {
                string? fileDir = Path.GetDirectoryName(_current.LastFilePath);
                if (!string.IsNullOrWhiteSpace(fileDir) && Directory.Exists(fileDir))
                {
                    return fileDir;
                }
            }

            return _defaultInitialDirectory;
        }
    }

    /// <summary>
    /// Records the last opened or saved file path and its containing directory to persistent preferences.
    /// </summary>
    public void RecordFileAccess(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        string fullPath = Path.GetFullPath(filePath);
        string? directory = Path.GetDirectoryName(fullPath);

        lock (_lock)
        {
            _current.LastFilePath = fullPath;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                _current.LastDirectory = directory;
            }

            SaveInternal(_current);
        }
    }

    private AppPreferences LoadInternal()
    {
        try
        {
            if (File.Exists(_preferencesFilePath))
            {
                string json = File.ReadAllText(_preferencesFilePath);
                AppPreferences? loaded = JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch
        {
            // Ignore corrupted preferences and use defaults
        }

        return new AppPreferences();
    }

    private void SaveInternal(AppPreferences preferences)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempPath = _preferencesFilePath + ".tmp";
            string json = JsonSerializer.Serialize(preferences, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _preferencesFilePath, overwrite: true);
        }
        catch
        {
            // Non-fatal: failing to write preferences should not interrupt document save/open
        }
    }
}

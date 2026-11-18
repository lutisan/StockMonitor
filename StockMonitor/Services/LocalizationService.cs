using System.ComponentModel;
using System.Globalization;
using System.Resources;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockMonitor.Services;

/// <summary>
/// Provides reactive multilingual localization (English and Slovak) backed by standard .resx resource files
/// (<c>Resources/Strings.resx</c> and <c>Resources/Strings.sk.resx</c>).
/// </summary>
public sealed class LocalizationService : ObservableObject
{
    private static readonly ResourceManager ResourceMgr =
        new("StockMonitor.Resources.Strings", typeof(LocalizationService).Assembly);

    private string _currentLanguage = "EN";
    private CultureInfo _currentCulture = CultureInfo.GetCultureInfo("en");

    /// <summary>
    /// Gets the singleton instance used for XAML indexer bindings.
    /// </summary>
    public static LocalizationService Instance { get; } = new();

    /// <summary>
    /// Gets the list of supported UI language codes ("EN" = English, "SK" = Slovak).
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } = ["EN", "SK"];

    /// <summary>
    /// Gets or sets the active two-letter language code ("EN" or "SK").
    /// Raises property change notifications for all indexer bindings when changed so the UI updates live.
    /// </summary>
    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "EN" : value.Trim().ToUpperInvariant();
            if (normalized != "SK")
            {
                normalized = "EN";
            }

            if (SetProperty(ref _currentLanguage, normalized))
            {
                _currentCulture = normalized == "SK"
                    ? CultureInfo.GetCultureInfo("sk")
                    : CultureInfo.GetCultureInfo("en");
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
            }
        }
    }

    /// <summary>
    /// Gets the localized string for the specified resource key in the active language.
    /// </summary>
    /// <param name="key">The localization resource key defined in <c>Strings.resx</c>.</param>
    /// <returns>The localized text in English or Slovak.</returns>
    public string this[string key] => Get(key);

    /// <summary>
    /// Resolves a localization key from the embedded <c>.resx</c> resource manager using the active culture.
    /// </summary>
    /// <param name="key">The localization key to resolve.</param>
    /// <returns>The translated string, or the key itself if not found.</returns>
    public string Get(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return string.Empty;
        }

        return ResourceMgr.GetString(key, _currentCulture) ?? key;
    }
}

using StockMonitor.Services;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="LocalizationService"/> verifying English and Slovak <c>.resx</c> lookups,
/// live property-change notification on language switch, and graceful fallbacks.
/// </summary>
public sealed class LocalizationServiceTests
{
    /// <summary>
    /// Verifies that switching <see cref="LocalizationService.CurrentLanguage"/> between EN and SK
    /// resolves distinct translated strings from <c>Strings.resx</c> and <c>Strings.sk.resx</c>
    /// and raises <c>Item[]</c> property change notifications for XAML bindings.
    /// </summary>
    [Fact]
    public void CurrentLanguage_SwitchBetweenEnAndSk_ResolvesTranslationsAndRaisesIndexerNotification()
    {
        // Arrange
        LocalizationService service = new();
        List<string?> changedProps = [];
        service.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName);

        string enTitle = service["Btn_SaveLotOverrides"];
        Assert.False(string.IsNullOrWhiteSpace(enTitle));
        Assert.NotEqual("Btn_SaveLotOverrides", enTitle);

        // Act: switch to Slovak
        service.CurrentLanguage = "SK";
        string skTitle = service["Btn_SaveLotOverrides"];

        // Assert: Slovak string is loaded and Item[] notification fired
        Assert.Equal("SK", service.CurrentLanguage);
        Assert.False(string.IsNullOrWhiteSpace(skTitle));
        Assert.NotEqual("Btn_SaveLotOverrides", skTitle);
        Assert.NotEqual(enTitle, skTitle);
        Assert.Contains("Item[]", changedProps);
    }

    /// <summary>
    /// Verifies that unsupported language codes fall back to English (<c>EN</c>),
    /// and unknown resource keys return the key name rather than throwing.
    /// </summary>
    [Fact]
    public void InvalidLanguageAndMissingKey_FallbackGracefully()
    {
        LocalizationService service = new() { CurrentLanguage = "SK" };

        // Act: assign unsupported language code
        service.CurrentLanguage = "DE";

        // Assert: normalized to EN
        Assert.Equal("EN", service.CurrentLanguage);
        Assert.Equal("NonExistent_Resource_Key", service.Get("NonExistent_Resource_Key"));
        Assert.Equal(string.Empty, service.Get("   "));
    }
}

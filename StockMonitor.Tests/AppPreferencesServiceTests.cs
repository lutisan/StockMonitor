using System.IO;
using StockMonitor.Services;
using StockMonitor.Tests.Helpers;

namespace StockMonitor.Tests;

/// <summary>
/// Unit tests for <see cref="AppPreferencesService"/> verifying initial Documents directory fallback,
/// last-file persistence, and last-directory memory across launches.
/// </summary>
public sealed class AppPreferencesServiceTests
{
    /// <summary>Verifies that the initial directory defaults to the configured Documents folder when no preferences exist.</summary>
    [Fact]
    public void InitialDirectory_DefaultsToConfiguredDocumentsFolder_WhenNoPreferencesExist()
    {
        using TempTestDirectory tempDir = new();
        string prefsPath = tempDir.FilePath("app_preferences.json");
        string fakeDocsDir = tempDir.FilePath("Documents");
        Directory.CreateDirectory(fakeDocsDir);

        AppPreferencesService service = new(prefsPath, fakeDocsDir);

        Assert.Null(service.GetLastFilePath());
        Assert.Equal(fakeDocsDir, service.GetInitialDirectory());
    }

    /// <summary>Verifies that <see cref="AppPreferencesService.RecordFileAccess"/> persists the last file path and folder across restarts.</summary>
    [Fact]
    public void RecordFileAccess_PersistsLastFilePathAndDirectoryAcrossInstances()
    {
        using TempTestDirectory tempDir = new();
        string prefsPath = tempDir.FilePath("app_preferences.json");
        string fakeDocsDir = tempDir.FilePath("Documents");
        string customDir = tempDir.FilePath("MyPortfolios");
        Directory.CreateDirectory(fakeDocsDir);
        Directory.CreateDirectory(customDir);

        string portfolioPath = Path.Combine(customDir, "retirement.json");

        AppPreferencesService writer = new(prefsPath, fakeDocsDir);
        writer.RecordFileAccess(portfolioPath);

        // Reload in a brand-new instance simulating application restart
        AppPreferencesService reader = new(prefsPath, fakeDocsDir);
        Assert.Equal(portfolioPath, reader.GetLastFilePath());
        Assert.Equal(customDir, reader.GetInitialDirectory());
    }

    /// <summary>Verifies that <see cref="AppPreferencesService.GetInitialDirectory"/> falls back to Documents if the recorded folder was deleted.</summary>
    [Fact]
    public void GetInitialDirectory_FallsBackToDocuments_WhenRecordedDirectoryWasDeleted()
    {
        using TempTestDirectory tempDir = new();
        string prefsPath = tempDir.FilePath("app_preferences.json");
        string fakeDocsDir = tempDir.FilePath("Documents");
        string deletedDir = tempDir.FilePath("TemporaryFolder");
        Directory.CreateDirectory(fakeDocsDir);
        Directory.CreateDirectory(deletedDir);

        AppPreferencesService writer = new(prefsPath, fakeDocsDir);
        writer.RecordFileAccess(Path.Combine(deletedDir, "old.json"));
        Directory.Delete(deletedDir, recursive: true);

        AppPreferencesService reader = new(prefsPath, fakeDocsDir);
        Assert.Equal(fakeDocsDir, reader.GetInitialDirectory());
    }
}

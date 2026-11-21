using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;

namespace StockMonitor.Services;

/// <summary>
/// Discovers and registers <see cref="IExportService"/> plugin implementations from <c>*.export.dll</c>
/// assemblies using isolated <see cref="AssemblyLoadContext"/> instances backed by <see cref="AssemblyDependencyResolver"/>.
/// </summary>
public static class ExportPluginLoader
{
    /// <summary>
    /// File pattern required for export plugin assemblies to keep startup scanning fast.
    /// </summary>
    public const string PluginSearchPattern = "*.export.dll";

    /// <summary>
    /// Default subfolder under <see cref="AppContext.BaseDirectory"/> searched for export plugin assemblies.
    /// </summary>
    public const string DefaultPluginsFolderName = "Exports";

    /// <summary>
    /// Scans the default plugin directories (<see cref="AppContext.BaseDirectory"/> and its <c>Exports</c> subfolder)
    /// or the explicitly supplied directories and registers every discovered <see cref="IExportService"/> implementation into <paramref name="services"/>.
    /// </summary>
    /// <param name="services">Target dependency injection service collection.</param>
    /// <param name="searchDirectories">Optional directories to scan instead of the defaults.</param>
    /// <returns>The number of <see cref="IExportService"/> implementations registered.</returns>
    public static int RegisterExportPlugins(IServiceCollection services, params string[] searchDirectories)
    {
        ArgumentNullException.ThrowIfNull(services);

        string[] directories = searchDirectories.Length > 0
            ? searchDirectories
            : [AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, DefaultPluginsFolderName)];

        IReadOnlyList<Type> discoveredTypes = DiscoverExportServiceTypes(directories);
        foreach (Type pluginType in discoveredTypes)
        {
            services.AddSingleton(
                typeof(IExportService),
                sp => ActivatorUtilities.CreateInstance(sp, pluginType));
        }

        return discoveredTypes.Count;
    }

    /// <summary>
    /// Discovers concrete public <see cref="IExportService"/> types from <c>*.export.dll</c> assemblies in the given directories.
    /// </summary>
    /// <param name="searchDirectories">Directories to scan (non-existent directories are skipped).</param>
    /// <returns>Ordered list of discovered concrete <see cref="IExportService"/> types.</returns>
    public static IReadOnlyList<Type> DiscoverExportServiceTypes(IEnumerable<string> searchDirectories)
    {
        ArgumentNullException.ThrowIfNull(searchDirectories);

        List<Type> result = [];
        HashSet<string> visitedFiles = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> registeredTypeKeys = new(StringComparer.Ordinal);

        foreach (string dir in searchDirectories)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                continue;
            }

            IEnumerable<string> pluginFiles;
            try
            {
                pluginFiles = Directory.EnumerateFiles(dir, PluginSearchPattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ExportPluginLoader] Failed to enumerate '{dir}': {ex.Message}");
                continue;
            }

            foreach (string filePath in pluginFiles)
            {
                string fullPath = Path.GetFullPath(filePath);
                if (!visitedFiles.Add(fullPath))
                {
                    continue;
                }

                LoadExportTypesFromAssembly(fullPath, result, registeredTypeKeys);
            }
        }

        return result;
    }

    private static void LoadExportTypesFromAssembly(
        string assemblyPath,
        List<Type> target,
        HashSet<string> registeredTypeKeys)
    {
        try
        {
            ExportPluginLoadContext loadContext = new(assemblyPath);
            Assembly pluginAssembly = loadContext.LoadFromAssemblyPath(assemblyPath);

            Type[] exportedTypes;
            try
            {
                exportedTypes = pluginAssembly.GetExportedTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                exportedTypes = rtle.Types.Where(t => t != null).Cast<Type>().ToArray();
            }

            foreach (Type type in exportedTypes.OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                if (!type.IsClass || type.IsAbstract || !typeof(IExportService).IsAssignableFrom(type))
                {
                    continue;
                }

                string key = type.FullName ?? type.Name;
                if (registeredTypeKeys.Add(key))
                {
                    target.Add(type);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[ExportPluginLoader] Failed to load export plugin '{assemblyPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Isolated load context for an export plugin assembly that shares host contract and Syncfusion assemblies
    /// with <see cref="AssemblyLoadContext.Default"/> while resolving plugin-private dependencies via <see cref="AssemblyDependencyResolver"/>.
    /// </summary>
    private sealed class ExportPluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public ExportPluginLoadContext(string pluginPath)
            : base(name: Path.GetFileNameWithoutExtension(pluginPath), isCollectible: false)
        {
            _resolver = new AssemblyDependencyResolver(pluginPath);
        }

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            string? name = assemblyName.Name;
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Share contract, MVVM, and licensed Syncfusion assemblies with the host default context
            // so IExportService type identity and SyncfusionLicenseProvider registrations remain unified.
            if (name.Equals("StockMonitor.Core", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("CommunityToolkit.Mvvm", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Syncfusion.", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string? assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            return assemblyPath != null ? LoadFromAssemblyPath(assemblyPath) : null;
        }

        protected override nint LoadUnmanagedDll(string unmanagedDllName)
        {
            string? libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return libraryPath != null ? LoadUnmanagedDllFromPath(libraryPath) : nint.Zero;
        }
    }
}

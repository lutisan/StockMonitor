using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;

namespace StockMonitor.Services;

/// <summary>
/// Generic plugin discovery and registration loader for <see cref="IExportService"/> (<c>*.export.dll</c>),
/// <see cref="IImportService"/> (<c>*.import.dll</c>), and any future contract interfaces.
/// Uses isolated <see cref="AssemblyLoadContext"/> instances backed by <see cref="AssemblyDependencyResolver"/>.
/// </summary>
public static class PluginLoader
{
    /// <summary>File search pattern for export plugin assemblies.</summary>
    public const string ExportSearchPattern = "*.export.dll";

    /// <summary>Default subfolder under <see cref="AppContext.BaseDirectory"/> searched for export plugins.</summary>
    public const string DefaultExportsFolderName = "Exports";

    /// <summary>File search pattern for import plugin assemblies.</summary>
    public const string ImportSearchPattern = "*.import.dll";

    /// <summary>Default subfolder under <see cref="AppContext.BaseDirectory"/> searched for import plugins.</summary>
    public const string DefaultImportsFolderName = "Imports";

    /// <summary>
    /// Discovers and registers all <see cref="IExportService"/> plugins from <c>*.export.dll</c> assemblies.
    /// </summary>
    public static int RegisterExportPlugins(IServiceCollection services, params string[] searchDirectories) =>
        RegisterPlugins<IExportService>(services, ExportSearchPattern, DefaultExportsFolderName, searchDirectories);

    /// <summary>
    /// Discovers and registers all <see cref="IImportService"/> plugins from <c>*.import.dll</c> assemblies.
    /// </summary>
    public static int RegisterImportPlugins(IServiceCollection services, params string[] searchDirectories) =>
        RegisterPlugins<IImportService>(services, ImportSearchPattern, DefaultImportsFolderName, searchDirectories);

    /// <summary>
    /// Scans the specified directories (or <see cref="AppContext.BaseDirectory"/> and <paramref name="defaultSubfolderName"/> when none are provided)
    /// for assemblies matching <paramref name="searchPattern"/> and registers every concrete public <typeparamref name="TService"/>
    /// implementation into <paramref name="services"/>.
    /// </summary>
    /// <typeparam name="TService">Plugin contract interface or base type.</typeparam>
    /// <param name="services">Target dependency injection service collection.</param>
    /// <param name="searchPattern">Filename pattern (e.g. <c>"*.export.dll"</c> or <c>"*.import.dll"</c>).</param>
    /// <param name="defaultSubfolderName">Default subfolder name under <see cref="AppContext.BaseDirectory"/>.</param>
    /// <param name="searchDirectories">Optional explicit directories to scan instead of defaults.</param>
    /// <returns>The number of <typeparamref name="TService"/> implementations registered.</returns>
    public static int RegisterPlugins<TService>(
        IServiceCollection services,
        string searchPattern,
        string defaultSubfolderName,
        params string[] searchDirectories)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        string[] directories = searchDirectories.Length > 0
            ? searchDirectories
            : [AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, defaultSubfolderName)];

        IReadOnlyList<Type> discoveredTypes = DiscoverPluginTypes<TService>(searchPattern, directories);
        foreach (Type pluginType in discoveredTypes)
        {
            services.AddSingleton(
                typeof(TService),
                sp => ActivatorUtilities.CreateInstance(sp, pluginType));
        }

        return discoveredTypes.Count;
    }

    /// <summary>
    /// Discovers concrete public <typeparamref name="TService"/> types from assemblies matching <paramref name="searchPattern"/>
    /// in the given <paramref name="searchDirectories"/>.
    /// </summary>
    /// <typeparam name="TService">Plugin contract interface or base type.</typeparam>
    /// <param name="searchPattern">Filename pattern (e.g. <c>"*.export.dll"</c> or <c>"*.import.dll"</c>).</param>
    /// <param name="searchDirectories">Directories to scan (non-existent directories are skipped).</param>
    /// <returns>Ordered list of discovered concrete <typeparamref name="TService"/> types.</returns>
    public static IReadOnlyList<Type> DiscoverPluginTypes<TService>(
        string searchPattern,
        IEnumerable<string> searchDirectories)
        where TService : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        ArgumentNullException.ThrowIfNull(searchDirectories);

        Type contractType = typeof(TService);
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
                pluginFiles = Directory.EnumerateFiles(dir, searchPattern, SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[PluginLoader] Failed to enumerate '{dir}' ({searchPattern}): {ex.Message}");
                continue;
            }

            foreach (string filePath in pluginFiles)
            {
                string fullPath = Path.GetFullPath(filePath);
                if (!visitedFiles.Add(fullPath))
                {
                    continue;
                }

                LoadPluginTypesFromAssembly(fullPath, contractType, result, registeredTypeKeys);
            }
        }

        return result;
    }

    private static void LoadPluginTypesFromAssembly(
        string assemblyPath,
        Type contractType,
        List<Type> target,
        HashSet<string> registeredTypeKeys)
    {
        try
        {
            PluginAssemblyLoadContext loadContext = new(assemblyPath);
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
                if (!type.IsClass || type.IsAbstract || !contractType.IsAssignableFrom(type))
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
            Trace.WriteLine($"[PluginLoader] Failed to load plugin '{assemblyPath}': {ex.Message}");
        }
    }

    /// <summary>
    /// Isolated load context for a plugin assembly that shares host contract and Syncfusion assemblies
    /// with <see cref="AssemblyLoadContext.Default"/> while resolving plugin-private dependencies via <see cref="AssemblyDependencyResolver"/>.
    /// </summary>
    private sealed class PluginAssemblyLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;

        public PluginAssemblyLoadContext(string pluginPath)
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
            // so contract type identity and SyncfusionLicenseProvider registrations remain unified.
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

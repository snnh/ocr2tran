using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Ocr2Tran.App;
using Ocr2Tran.Core;
using Ocr2Tran.Ocr;
using Ocr2Tran.Translation;

namespace Ocr2Tran.Plugins;

public sealed class PluginPipeline : IDisposable
{
    private static readonly object ResolverGate = new();
    private static readonly List<string> PluginDirectories = [];
    private static bool _resolverRegistered;

    private readonly List<object> _instances;
    private readonly PluginContext _context;

    private PluginPipeline(
        PluginContext context,
        List<object> instances,
        IReadOnlyList<IImageProcessingPlugin> imageProcessors,
        IReadOnlyList<ITextProcessingPlugin> textProcessors,
        IReadOnlyList<IOcrServicePlugin> ocrServices,
        IReadOnlyList<ITranslationServicePlugin> translationServices,
        IReadOnlyList<string> loadErrors)
    {
        _context = context;
        _instances = instances;
        ImageProcessors = imageProcessors;
        TextProcessors = textProcessors;
        OcrServices = ocrServices;
        TranslationServices = translationServices;
        LoadErrors = loadErrors;
    }

    public IReadOnlyList<IImageProcessingPlugin> ImageProcessors { get; }
    public IReadOnlyList<ITextProcessingPlugin> TextProcessors { get; }
    public IReadOnlyList<IOcrServicePlugin> OcrServices { get; }
    public IReadOnlyList<ITranslationServicePlugin> TranslationServices { get; }
    public IReadOnlyList<string> LoadErrors { get; }

    public static PluginPipeline Load(AppSettings settings)
    {
        var baseDirectory = AppContext.BaseDirectory;
        var pluginDirectory = ResolvePluginDirectory(baseDirectory, settings.Plugins.Directory);
        var context = new PluginContext(settings, baseDirectory, pluginDirectory);

        if (!settings.Plugins.Enabled || !Directory.Exists(pluginDirectory))
        {
            return Empty(context);
        }

        RegisterResolver(pluginDirectory);

        var instances = new List<object>();
        var loadErrors = new List<string>();
        var approvedHosts = ParseHostList(settings.Plugins.ApprovedNetworkHosts);
        foreach (var path in EnumeratePluginEntryAssemblies(pluginDirectory, settings.Plugins.RequireManifest))
        {
            if (!TryApprovePlugin(path, settings.Plugins.RequireManifest, approvedHosts, loadErrors))
            {
                continue;
            }

            LoadAssemblyPlugins(path, instances, loadErrors);
        }

        return new PluginPipeline(
            context,
            instances,
            Sort<IImageProcessingPlugin>(instances),
            Sort<ITextProcessingPlugin>(instances),
            Sort<IOcrServicePlugin>(instances),
            Sort<ITranslationServicePlugin>(instances),
            loadErrors);
    }

    public IOcrEngine? FindOcrService(string provider)
    {
        return FindByName(OcrServices, provider);
    }

    public ITranslator? FindTranslationService(string provider)
    {
        return FindByName(TranslationServices, provider);
    }

    public bool Owns(object instance)
    {
        return _instances.Contains(instance);
    }

    public async ValueTask<CapturedScreen> ProcessImageAsync(CapturedScreen screen, CancellationToken cancellationToken)
    {
        var current = screen;
        foreach (var plugin in ImageProcessors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var next = await plugin.ProcessAsync(current, _context, cancellationToken).ConfigureAwait(false);
            if (next is null)
            {
                continue;
            }

            if (!ReferenceEquals(current, screen) && !ReferenceEquals(current, next))
            {
                current.Dispose();
            }

            current = next;
        }

        return current;
    }

    public async ValueTask<IReadOnlyList<TextRegion>> ProcessOcrTextAsync(IReadOnlyList<TextRegion> regions, CancellationToken cancellationToken)
    {
        var current = regions;
        foreach (var plugin in TextProcessors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            current = await plugin.ProcessAsync(current, _context, cancellationToken).ConfigureAwait(false) ?? current;
        }

        return current;
    }

    public void Dispose()
    {
        foreach (var instance in _instances.OfType<IDisposable>())
        {
            instance.Dispose();
        }
    }

    private static PluginPipeline Empty(PluginContext context)
    {
        return new PluginPipeline(
            context,
            [],
            [],
            [],
            [],
            [],
            []);
    }

    private static string ResolvePluginDirectory(string baseDirectory, string configuredDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(configuredDirectory) ? "plugins" : configuredDirectory.Trim();
        return Path.GetFullPath(Path.IsPathRooted(directory)
            ? directory
            : Path.Combine(baseDirectory, directory));
    }

    private static void RegisterResolver(string pluginDirectory)
    {
        lock (ResolverGate)
        {
            if (!PluginDirectories.Contains(pluginDirectory, StringComparer.OrdinalIgnoreCase))
            {
                PluginDirectories.Add(pluginDirectory);
            }

            if (_resolverRegistered)
            {
                return;
            }

            AssemblyLoadContext.Default.Resolving += ResolvePluginDependency;
            _resolverRegistered = true;
        }
    }

    private static Assembly? ResolvePluginDependency(AssemblyLoadContext context, AssemblyName assemblyName)
    {
        lock (ResolverGate)
        {
            foreach (var directory in PluginDirectories)
            {
                var candidate = Path.Combine(directory, assemblyName.Name + ".dll");
                if (File.Exists(candidate))
                {
                    return context.LoadFromAssemblyPath(candidate);
                }
            }
        }

        return null;
    }

    private static void LoadAssemblyPlugins(string path, List<object> instances, List<string> loadErrors)
    {
        try
        {
            var assemblyName = AssemblyName.GetAssemblyName(path);
            if (assemblyName.Name == typeof(PluginPipeline).Assembly.GetName().Name)
            {
                return;
            }

            var assembly = LoadOrGetAssembly(path);
            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) is null || !IsPluginType(type))
                {
                    continue;
                }

                if (Activator.CreateInstance(type) is { } instance)
                {
                    instances.Add(instance);
                }
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or FileLoadException or ReflectionTypeLoadException or TargetInvocationException or TypeLoadException)
        {
            loadErrors.Add($"{Path.GetFileName(path)}: {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumeratePluginEntryAssemblies(string pluginDirectory, bool requireManifest)
    {
        if (!requireManifest)
        {
            return Directory.EnumerateFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        }

        return Directory
            .EnumerateFiles(pluginDirectory, "*.plugin.json", SearchOption.TopDirectoryOnly)
            .Select(path => Path.ChangeExtension(path, ".dll"))
            .Where(File.Exists);
    }

    private static bool TryApprovePlugin(string assemblyPath, bool requireManifest, IReadOnlySet<string> approvedHosts, List<string> loadErrors)
    {
        var manifestPath = Path.ChangeExtension(assemblyPath, ".plugin.json");
        if (!File.Exists(manifestPath))
        {
            if (requireManifest)
            {
                loadErrors.Add($"{Path.GetFileName(assemblyPath)}: missing plugin manifest {Path.GetFileName(manifestPath)}");
                return false;
            }

            return true;
        }

        PluginManifest? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            loadErrors.Add($"{Path.GetFileName(assemblyPath)}: invalid plugin manifest: {ex.Message}");
            return false;
        }

        var requestedHosts = manifest?.Network?.Hosts ?? [];
        foreach (var requestedHost in requestedHosts.Select(NormalizeHost).Where(host => host.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsHostApproved(requestedHost, approvedHosts))
            {
                loadErrors.Add($"{Path.GetFileName(assemblyPath)}: network host not approved: {requestedHost}");
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> ParseHostList(string value)
    {
        return value
            .Split([',', ';', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeHost)
            .Where(host => host.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            trimmed = uri.Host;
        }

        return trimmed.TrimEnd('.').ToLowerInvariant();
    }

    private static bool IsHostApproved(string host, IReadOnlySet<string> approvedHosts)
    {
        if (approvedHosts.Contains(host))
        {
            return true;
        }

        foreach (var approved in approvedHosts)
        {
            if (approved.StartsWith("*.", StringComparison.Ordinal) &&
                host.EndsWith(approved[1..], StringComparison.OrdinalIgnoreCase) &&
                host.Length > approved.Length - 1)
            {
                return true;
            }
        }

        return false;
    }

    private static Assembly LoadOrGetAssembly(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var loaded = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly =>
            !string.IsNullOrEmpty(assembly.Location) &&
            Path.GetFullPath(assembly.Location).Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        return loaded ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(fullPath);
    }

    private static bool IsPluginType(Type type)
    {
        return typeof(IImageProcessingPlugin).IsAssignableFrom(type) ||
               typeof(ITextProcessingPlugin).IsAssignableFrom(type) ||
               typeof(IOcrServicePlugin).IsAssignableFrom(type) ||
               typeof(ITranslationServicePlugin).IsAssignableFrom(type);
    }

    private sealed class PluginManifest
    {
        public PluginNetworkManifest? Network { get; set; }
    }

    private sealed class PluginNetworkManifest
    {
        public string[] Hosts { get; set; } = [];
    }

    private static IReadOnlyList<T> Sort<T>(IEnumerable<object> instances)
        where T : IOcr2TranPlugin
    {
        return instances
            .OfType<T>()
            .OrderBy(plugin => plugin.Order)
            .ThenBy(plugin => plugin.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static T? FindByName<T>(IEnumerable<T> plugins, string name)
        where T : IOcr2TranPlugin
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return default;
        }

        var trimmed = name.Trim();
        return plugins.FirstOrDefault(plugin => plugin.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase) ||
                                                plugin.GetType().FullName?.Equals(trimmed, StringComparison.OrdinalIgnoreCase) == true ||
                                                plugin.GetType().Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

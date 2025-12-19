// Copyright (c) Bravellian
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Registry of module types and initialized instances.
/// </summary>
public static class ModuleRegistry
{
    private static readonly System.Threading.Lock Sync = new();
    private static readonly HashSet<Type> BackgroundModuleTypes = new();
    private static readonly HashSet<Type> ApiModuleTypes = new();
    private static readonly HashSet<Type> FullStackModuleTypes = new();

    private static readonly Dictionary<Type, IModuleDefinition> Instances = new();

    /// <summary>
    /// Registers a background module type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    public static void RegisterBackgroundModule<T>() where T : class, IBackgroundModule, new()
    {
        RegisterType(typeof(T), BackgroundModuleTypes);
    }

    /// <summary>
    /// Registers an API module type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    public static void RegisterApiModule<T>() where T : class, IApiModule, new()
    {
        RegisterType(typeof(T), ApiModuleTypes);
    }

    /// <summary>
    /// Registers a full stack module type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    public static void RegisterFullStackModule<T>() where T : class, IFullStackModule, new()
    {
        RegisterType(typeof(T), FullStackModuleTypes);
    }

    internal static IReadOnlyCollection<IBackgroundModule> InitializeBackgroundModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return InitializeModules<IBackgroundModule>(BackgroundModuleTypes, configuration, services, loggerFactory);
    }

    internal static IReadOnlyCollection<IApiModule> InitializeApiModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return InitializeModules<IApiModule>(ApiModuleTypes, configuration, services, loggerFactory);
    }

    internal static IReadOnlyCollection<IFullStackModule> InitializeFullStackModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        return InitializeModules<IFullStackModule>(FullStackModuleTypes, configuration, services, loggerFactory);
    }

    internal static IReadOnlyCollection<IApiModule> GetApiModules()
    {
        lock (Sync)
        {
            return Instances.Values.OfType<IApiModule>().ToArray();
        }
    }

    internal static IReadOnlyCollection<IFullStackModule> GetFullStackModules()
    {
        lock (Sync)
        {
            return Instances.Values.OfType<IFullStackModule>().ToArray();
        }
    }

    /// <summary>
    /// Clears all registered module types and instances.
    /// </summary>
    /// <remarks>
    /// This method is intended for testing purposes only. It should not be used in production code
    /// as it affects global state that may be shared across different parts of the application.
    /// Tests using this method should not be run in parallel to avoid race conditions.
    /// </remarks>
    internal static void Reset()
    {
        lock (Sync)
        {
            BackgroundModuleTypes.Clear();
            ApiModuleTypes.Clear();
            FullStackModuleTypes.Clear();
            Instances.Clear();
        }
    }

    private static void RegisterType(Type type, ISet<Type> target)
    {
        lock (Sync)
        {
            // Check if this type is already registered in a different collection
            var alreadyInBackground = BackgroundModuleTypes.Contains(type);
            var alreadyInApi = ApiModuleTypes.Contains(type);
            var alreadyInFullStack = FullStackModuleTypes.Contains(type);

            if ((alreadyInBackground && target != BackgroundModuleTypes) ||
                (alreadyInApi && target != ApiModuleTypes) ||
                (alreadyInFullStack && target != FullStackModuleTypes))
            {
                throw new InvalidOperationException($"Module type '{type.FullName}' is already registered in a different category. A module cannot be registered in multiple categories.");
            }

            // If already in the target collection, this is a no-op
            if (target.Contains(type))
            {
                return;
            }

            target.Add(type);
        }
    }

    private static IReadOnlyCollection<TModule> InitializeModules<TModule>(
        IEnumerable<Type> types,
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
        where TModule : class, IModuleDefinition
    {
        // Take a snapshot of types under lock to avoid enumeration issues
        Type[] typeSnapshot;
        lock (Sync)
        {
            typeSnapshot = types.ToArray();
        }

        var initialized = new List<TModule>();
        foreach (var type in typeSnapshot)
        {
            var module = (TModule)CreateInstance(type, loggerFactory);
            LoadConfiguration(configuration, module, loggerFactory);
            RegisterInstance(module);
            initialized.Add(module);
        }

        // Validate module keys before mutating the service collection
        EnsureUniqueKeys();

        // Only after successful validation, register services and health checks
        foreach (var module in initialized)
        {
            module.AddModuleServices(services);
            RegisterHealthChecks(services, module, loggerFactory);
        }

        return initialized;
    }

    private static IModuleDefinition CreateInstance(Type type, ILoggerFactory? loggerFactory)
    {
        try
        {
            return (IModuleDefinition)Activator.CreateInstance(type)!;
        }
        catch (Exception ex)
        {
            loggerFactory?.CreateLogger(typeof(ModuleRegistry))
                .LogError(ex, "Failed to create module instance for {ModuleType}", type.FullName);
            throw;
        }
    }

    private static void LoadConfiguration(IConfiguration configuration, IModuleDefinition module, ILoggerFactory? loggerFactory)
    {
        var required = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in module.GetRequiredConfigurationKeys())
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Missing required configuration '{key}' for module '{module.Key}'.");
            }

            required[key] = value;
        }

        var optional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in module.GetOptionalConfigurationKeys())
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                optional[key] = value;
            }
        }

        try
        {
            module.LoadConfiguration(required, optional);
        }
        catch (Exception ex)
        {
            loggerFactory?.CreateLogger(typeof(ModuleRegistry))
                .LogError(ex, "Failed to load configuration for module {ModuleKey}", module.Key);
            throw;
        }
    }

    private static void RegisterHealthChecks(IServiceCollection services, IModuleDefinition module, ILoggerFactory? loggerFactory)
    {
        var builder = services.AddHealthChecks();
        var logger = loggerFactory?.CreateLogger(typeof(ModuleRegistry));
        var moduleBuilder = new ModuleHealthCheckBuilder(builder, logger);
        try
        {
            module.RegisterHealthChecks(moduleBuilder);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to register health checks for module {ModuleKey}", module.Key);
            throw;
        }
    }

    private static void RegisterInstance(IModuleDefinition module)
    {
        // Validate module key is URL-safe (no slashes)
        if (module.Key.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Module key '{module.Key}' contains invalid characters. Module keys must be URL-safe and cannot contain slashes.");
        }

        lock (Sync)
        {
            Instances[module.GetType()] = module;
        }
    }

    private static void EnsureUniqueKeys()
    {
        lock (Sync)
        {
            var duplicates = Instances.Values
                .GroupBy(instance => instance.Key, StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate module key detected (comparison is case-insensitive): '{duplicates[0]}'.");
            }
        }
    }
}

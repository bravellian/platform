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
/// Internal registry of module types and initialized instances.
/// </summary>
internal static class ModuleRegistry
{
    private static readonly System.Threading.Lock Sync = new();
    private static readonly Dictionary<ModuleCategory, HashSet<Type>> RegisteredTypes = new()
    {
        [ModuleCategory.Background] = new HashSet<Type>(),
        [ModuleCategory.Api] = new HashSet<Type>(),
        [ModuleCategory.FullStack] = new HashSet<Type>(),
    };

    private static readonly Dictionary<Type, IModuleDefinition> Instances = new();

    internal static void RegisterModuleType(Type type, ModuleCategory category)
    {
        lock (Sync)
        {
            var alreadyRegistered = RegisteredTypes
                .Where(pair => pair.Value.Contains(type))
                .Select(pair => pair.Key)
                .ToArray();

            if (alreadyRegistered.Length > 0 && !alreadyRegistered.Contains(category))
            {
                throw new InvalidOperationException($"Module type '{type.FullName}' is already registered in a different category. A module cannot be registered in multiple categories.");
            }

            var targetSet = RegisteredTypes[category];
            if (targetSet.Contains(type))
            {
                return;
            }

            targetSet.Add(type);
        }
    }

    internal static IReadOnlyCollection<Type> GetRegisteredTypes(ModuleCategory category)
    {
        lock (Sync)
        {
            return RegisteredTypes[category].ToArray();
        }
    }

    internal static IReadOnlyCollection<TModule> InitializeModules<TModule>(
        ModuleCategory category,
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
        where TModule : class, IModuleDefinition
    {
        var types = SnapshotTypes(category);
        var initialized = new List<TModule>();

        foreach (var type in types)
        {
            var module = (TModule)CreateInstance(type, loggerFactory);
            LoadConfiguration(configuration, module, loggerFactory);
            RegisterInstance(module);
            initialized.Add(module);
        }

        EnsureUniqueKeys();

        foreach (var module in initialized)
        {
            module.AddModuleServices(services);
            RegisterHealthChecks(services, module, loggerFactory);
        }

        return initialized;
    }

    internal static IReadOnlyCollection<TModule> GetModules<TModule>()
        where TModule : class, IModuleDefinition
    {
        lock (Sync)
        {
            return Instances.Values.OfType<TModule>().ToArray();
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
            foreach (var entry in RegisteredTypes.Values)
            {
                entry.Clear();
            }

            Instances.Clear();
        }
    }

    private static Type[] SnapshotTypes(ModuleCategory category)
    {
        lock (Sync)
        {
            return RegisteredTypes[category].ToArray();
        }
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
                .Where(g => g.Skip(1).Any())
                .Select(g => g.Key)
                .ToList();

            if (duplicates.Count > 0)
            {
                throw new InvalidOperationException($"Duplicate module key detected (comparison is case-insensitive): '{duplicates[0]}'.");
            }
        }
    }
}

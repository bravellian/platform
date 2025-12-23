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
    private static readonly HashSet<Type> RegisteredTypes = new();

    private static readonly Dictionary<Type, IModuleDefinition> Instances = new();

    /// <summary>
    /// Registers a module type.
    /// </summary>
    /// <typeparam name="T">The module type.</typeparam>
    public static void RegisterModule<T>() where T : class, IModuleDefinition, new()
    {
        RegisterModuleType(typeof(T));
    }

    internal static void RegisterModuleType(Type type)
    {
        lock (Sync)
        {
            if (RegisteredTypes.Contains(type))
            {
                return;
            }

            RegisteredTypes.Add(type);
        }
    }

    /// <summary>
    /// Gets a snapshot of all registered module types.
    /// </summary>
    /// <returns>A read-only collection of registered module types.</returns>
    public static IReadOnlyCollection<Type> GetRegisteredModuleTypes()
    {
        lock (Sync)
        {
            return RegisteredTypes.ToArray();
        }
    }

    internal static IReadOnlyCollection<IModuleDefinition> InitializeModules(
        IConfiguration configuration,
        IServiceCollection services,
        ILoggerFactory? loggerFactory)
    {
        var types = SnapshotTypes();
        var initialized = new List<IModuleDefinition>();

        foreach (var type in types)
        {
            var module = CreateInstance(type, loggerFactory);
            LoadConfiguration(configuration, module, loggerFactory);
            RegisterInstance(module);
            RegisterEngines(module);
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
            RegisteredTypes.Clear();
            Instances.Clear();
            ModuleEngineRegistry.Reset();
        }
    }

    private static Type[] SnapshotTypes()
    {
        lock (Sync)
        {
            return RegisteredTypes.ToArray();
        }
    }

    private static void RegisterEngines(IModuleDefinition module)
    {
        var descriptors = module.DescribeEngines()?.ToArray() ?? Array.Empty<IModuleEngineDescriptor>();
        if (descriptors.Length == 0)
        {
            return;
        }

        var validated = descriptors
            .Select(descriptor =>
            {
                if (!string.Equals(descriptor.ModuleKey, module.Key, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Engine descriptor module key '{descriptor.ModuleKey}' must match its owning module key '{module.Key}'. " +
                        "Engine descriptors must use their owning module's key to ensure proper isolation and discovery. " +
                        "Update the engine descriptor's ModuleKey to match the module's Key.");
                }

                return descriptor;
            })
            .ToArray();

        ModuleEngineRegistry.Register(module.Key, validated);
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

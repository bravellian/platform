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

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Engine discovery service used by adapters and hosts.
/// </summary>
public sealed class ModuleEngineDiscoveryService
{
    /// <summary>
    /// Lists all engines registered by modules.
    /// </summary>
    public IReadOnlyCollection<IModuleEngineDescriptor> List() => ModuleEngineRegistry.GetEngines();

    /// <summary>
    /// Lists engines filtered by kind or feature area.
    /// </summary>
    public IReadOnlyCollection<IModuleEngineDescriptor> List(EngineKind? kind, string? featureArea = null)
    {
        return ModuleEngineRegistry.GetEngines()
            .Where(e => (!kind.HasValue || e.Manifest.Kind == kind.Value)
                     && (featureArea is null || string.Equals(e.Manifest.FeatureArea, featureArea, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Resolves a webhook engine by provider and event type.
    /// </summary>
    public IModuleEngineDescriptor? ResolveWebhookEngine(string provider, string eventType) => ModuleEngineRegistry.FindWebhookEngine(provider, eventType);

    /// <summary>
    /// Resolves an engine descriptor by module and engine identifier.
    /// </summary>
    public IModuleEngineDescriptor? ResolveById(string moduleKey, string engineId) => ModuleEngineRegistry.FindById(moduleKey, engineId);

    /// <summary>
    /// Resolves an engine instance for a descriptor.
    /// </summary>
    public TContract ResolveEngine<TContract>(ModuleEngineDescriptor<TContract> descriptor, IServiceProvider serviceProvider)
        where TContract : notnull
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var instance = descriptor.Factory(serviceProvider);

        if (instance is null)
        {
            throw new System.InvalidOperationException(
                $"The factory for module engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' returned null.");
        }

        return instance;
    }
    /// <summary>
    /// Resolves an engine instance for a descriptor when only the contract type is known at runtime.
    /// </summary>
    public object ResolveEngine(IModuleEngineDescriptor descriptor, IServiceProvider serviceProvider)
    {
        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        var instance = descriptor.Create(serviceProvider);

        if (instance is null)
        {
            throw new InvalidOperationException(
                $"The factory for module engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' returned null.");
        }

        return instance;
    }
}

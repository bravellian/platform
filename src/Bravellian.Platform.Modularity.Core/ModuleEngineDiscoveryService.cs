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
    public IReadOnlyCollection<ModuleEngineDescriptor> List() => ModuleEngineRegistry.GetEngines();

    /// <summary>
    /// Lists engines filtered by kind or feature area.
    /// </summary>
    public IReadOnlyCollection<ModuleEngineDescriptor> List(EngineKind? kind, string? featureArea = null)
    {
        return ModuleEngineRegistry.GetEngines()
            .Where(e => (!kind.HasValue || e.Manifest.Kind == kind.Value)
                     && (featureArea is null || string.Equals(e.Manifest.FeatureArea, featureArea, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }

    /// <summary>
    /// Resolves a webhook engine by provider and event type.
    /// </summary>
    public ModuleEngineDescriptor? ResolveWebhookEngine(string provider, string eventType) => ModuleEngineRegistry.FindWebhookEngine(provider, eventType);

    /// <summary>
    /// Resolves an engine descriptor by module and engine identifier.
    /// </summary>
    public ModuleEngineDescriptor? ResolveById(string moduleKey, string engineId) => ModuleEngineRegistry.FindById(moduleKey, engineId);

    /// <summary>
    /// Resolves an engine instance for a descriptor.
    /// </summary>
    public object ResolveEngine(ModuleEngineDescriptor descriptor, IServiceProvider serviceProvider) => descriptor.Factory(serviceProvider);
}

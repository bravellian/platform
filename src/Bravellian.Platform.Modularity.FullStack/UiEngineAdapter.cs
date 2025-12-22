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

using Microsoft.Extensions.DependencyInjection;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Adapter that maps UI engine contracts to host navigation tokens.
/// </summary>
public sealed class UiEngineAdapter
{
    private readonly ModuleEngineDiscoveryService discoveryService;
    private readonly IServiceProvider services;

    /// <summary>
    /// Initializes a new instance of the <see cref="UiEngineAdapter"/> class.
    /// </summary>
    public UiEngineAdapter(ModuleEngineDiscoveryService discoveryService, IServiceProvider services)
    {
        this.discoveryService = discoveryService;
        this.services = services;
    }

    /// <summary>
    /// Executes a UI engine and returns a transport-ready response.
    /// </summary>
    public async Task<UiAdapterResponse<TViewModel>> ExecuteAsync<TInput, TViewModel>(string moduleKey, string engineId, TInput command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(moduleKey))
        {
            throw new ArgumentException("Module key must be a non-empty, non-whitespace string.", nameof(moduleKey));
        }

        if (string.IsNullOrWhiteSpace(engineId))
        {
            throw new ArgumentException("Engine ID must be a non-empty, non-whitespace string.", nameof(engineId));
        }

        if (command is null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        var descriptor = discoveryService.ResolveById(moduleKey, engineId)
            ?? throw new InvalidOperationException($"No UI engine registered with id '{engineId}' for module '{moduleKey}'.");

        ValidateRequiredServices(descriptor, descriptor.Manifest.RequiredServices);

        var typedDescriptor = descriptor as ModuleEngineDescriptor<IUiEngine<TInput, TViewModel>>
            ?? throw new InvalidOperationException($"Engine '{engineId}' does not implement the expected UI contract.");

        var engine = discoveryService.ResolveEngine(typedDescriptor, services);

        var result = await engine.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);

        return new UiAdapterResponse<TViewModel>(result.ViewModel, result.NavigationTargets, result.Events);
    }

    private void ValidateRequiredServices(IModuleEngineDescriptor descriptor, IReadOnlyCollection<string>? requiredServices)
    {
        if (requiredServices is null || requiredServices.Count == 0)
        {
            return;
        }

        foreach (var service in requiredServices)
        {
            if (string.IsNullOrWhiteSpace(service))
            {
                throw new InvalidOperationException(
                    $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' declares an empty required service identifier.");
            }
        }

        var validator = services.GetService<IRequiredServiceValidator>();
        if (validator is null)
        {
            throw new InvalidOperationException(
                $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' declares required services but no {nameof(IRequiredServiceValidator)} is registered.");
        }

        var missing = validator.GetMissingServices(requiredServices) ?? Array.Empty<string>();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Engine '{descriptor.ModuleKey}/{descriptor.Manifest.Id}' is missing required services: {string.Join(", ", missing)}.");
        }
    }
}

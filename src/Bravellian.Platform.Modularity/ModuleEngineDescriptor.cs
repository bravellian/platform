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
/// Strongly typed engine descriptor registered by a module. Factories are provided by modules and consumed by adapters/hosts.
/// </summary>
/// <typeparam name="TContract">Engine contract interface (e.g., <see cref="IUiEngine{TInput, TResult}"/>).</typeparam>
/// <param name="ModuleKey">Module key that owns the engine.</param>
/// <param name="Manifest">Engine manifest metadata.</param>
/// <param name="Factory">Factory that resolves the engine instance from an <see cref="IServiceProvider"/>.</param>
public sealed record ModuleEngineDescriptor<TContract>(
    string ModuleKey,
    ModuleEngineManifest Manifest,
    Func<IServiceProvider, TContract> Factory) : IModuleEngineDescriptor where TContract : notnull
{
    /// <summary>
    /// Gets the contract type implemented by the engine.
    /// </summary>
    public Type ContractType => typeof(TContract);

    /// <summary>
    /// Creates the engine instance from the service provider.
    /// </summary>
    /// <param name="serviceProvider">The service provider used to resolve the engine.</param>
    /// <returns>The engine instance.</returns>
    public object? Create(IServiceProvider serviceProvider) => Factory(serviceProvider);
}

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
/// Describes a module engine in a transport-agnostic manner.
/// </summary>
/// <param name="Id">Engine identifier scoped to the module.</param>
/// <param name="Version">Contract version for the engine.</param>
/// <param name="Description">Human readable description.</param>
/// <param name="Kind">Engine kind (UI or webhook).</param>
/// <param name="FeatureArea">Optional feature area/page grouping.</param>
/// <param name="Capabilities">Action/event capabilities.</param>
/// <param name="Inputs">Input schemas.</param>
/// <param name="Outputs">Output schemas.</param>
/// <param name="NavigationHints">Navigation tokens understood by adapters.</param>
/// <param name="RequiredServices">Services the host must supply via DI.</param>
/// <param name="AdapterHints">Transport-level hints for adapters.</param>
/// <param name="Security">Security metadata, typically for webhook engines.</param>
/// <param name="Compatibility">Compatibility and version notes.</param>
/// <param name="WebhookMetadata">Webhook event metadata advertised by the engine.</param>
public sealed record ModuleEngineManifest(
    string Id,
    string Version,
    string Description,
    EngineKind Kind,
    string? FeatureArea = null,
    ModuleEngineCapabilities? Capabilities = null,
    IReadOnlyCollection<ModuleEngineSchema>? Inputs = null,
    IReadOnlyCollection<ModuleEngineSchema>? Outputs = null,
    ModuleEngineNavigationHints? NavigationHints = null,
    IReadOnlyCollection<string>? RequiredServices = null,
    ModuleEngineAdapterHints? AdapterHints = null,
    ModuleEngineSecurity? Security = null,
    ModuleEngineCompatibility? Compatibility = null,
    IReadOnlyCollection<ModuleEngineWebhookMetadata>? WebhookMetadata = null);

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

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// Options for mapping UI engine endpoints.
/// </summary>
public sealed class UiEngineEndpointOptions
{
    /// <summary>
    /// Route pattern for UI engine execution.
    /// </summary>
    public string RoutePattern { get; set; } = "/modules/{moduleKey}/ui/{engineId}";

    /// <summary>
    /// Route parameter name for the module key.
    /// </summary>
    public string ModuleKeyRouteParameterName { get; set; } = "moduleKey";

    /// <summary>
    /// Route parameter name for the engine id.
    /// </summary>
    public string EngineIdRouteParameterName { get; set; } = "engineId";

    /// <summary>
    /// Optional schema name to select the input type.
    /// </summary>
    public string? InputSchemaName { get; set; }

    /// <summary>
    /// Optional schema name to select the output type.
    /// </summary>
    public string? OutputSchemaName { get; set; }

    /// <summary>
    /// Overrides the JSON serializer options.
    /// </summary>
    public JsonSerializerOptions? SerializerOptions { get; set; }

    /// <summary>
    /// Custom response mapping for UI adapter results.
    /// </summary>
    public Func<object, IResult>? ResponseFactory { get; set; }
}

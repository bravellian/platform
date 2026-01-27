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

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Bravellian.Platform.Webhooks;
using Bravellian.Platform.Webhooks.AspNetCore;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// ASP.NET Core endpoint helpers for module engines.
/// </summary>
public static class ModuleEndpointRouteBuilderExtensions
{
    private static readonly MethodInfo UiExecuteMethod = typeof(UiEngineAdapter)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(method => string.Equals(method.Name, "ExecuteAsync", StringComparison.Ordinal) && method.IsGenericMethodDefinition);

    /// <summary>
    /// Maps a generic UI engine endpoint that uses engine manifests to deserialize inputs.
    /// </summary>
    public static IEndpointConventionBuilder MapUiEngineEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<UiEngineEndpointOptions>? configure = null)
    {
        var options = new UiEngineEndpointOptions();
        configure?.Invoke(options);

        return endpoints.MapPost(options.RoutePattern, async (
            HttpContext context,
            UiEngineAdapter adapter,
            ModuleEngineDiscoveryService discovery,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetRouteValue(context, options.ModuleKeyRouteParameterName, out var moduleKey)
                || !TryGetRouteValue(context, options.EngineIdRouteParameterName, out var engineId))
            {
                return Results.BadRequest("Route parameters must include module and engine identifiers.");
            }

            var descriptor = ResolveUiDescriptor(discovery, moduleKey, engineId);
            if (descriptor is null)
            {
                return Results.NotFound();
            }

            var inputType = ResolveSchemaType(descriptor.Manifest.Inputs, options.InputSchemaName);
            if (inputType is null)
            {
                return Results.BadRequest("UI engine manifest must declare an input schema.");
            }

            var outputType = ResolveSchemaType(descriptor.Manifest.Outputs, options.OutputSchemaName);
            if (outputType is null)
            {
                return Results.BadRequest("UI engine manifest must declare an output schema.");
            }

            var rawBody = await ReadRawBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return Results.BadRequest("Request body is required.");
            }

            var serializerOptions = ResolveJsonOptions(context, options.SerializerOptions);
            var command = JsonSerializer.Deserialize(rawBody, inputType, serializerOptions);
            if (command is null)
            {
                return Results.BadRequest("Request body is required.");
            }

            var response = await ExecuteUiEngineAsync(
                adapter,
                moduleKey,
                engineId,
                inputType,
                outputType,
                command,
                cancellationToken).ConfigureAwait(false);

            return options.ResponseFactory?.Invoke(response) ?? Results.Ok(response);
        });
    }

    /// <summary>
    /// Maps a webhook intake endpoint that forwards requests to the webhook ingestion pipeline.
    /// </summary>
    public static IEndpointConventionBuilder MapWebhookEngineEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<WebhookEndpointOptions>? configure = null)
    {
        var options = new WebhookEndpointOptions();
        configure?.Invoke(options);

        return endpoints.MapPost(options.RoutePattern, async (
            HttpContext context,
            IWebhookIngestor ingestor,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetRouteValue(context, options.ProviderRouteParameterName, out var provider)
                || !TryGetRouteValue(context, options.EventTypeRouteParameterName, out var eventType))
            {
                return Results.BadRequest("Route parameters must include provider and event type.");
            }

            if (!string.IsNullOrWhiteSpace(options.EventTypeHeaderName))
            {
                context.Request.Headers[options.EventTypeHeaderName] = eventType;
            }

            return await WebhookEndpoint.HandleAsync(context, provider, ingestor, cancellationToken)
                .ConfigureAwait(false);
        });
    }

    private static bool TryGetRouteValue(HttpContext context, string name, out string value)
    {
        if (context.Request.RouteValues.TryGetValue(name, out var routeValue)
            && routeValue is not null
            && !string.IsNullOrWhiteSpace(routeValue.ToString()))
        {
            value = routeValue.ToString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static IModuleEngineDescriptor? ResolveUiDescriptor(
        ModuleEngineDiscoveryService discovery,
        string moduleKey,
        string engineId)
    {
        var descriptor = discovery.ResolveById(moduleKey, engineId);
        if (descriptor is not null && descriptor.Manifest.Kind == EngineKind.Ui)
        {
            return descriptor;
        }

        return discovery.List(EngineKind.Ui)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.ModuleKey, moduleKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(candidate.Manifest.Id, engineId, StringComparison.OrdinalIgnoreCase));
    }

    private static Type? ResolveSchemaType(
        IReadOnlyCollection<ModuleEngineSchema>? schemas,
        string? schemaName)
    {
        if (schemas is null || schemas.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(schemaName))
        {
            var match = schemas.FirstOrDefault(schema =>
                string.Equals(schema.Name, schemaName, StringComparison.OrdinalIgnoreCase));
            return match?.ClrType;
        }

        return schemas.First().ClrType;
    }


    private static async Task<object> ExecuteUiEngineAsync(
        UiEngineAdapter adapter,
        string moduleKey,
        string engineId,
        Type inputType,
        Type outputType,
        object command,
        CancellationToken cancellationToken)
    {
        var method = UiExecuteMethod.MakeGenericMethod(inputType, outputType);
        var task = (Task)method.Invoke(adapter, new[] { moduleKey, engineId, command, cancellationToken })!;
        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")!.GetValue(task)!;
    }


    private static JsonSerializerOptions ResolveJsonOptions(HttpContext context, JsonSerializerOptions? overrideOptions)
    {
        if (overrideOptions is not null)
        {
            return overrideOptions;
        }

        var options = context.RequestServices.GetService<IOptions<JsonOptions>>()?.Value?.SerializerOptions;
        return options ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private static async Task<string> ReadRawBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}

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

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bravellian.Platform.Modularity;

/// <summary>
/// ASP.NET Core endpoint helpers for module engines.
/// </summary>
public static class ModuleEndpointRouteBuilderExtensions
{
    private static readonly MethodInfo UiExecuteMethod = typeof(UiEngineAdapter)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(method => string.Equals(method.Name, "ExecuteAsync", StringComparison.Ordinal) && method.IsGenericMethodDefinition);

    private static readonly MethodInfo WebhookDispatchMethod = typeof(WebhookEngineAdapter)
        .GetMethods(BindingFlags.Public | BindingFlags.Instance)
        .Single(method => string.Equals(method.Name, "DispatchAsync", StringComparison.Ordinal) && method.IsGenericMethodDefinition);

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
    /// Maps a generic webhook intake endpoint that uses webhook metadata to deserialize payloads.
    /// </summary>
    public static IEndpointConventionBuilder MapWebhookEngineEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<WebhookEndpointOptions>? configure = null)
    {
        var options = new WebhookEndpointOptions();
        configure?.Invoke(options);

        return endpoints.MapPost(options.RoutePattern, async (
            HttpContext context,
            WebhookEngineAdapter adapter,
            ModuleEngineDiscoveryService discovery,
            CancellationToken cancellationToken) =>
        {
            if (!TryGetRouteValue(context, options.ProviderRouteParameterName, out var provider)
                || !TryGetRouteValue(context, options.EventTypeRouteParameterName, out var eventType))
            {
                return Results.BadRequest("Route parameters must include provider and event type.");
            }

            var descriptor = discovery.ResolveWebhookEngine(provider, eventType);
            if (descriptor is null)
            {
                return Results.NotFound();
            }

            var payloadType = ResolveWebhookPayloadType(descriptor, provider, eventType);
            if (payloadType is null)
            {
                return Results.NotFound();
            }

            var rawBody = await ReadRawBodyAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(rawBody))
            {
                return Results.BadRequest("Request body is required.");
            }

            var serializerOptions = ResolveJsonOptions(context, options.SerializerOptions);
            var payload = JsonSerializer.Deserialize(rawBody, payloadType, serializerOptions);
            if (payload is null)
            {
                return Results.BadRequest("Request body is required.");
            }

            var headers = context.Request.Headers.ToDictionary(
                header => header.Key,
                header => header.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);

            var signature = ResolveHeaderOrQuery(
                context.Request,
                options.SignatureHeaderName,
                options.SignatureQueryName);

            var idempotencyKey = ResolveHeaderOrQuery(
                context.Request,
                options.IdempotencyHeaderName,
                options.IdempotencyQueryName) ?? string.Empty;

            var attempt = ResolveAttempt(context.Request, options.AttemptHeaderName, options.AttemptQueryName);

            var request = CreateWebhookRequest(
                payloadType,
                provider,
                eventType,
                headers,
                rawBody,
                idempotencyKey,
                attempt,
                signature,
                payload);

            var response = await DispatchWebhookAsync(
                adapter,
                payloadType,
                request,
                cancellationToken).ConfigureAwait(false);

            if (options.ResponseFactory is not null)
            {
                return options.ResponseFactory(response);
            }

            var statusCode = ResolveStatusCode(options, response.Outcome);
            return Results.Json(response, statusCode: statusCode);
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

    private static Type? ResolveWebhookPayloadType(
        IModuleEngineDescriptor descriptor,
        string provider,
        string eventType)
    {
        if (descriptor.Manifest.WebhookMetadata is null)
        {
            return null;
        }

        var entry = descriptor.Manifest.WebhookMetadata.FirstOrDefault(metadata =>
            string.Equals(metadata.Provider, provider, StringComparison.OrdinalIgnoreCase)
            && string.Equals(metadata.EventType, eventType, StringComparison.OrdinalIgnoreCase));

        return entry?.PayloadSchema.ClrType;
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

    private static async Task<WebhookAdapterResponse> DispatchWebhookAsync(
        WebhookEngineAdapter adapter,
        Type payloadType,
        object request,
        CancellationToken cancellationToken)
    {
        var method = WebhookDispatchMethod.MakeGenericMethod(payloadType);
        var task = (Task)method.Invoke(adapter, new[] { request, cancellationToken })!;
        await task.ConfigureAwait(false);
        return (WebhookAdapterResponse)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    private static object CreateWebhookRequest(
        Type payloadType,
        string provider,
        string eventType,
        IReadOnlyDictionary<string, string> headers,
        string rawBody,
        string idempotencyKey,
        int attempt,
        string? signature,
        object payload)
    {
        var requestType = typeof(WebhookAdapterRequest<>).MakeGenericType(payloadType);
        return Activator.CreateInstance(
            requestType,
            provider,
            eventType,
            headers,
            rawBody,
            idempotencyKey,
            attempt,
            signature,
            payload)!;
    }

    private static string? ResolveHeaderOrQuery(
        HttpRequest request,
        string? headerName,
        string? queryName)
    {
        if (!string.IsNullOrWhiteSpace(headerName)
            && request.Headers.TryGetValue(headerName, out var headerValue)
            && !string.IsNullOrWhiteSpace(headerValue))
        {
            return headerValue.ToString();
        }

        if (!string.IsNullOrWhiteSpace(queryName)
            && request.Query.TryGetValue(queryName, out var queryValue)
            && !string.IsNullOrWhiteSpace(queryValue))
        {
            return queryValue.ToString();
        }

        return null;
    }

    private static int ResolveAttempt(
        HttpRequest request,
        string? headerName,
        string? queryName)
    {
        if (!string.IsNullOrWhiteSpace(headerName)
            && request.Headers.TryGetValue(headerName, out var headerValue)
            && int.TryParse(headerValue.ToString(), CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        if (!string.IsNullOrWhiteSpace(queryName)
            && request.Query.TryGetValue(queryName, out var queryValue)
            && int.TryParse(queryValue.ToString(), CultureInfo.InvariantCulture, out parsed))
        {
            return parsed;
        }

        return 1;
    }

    private static int ResolveStatusCode(WebhookEndpointOptions options, WebhookOutcomeType outcome)
    {
        return options.OutcomeStatusCodes.TryGetValue(outcome, out var statusCode)
            ? statusCode
            : StatusCodes.Status200OK;
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

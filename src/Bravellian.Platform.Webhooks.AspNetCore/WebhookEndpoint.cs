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

using Microsoft.AspNetCore.Http;
using Bravellian.Platform.Webhooks;

namespace Bravellian.Platform.Webhooks.AspNetCore;

/// <summary>
/// Helpers for exposing webhook endpoints.
/// </summary>
public static class WebhookEndpoint
{
    /// <summary>
    /// Handles an inbound webhook request and returns the appropriate HTTP result.
    /// </summary>
    /// <param name="context">HTTP context.</param>
    /// <param name="providerName">Provider name.</param>
    /// <param name="ingestor">Webhook ingestor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result to return to the provider.</returns>
    public static async Task<IResult> HandleAsync(
        HttpContext context,
        string providerName,
        IWebhookIngestor ingestor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(providerName))
        {
            throw new ArgumentException("Provider name is required.", nameof(providerName));
        }

        if (ingestor == null)
        {
            ArgumentNullException.ThrowIfNull(ingestor);
        }

        var request = context.Request;
        if (!request.Body.CanSeek)
        {
            request.EnableBuffering();
        }

        byte[] bodyBytes;
        using (var memory = new MemoryStream())
        {
            await request.Body.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            bodyBytes = memory.ToArray();
        }

        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToString();
        }

        var envelope = new WebhookEnvelope(
            providerName,
            DateTimeOffset.UtcNow,
            request.Method,
            request.Path.HasValue ? request.Path.Value! : string.Empty,
            request.QueryString.HasValue ? request.QueryString.Value! : string.Empty,
            headers,
            request.ContentType,
            bodyBytes,
            context.Connection.RemoteIpAddress?.ToString());

        var result = await ingestor.IngestAsync(providerName, envelope, cancellationToken).ConfigureAwait(false);
        return Results.StatusCode((int)result.HttpStatusCode);
    }
}



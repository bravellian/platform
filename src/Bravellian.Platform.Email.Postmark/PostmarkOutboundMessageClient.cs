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

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Queries Postmark outbound message status for reconciliation.
/// </summary>
public sealed class PostmarkOutboundMessageClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient;
    private readonly PostmarkOptions options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkOutboundMessageClient"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="options">Postmark options.</param>
    public PostmarkOutboundMessageClient(HttpClient httpClient, PostmarkOptions options)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.options.Validate();

        if (this.httpClient.BaseAddress == null)
        {
            this.httpClient.BaseAddress = this.options.BaseUrl;
        }
    }

    /// <summary>
    /// Retrieves outbound message details by Postmark message id.
    /// </summary>
    /// <param name="messageId">Postmark message id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup result.</returns>
    public async Task<PostmarkMessageDetailsLookup> GetOutboundMessageDetailsAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return new PostmarkMessageDetailsLookup(PostmarkQueryStatus.NotFound, null, "MessageId is empty.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildDetailsUri(messageId));
        request.Headers.Add("X-Postmark-Server-Token", options.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new PostmarkMessageDetailsLookup(PostmarkQueryStatus.NotFound, null, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return new PostmarkMessageDetailsLookup(PostmarkQueryStatus.Error, null, error);
        }

        var details = await DeserializeAsync<PostmarkOutboundMessageDetails>(response, cancellationToken).ConfigureAwait(false);
        if (details == null)
        {
            return new PostmarkMessageDetailsLookup(PostmarkQueryStatus.Error, null, "Failed to parse Postmark response.");
        }

        return new PostmarkMessageDetailsLookup(PostmarkQueryStatus.Found, details, null);
    }

    /// <summary>
    /// Searches outbound messages by metadata key/value.
    /// </summary>
    /// <param name="metadataKey">Metadata key.</param>
    /// <param name="metadataValue">Metadata value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Lookup result.</returns>
    public async Task<PostmarkSearchLookup> SearchOutboundByMetadataAsync(
        string metadataKey,
        string metadataValue,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metadataKey) || string.IsNullOrWhiteSpace(metadataValue))
        {
            return new PostmarkSearchLookup(PostmarkQueryStatus.NotFound, null, "Metadata filter missing.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildSearchUri(metadataKey, metadataValue));
        request.Headers.Add("X-Postmark-Server-Token", options.ServerToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            return new PostmarkSearchLookup(PostmarkQueryStatus.Error, null, error);
        }

        var result = await DeserializeAsync<PostmarkOutboundSearchResponse>(response, cancellationToken).ConfigureAwait(false);
        if (result == null)
        {
            return new PostmarkSearchLookup(PostmarkQueryStatus.Error, null, "Failed to parse Postmark response.");
        }

        if (result.TotalCount <= 0 || result.Messages == null || result.Messages.Count == 0)
        {
            return new PostmarkSearchLookup(PostmarkQueryStatus.NotFound, result, null);
        }

        return new PostmarkSearchLookup(PostmarkQueryStatus.Found, result, null);
    }

    private Uri BuildDetailsUri(string messageId)
    {
        var baseUrl = options.BaseUrl.ToString().TrimEnd('/') + "/";
        var path = $"messages/outbound/{Uri.EscapeDataString(messageId)}/details";
        return new Uri(new Uri(baseUrl), path);
    }

    private Uri BuildSearchUri(string metadataKey, string metadataValue)
    {
        var baseUrl = options.BaseUrl.ToString().TrimEnd('/') + "/";
        var path = "messages/outbound";
        var builder = new UriBuilder(new Uri(new Uri(baseUrl), path))
        {
            Query = $"count=1&offset=0&metadata_{Uri.EscapeDataString(metadataKey)}={Uri.EscapeDataString(metadataValue)}"
        };
        return builder.Uri;
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> DeserializeAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content == null)
        {
            return default;
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Postmark query status.
    /// </summary>
    public enum PostmarkQueryStatus
    {
        Found = 0,
        NotFound = 1,
        Error = 2,
    }

    /// <summary>
    /// Result for a message details query.
    /// </summary>
    public sealed record PostmarkMessageDetailsLookup(
        PostmarkQueryStatus Status,
        PostmarkOutboundMessageDetails? Details,
        string? Error)
    {
        public bool IsFound => Status == PostmarkQueryStatus.Found;
    }

    /// <summary>
    /// Result for a metadata search query.
    /// </summary>
    public sealed record PostmarkSearchLookup(
        PostmarkQueryStatus Status,
        PostmarkOutboundSearchResponse? Response,
        string? Error)
    {
        public bool IsFound => Status == PostmarkQueryStatus.Found;
    }

    /// <summary>
    /// Postmark outbound message details.
    /// </summary>
    public sealed record PostmarkOutboundMessageDetails
    {
        [JsonPropertyName("MessageID")]
        public string? MessageId { get; init; }

        [JsonPropertyName("Status")]
        public string? Status { get; init; }

        [JsonPropertyName("Metadata")]
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }
    }

    /// <summary>
    /// Postmark outbound search response.
    /// </summary>
    public sealed record PostmarkOutboundSearchResponse
    {
        [JsonPropertyName("TotalCount")]
        public int TotalCount { get; init; }

        [JsonPropertyName("Messages")]
        public IReadOnlyList<PostmarkOutboundSearchMessage>? Messages { get; init; }
    }

    /// <summary>
    /// Postmark outbound search message.
    /// </summary>
    public sealed record PostmarkOutboundSearchMessage
    {
        [JsonPropertyName("MessageID")]
        public string? MessageId { get; init; }

        [JsonPropertyName("Status")]
        public string? Status { get; init; }
    }
}

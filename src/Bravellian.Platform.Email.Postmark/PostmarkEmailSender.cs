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
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bravellian.Platform.Email;

namespace Bravellian.Platform.Email.Postmark;

/// <summary>
/// Postmark implementation of <see cref="IOutboundEmailSender"/>.
/// </summary>
public sealed class PostmarkEmailSender : IOutboundEmailSender
{
    private const string MessageKeyHeader = "X-Message-Key";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient httpClient;
    private readonly PostmarkOptions options;
    private readonly IPostmarkEmailValidator validator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkEmailSender"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="options">Postmark options.</param>
    public PostmarkEmailSender(HttpClient httpClient, PostmarkOptions options)
        : this(httpClient, options, new PostmarkEmailValidator())
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostmarkEmailSender"/> class.
    /// </summary>
    /// <param name="httpClient">HTTP client.</param>
    /// <param name="options">Postmark options.</param>
    /// <param name="validator">Postmark validator.</param>
    public PostmarkEmailSender(HttpClient httpClient, PostmarkOptions options, IPostmarkEmailValidator validator)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);

        this.httpClient = httpClient;
        this.options = options;
        this.validator = validator;
        this.options.Validate();

        if (this.httpClient.BaseAddress == null)
        {
            this.httpClient.BaseAddress = this.options.BaseUrl;
        }
    }

    /// <inheritdoc />
    public async Task<EmailSendResult> SendAsync(OutboundEmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var validation = validator.Validate(message);
        if (!validation.Succeeded)
        {
            return EmailSendResult.PermanentFailure("validation", string.Join(" ", validation.Errors));
        }

        var payload = BuildRequest(message, options.MessageStream);
        var requestJson = JsonSerializer.Serialize(payload, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "email")
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        httpRequest.Headers.Add("X-Postmark-Server-Token", options.ServerToken);

        try
        {
            using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            var responseBody = response.Content == null
                ? null
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return EmailSendResult.Success(TryExtractMessageId(responseBody));
            }

            var failureReason = TryExtractError(responseBody) ?? $"Postmark request failed with {(int)response.StatusCode}.";
            return IsTransientStatus(response.StatusCode)
                ? EmailSendResult.TransientFailure(response.StatusCode.ToString(), failureReason)
                : EmailSendResult.PermanentFailure(response.StatusCode.ToString(), failureReason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return EmailSendResult.TransientFailure(null, "Postmark request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return EmailSendResult.TransientFailure(null, ex.Message);
        }
    }

    private static PostmarkEmailRequest BuildRequest(OutboundEmailMessage message, string? messageStream)
    {
        var headers = BuildHeaders(message.Headers, message.MessageKey);
        var metadata = BuildMetadata(message.Metadata, message.MessageKey);

        return new PostmarkEmailRequest
        {
            From = message.From.ToString(),
            To = JoinAddresses(message.To),
            Cc = message.Cc.Count == 0 ? null : JoinAddresses(message.Cc),
            Bcc = message.Bcc.Count == 0 ? null : JoinAddresses(message.Bcc),
            Subject = message.Subject,
            TextBody = message.TextBody,
            HtmlBody = message.HtmlBody,
            ReplyTo = message.ReplyTo?.ToString(),
            Tag = message.Tags.Count == 0 ? null : message.Tags[0],
            MessageStream = string.IsNullOrWhiteSpace(messageStream) ? null : messageStream,
            Headers = headers,
            Metadata = metadata.Count == 0 ? null : metadata,
            Attachments = MapAttachments(message.Attachments)
        };
    }

    private static IReadOnlyList<PostmarkHeader>? BuildHeaders(
        IReadOnlyDictionary<string, string> headers,
        string messageKey)
    {
        if (headers.Count == 0)
        {
            return new[] { new PostmarkHeader(MessageKeyHeader, messageKey) };
        }

        var list = headers.Select(header => new PostmarkHeader(header.Key, header.Value)).ToList();
        if (!headers.ContainsKey(MessageKeyHeader))
        {
            list.Add(new PostmarkHeader(MessageKeyHeader, messageKey));
        }

        return list;
    }

    private static Dictionary<string, string> BuildMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string messageKey)
    {
        var result = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        if (!result.ContainsKey("MessageKey"))
        {
            result["MessageKey"] = messageKey;
        }

        return result;
    }

    private static string JoinAddresses(IReadOnlyList<EmailAddress> addresses)
    {
        return string.Join(", ", addresses.Select(address => address.ToString()));
    }

    private static PostmarkAttachment[]? MapAttachments(IReadOnlyList<EmailAttachment> attachments)
    {
        if (attachments.Count == 0)
        {
            return null;
        }

        return attachments
            .Select(attachment => new PostmarkAttachment(
                attachment.FileName,
                Convert.ToBase64String(attachment.ContentBytes),
                attachment.ContentType,
                attachment.ContentId))
            .ToArray();
    }

    private static string? TryExtractMessageId(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var response = JsonSerializer.Deserialize<PostmarkSendResponse>(responseBody, SerializerOptions);
            return response?.MessageId;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryExtractError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            var response = JsonSerializer.Deserialize<PostmarkErrorResponse>(responseBody, SerializerOptions);
            return response?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500;
    }

    private sealed record PostmarkEmailRequest
    {
        [JsonPropertyName("From")]
        public string? From { get; init; }

        [JsonPropertyName("To")]
        public string? To { get; init; }

        [JsonPropertyName("Cc")]
        public string? Cc { get; init; }

        [JsonPropertyName("Bcc")]
        public string? Bcc { get; init; }

        [JsonPropertyName("Subject")]
        public string? Subject { get; init; }

        [JsonPropertyName("TextBody")]
        public string? TextBody { get; init; }

        [JsonPropertyName("HtmlBody")]
        public string? HtmlBody { get; init; }

        [JsonPropertyName("ReplyTo")]
        public string? ReplyTo { get; init; }

        [JsonPropertyName("Tag")]
        public string? Tag { get; init; }

        [JsonPropertyName("MessageStream")]
        public string? MessageStream { get; init; }

        [JsonPropertyName("Headers")]
        public IReadOnlyList<PostmarkHeader>? Headers { get; init; }

        [JsonPropertyName("Metadata")]
        public IReadOnlyDictionary<string, string>? Metadata { get; init; }

        [JsonPropertyName("Attachments")]
        public IReadOnlyList<PostmarkAttachment>? Attachments { get; init; }
    }

    private sealed record PostmarkHeader(
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("Value")] string Value);

    private sealed record PostmarkAttachment(
        [property: JsonPropertyName("Name")] string Name,
        [property: JsonPropertyName("Content")] string Content,
        [property: JsonPropertyName("ContentType")] string ContentType,
        [property: JsonPropertyName("ContentID")] string? ContentId);

    private sealed record PostmarkSendResponse(
        [property: JsonPropertyName("MessageID")] string? MessageId);

    private sealed record PostmarkErrorResponse(
        [property: JsonPropertyName("Message")] string? Message,
        [property: JsonPropertyName("ErrorCode")] int ErrorCode);
}

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

namespace Bravellian.Platform.Email.Postmark;

internal static class PostmarkWebhookParser
{
    private const string MessageKeyHeader = "X-Message-Key";

    public static bool TryParse(byte[] bodyBytes, out PostmarkWebhookPayload payload, out string? error)
    {
        payload = null!;
        error = null;

        try
        {
            using var document = JsonDocument.Parse(bodyBytes);
            var root = document.RootElement;

            var recordType = GetString(root, "RecordType") ?? GetString(root, "EventType");
            var eventId = GetString(root, "ID") ?? GetString(root, "EventID") ?? GetString(root, "EventId");
            var messageId = GetString(root, "MessageID") ?? GetString(root, "MessageId");
            var messageKey = GetMessageKey(root);
            var bounceType = GetString(root, "Type");
            var description = GetString(root, "Description") ?? GetString(root, "Details");

            payload = new PostmarkWebhookPayload(
                recordType,
                eventId,
                messageId,
                messageKey,
                bounceType,
                description);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? GetMessageKey(JsonElement root)
    {
        if (TryGetPropertyIgnoreCase(root, "Metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            var metadataKey = GetString(metadata, "MessageKey") ?? GetString(metadata, "messageKey");
            if (!string.IsNullOrWhiteSpace(metadataKey))
            {
                return metadataKey;
            }
        }

        if (TryGetPropertyIgnoreCase(root, "Headers", out var headers))
        {
            var headerValue = GetHeaderValue(headers, MessageKeyHeader);
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }
        }

        if (TryGetPropertyIgnoreCase(root, "MessageHeaders", out var messageHeaders))
        {
            var headerValue = GetHeaderValue(messageHeaders, MessageKeyHeader);
            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                return headerValue;
            }
        }

        return null;
    }

    private static string? GetHeaderValue(JsonElement element, string headerName)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    return GetValueAsString(property.Value);
                }
            }
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var name = GetString(item, "Name") ?? GetString(item, "Header") ?? GetString(item, "Key");
                if (!string.Equals(name, headerName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = GetString(item, "Value") ?? GetString(item, "HeaderValue");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var value))
        {
            return null;
        }

        return GetValueAsString(value);
    }

    private static string? GetValueAsString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}

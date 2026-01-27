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

namespace Bravellian.Platform.Correlation;

/// <summary>
/// Default serializer for correlation metadata.
/// </summary>
public sealed class DefaultCorrelationSerializer : ICorrelationSerializer
{
    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Serialize(CorrelationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CorrelationHeaders.CorrelationId] = context.CorrelationId.Value,
            [CorrelationHeaders.CreatedAtUtc] = context.CreatedAtUtc.ToString("O", CultureInfo.InvariantCulture)
        };

        if (context.CausationId is not null)
        {
            values[CorrelationHeaders.CausationId] = context.CausationId.Value.Value;
        }

        if (!string.IsNullOrWhiteSpace(context.TraceId))
        {
            values[CorrelationHeaders.TraceId] = context.TraceId!;
        }

        if (!string.IsNullOrWhiteSpace(context.SpanId))
        {
            values[CorrelationHeaders.SpanId] = context.SpanId!;
        }

        if (context.Tags is null)
        {
            return values;
        }

        foreach (var tag in context.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Key) || tag.Value is null)
            {
                continue;
            }

            values[$"{CorrelationHeaders.TagPrefix}{tag.Key}"] = tag.Value;
        }

        return values;
    }

    /// <inheritdoc />
    public CorrelationContext? Deserialize(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        if (!TryGetValue(values, CorrelationHeaders.CorrelationId, out var correlationValue)
            || !CorrelationId.TryParse(correlationValue, out var correlationId))
        {
            return null;
        }

        CorrelationId? causationId = null;
        if (TryGetValue(values, CorrelationHeaders.CausationId, out var causationValue))
        {
            if (!CorrelationId.TryParse(causationValue, out var parsedCausation))
            {
                return null;
            }

            causationId = parsedCausation;
        }

        _ = TryGetValue(values, CorrelationHeaders.TraceId, out var traceId);
        _ = TryGetValue(values, CorrelationHeaders.SpanId, out var spanId);

        var createdAtUtc = DateTimeOffset.UnixEpoch;
        if (TryGetValue(values, CorrelationHeaders.CreatedAtUtc, out var createdAtValue)
            && DateTimeOffset.TryParse(createdAtValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedCreatedAt))
        {
            createdAtUtc = parsedCreatedAt;
        }

        Dictionary<string, string>? tags = null;
        foreach (var item in values)
        {
            if (!item.Key.StartsWith(CorrelationHeaders.TagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = item.Key[CorrelationHeaders.TagPrefix.Length..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            tags ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            tags[key] = item.Value;
        }

        return new CorrelationContext(
            correlationId,
            causationId,
            string.IsNullOrWhiteSpace(traceId) ? null : traceId,
            string.IsNullOrWhiteSpace(spanId) ? null : spanId,
            createdAtUtc,
            tags);
    }

    private static bool TryGetValue(IReadOnlyDictionary<string, string> values, string key, out string? value)
    {
        if (values.TryGetValue(key, out var directValue))
        {
            value = directValue;
            return true;
        }

        foreach (var item in values)
        {
            if (!string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            value = item.Value;
            return true;
        }

        value = null;
        return false;
    }
}

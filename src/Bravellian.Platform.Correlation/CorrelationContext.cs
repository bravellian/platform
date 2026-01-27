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

namespace Bravellian.Platform.Correlation;

/// <summary>
/// Represents correlation identifiers for a single logical flow.
/// </summary>
public sealed record CorrelationContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationContext"/> class.
    /// </summary>
    /// <param name="correlationId">Primary correlation identifier.</param>
    /// <param name="causationId">Optional causation identifier.</param>
    /// <param name="traceId">Optional trace identifier.</param>
    /// <param name="spanId">Optional span identifier.</param>
    /// <param name="createdAtUtc">Timestamp when the context was created (UTC).</param>
    /// <param name="tags">Optional tags associated with the context.</param>
    public CorrelationContext(
        CorrelationId correlationId,
        CorrelationId? causationId,
        string? traceId,
        string? spanId,
        DateTimeOffset createdAtUtc,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        CorrelationId = correlationId;
        CausationId = causationId;
        TraceId = traceId;
        SpanId = spanId;
        CreatedAtUtc = createdAtUtc;
        Tags = tags;
    }

    /// <summary>
    /// Gets the primary correlation identifier.
    /// </summary>
    public CorrelationId CorrelationId { get; }

    /// <summary>
    /// Gets the optional causation identifier.
    /// </summary>
    public CorrelationId? CausationId { get; }

    /// <summary>
    /// Gets the optional trace identifier.
    /// </summary>
    public string? TraceId { get; }

    /// <summary>
    /// Gets the optional span identifier.
    /// </summary>
    public string? SpanId { get; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>
    /// Gets the optional tags associated with the context.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; }
}
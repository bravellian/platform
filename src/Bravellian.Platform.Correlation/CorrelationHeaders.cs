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
/// Defines header keys used for correlation metadata.
/// </summary>
public static class CorrelationHeaders
{
    /// <summary>
    /// Header name for the correlation identifier.
    /// </summary>
    public const string CorrelationId = "X-Correlation-Id";

    /// <summary>
    /// Header name for the causation identifier.
    /// </summary>
    public const string CausationId = "X-Causation-Id";

    /// <summary>
    /// Header name for the trace identifier.
    /// </summary>
    public const string TraceId = "X-Trace-Id";

    /// <summary>
    /// Header name for the span identifier.
    /// </summary>
    public const string SpanId = "X-Span-Id";

    /// <summary>
    /// Header name for the correlation creation timestamp in UTC.
    /// </summary>
    public const string CreatedAtUtc = "X-Correlation-Created-At";

    /// <summary>
    /// Prefix for correlation tag headers.
    /// </summary>
    public const string TagPrefix = "X-Correlation-Tag-";
}
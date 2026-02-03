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

namespace Bravellian.Platform.Audit;

/// <summary>
/// Defines a minimal audit event query.
/// </summary>
public sealed record AuditQuery
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditQuery"/> record.
    /// </summary>
    /// <param name="anchors">Anchors to match.</param>
    /// <param name="fromUtc">Optional start time (UTC).</param>
    /// <param name="toUtc">Optional end time (UTC).</param>
    /// <param name="name">Optional event name filter.</param>
    /// <param name="limit">Optional limit.</param>
    public AuditQuery(
        IReadOnlyList<EventAnchor> anchors,
        DateTimeOffset? fromUtc = null,
        DateTimeOffset? toUtc = null,
        string? name = null,
        int? limit = null)
    {
        Anchors = anchors ?? throw new ArgumentNullException(nameof(anchors));
        FromUtc = fromUtc;
        ToUtc = toUtc;
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Limit = limit;
    }

    /// <summary>
    /// Gets the anchors to match.
    /// </summary>
    public IReadOnlyList<EventAnchor> Anchors { get; }

    /// <summary>
    /// Gets the optional start time filter (UTC).
    /// </summary>
    public DateTimeOffset? FromUtc { get; }

    /// <summary>
    /// Gets the optional end time filter (UTC).
    /// </summary>
    public DateTimeOffset? ToUtc { get; }

    /// <summary>
    /// Gets the optional event name filter.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the optional limit.
    /// </summary>
    public int? Limit { get; }
}

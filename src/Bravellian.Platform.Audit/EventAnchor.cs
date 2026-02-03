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
/// Describes a stable anchor for querying audit events.
/// </summary>
public sealed record EventAnchor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventAnchor"/> record.
    /// </summary>
    /// <param name="anchorType">Anchor type (e.g., Tenant, Invoice, Job).</param>
    /// <param name="anchorId">Anchor identifier.</param>
    /// <param name="role">Anchor role (e.g., Subject, Owner, Participant).</param>
    public EventAnchor(string anchorType, string anchorId, string role)
    {
        if (string.IsNullOrWhiteSpace(anchorType))
        {
            throw new ArgumentException("Anchor type is required.", nameof(anchorType));
        }

        if (string.IsNullOrWhiteSpace(anchorId))
        {
            throw new ArgumentException("Anchor id is required.", nameof(anchorId));
        }

        if (string.IsNullOrWhiteSpace(role))
        {
            throw new ArgumentException("Anchor role is required.", nameof(role));
        }

        AnchorType = anchorType.Trim();
        AnchorId = anchorId.Trim();
        Role = role.Trim();
    }

    /// <summary>
    /// Gets the anchor type.
    /// </summary>
    public string AnchorType { get; }

    /// <summary>
    /// Gets the anchor identifier.
    /// </summary>
    public string AnchorId { get; }

    /// <summary>
    /// Gets the anchor role.
    /// </summary>
    public string Role { get; }
}

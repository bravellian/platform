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
/// Represents the identifier of an audit event.
/// </summary>
public readonly record struct AuditEventId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AuditEventId"/> struct.
    /// </summary>
    /// <param name="value">Audit event identifier value.</param>
    /// <exception cref="ArgumentException">Thrown when value is null or whitespace.</exception>
    public AuditEventId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("AuditEventId value is required.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the audit event identifier value.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a new audit event identifier.
    /// </summary>
    /// <returns>New audit event identifier.</returns>
    public static AuditEventId NewId()
    {
        return new AuditEventId(Guid.NewGuid().ToString("n"));
    }

    /// <summary>
    /// Attempts to parse an audit event identifier from a string.
    /// </summary>
    /// <param name="value">String value to parse.</param>
    /// <param name="eventId">Parsed audit event identifier.</param>
    /// <returns><c>true</c> if parsing succeeds; otherwise <c>false</c>.</returns>
    public static bool TryParse(string? value, out AuditEventId eventId)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            eventId = default;
            return false;
        }

        eventId = new AuditEventId(value.Trim());
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}

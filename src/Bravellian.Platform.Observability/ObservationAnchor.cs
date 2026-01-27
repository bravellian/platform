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

namespace Bravellian.Platform.Observability;

/// <summary>
/// Represents an anchor for linking observability records.
/// </summary>
/// <param name="Type">Anchor type identifier.</param>
/// <param name="Value">Anchor value.</param>
public sealed record ObservationAnchor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ObservationAnchor"/> record.
    /// </summary>
    /// <param name="type">Anchor type identifier.</param>
    /// <param name="value">Anchor value.</param>
    public ObservationAnchor(string type, string value)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Anchor type is required.", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Anchor value is required.", nameof(value));
        }

        Type = type;
        Value = value;
    }

    /// <summary>
    /// Gets the anchor type identifier.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Gets the anchor value.
    /// </summary>
    public string Value { get; }
}

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

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Bravellian.Platform;

/// <summary>
/// Strongly-typed identifier for an outbox join.
/// </summary>
[TypeConverter(typeof(JoinIdTypeConverter))]
[JsonConverter(typeof(JoinIdJsonConverter))]
public readonly record struct JoinId
{
    // Static constructor to ensure Dapper type handler is registered
    static JoinId()
    {
        JoinIdTypeHandler.Register();
    }

    private readonly Guid value;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinId"/> struct.
    /// </summary>
    /// <param name="value">The underlying GUID value.</param>
    public JoinId(Guid value)
    {
        this.value = value;
    }

    /// <summary>
    /// Gets the underlying GUID value.
    /// </summary>
    public Guid Value => value;

    /// <summary>
    /// Creates a new <see cref="JoinId"/> with a new GUID.
    /// </summary>
    /// <returns>A new <see cref="JoinId"/>.</returns>
    public static JoinId NewJoinId() => new(Guid.NewGuid());

    /// <summary>
    /// Implicitly converts a <see cref="JoinId"/> to a <see cref="Guid"/>.
    /// </summary>
    /// <param name="joinId">The join ID to convert.</param>
    public static implicit operator Guid(JoinId joinId) => joinId.value;

    /// <summary>
    /// Implicitly converts a <see cref="Guid"/> to a <see cref="JoinId"/>.
    /// </summary>
    /// <param name="value">The GUID to convert.</param>
    public static implicit operator JoinId(Guid value) => new(value);

    /// <summary>
    /// Returns the string representation of this join ID.
    /// </summary>
    /// <returns>The string representation of the underlying GUID.</returns>
    public override string ToString() => value.ToString();
}

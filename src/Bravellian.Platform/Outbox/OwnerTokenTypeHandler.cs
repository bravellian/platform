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

using System.Data;
using Dapper;

namespace Bravellian.Platform;

/// <summary>
/// Dapper type handler for <see cref="OwnerToken"/>.
/// </summary>
internal sealed class OwnerTokenTypeHandler : SqlMapper.TypeHandler<OwnerToken>
{
    /// <summary>
    /// Static constructor to register the type handler with Dapper.
    /// </summary>
    static OwnerTokenTypeHandler()
    {
        SqlMapper.AddTypeHandler(new OwnerTokenTypeHandler());
    }

    /// <summary>
    /// Ensures the type handler is registered. Call this method at application startup.
    /// </summary>
    internal static void Register()
    {
        // The static constructor will be called automatically
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, OwnerToken value)
    {
        parameter.Value = value.Value;
        parameter.DbType = DbType.Guid;
    }

    /// <inheritdoc/>
    public override OwnerToken Parse(object value)
    {
        return value switch
        {
            Guid g => new OwnerToken(g),
            string s => new OwnerToken(Guid.Parse(s)),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType()} to OwnerToken")
        };
    }
}

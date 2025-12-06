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
/// Dapper type handler for <see cref="JoinId"/>.
/// </summary>
internal sealed class JoinIdTypeHandler : SqlMapper.TypeHandler<JoinId>
{
    /// <summary>
    /// Static constructor to register the type handler with Dapper.
    /// </summary>
    static JoinIdTypeHandler()
    {
        SqlMapper.AddTypeHandler(new JoinIdTypeHandler());
    }

    /// <summary>
    /// Ensures the type handler is registered. Call this method at application startup.
    /// </summary>
    internal static void Register()
    {
        // The static constructor will be called automatically
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, JoinId value)
    {
        parameter.Value = value.Value;
        parameter.DbType = DbType.Guid;
    }

    /// <inheritdoc/>
    public override JoinId Parse(object value)
    {
        return value switch
        {
            Guid g => new JoinId(g),
            string s => new JoinId(Guid.Parse(s)),
            _ => throw new InvalidCastException($"Cannot convert {value?.GetType()} to JoinId")
        };
    }
}

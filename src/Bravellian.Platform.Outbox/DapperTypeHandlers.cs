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

#pragma warning disable MA0048 // File name must match type name - intentionally grouping all type handlers

using System.Data;
using Dapper;

namespace Bravellian.Platform.Outbox;

/// <summary>
/// Dapper type handler for OutboxMessageIdentifier to convert between Guid and OutboxMessageIdentifier.
/// </summary>
internal sealed class OutboxMessageIdentifierTypeHandler : SqlMapper.TypeHandler<OutboxMessageIdentifier>
{
    /// <inheritdoc/>
    public override OutboxMessageIdentifier Parse(object value)
    {
        return value is Guid guid ? OutboxMessageIdentifier.From(guid) : default;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, OutboxMessageIdentifier value)
    {
        parameter.Value = value.Value;
        parameter.DbType = DbType.Guid;
    }
}

/// <summary>
/// Dapper type handler for nullable OutboxMessageIdentifier to convert between Guid? and OutboxMessageIdentifier?.
/// </summary>
internal sealed class NullableOutboxMessageIdentifierTypeHandler : SqlMapper.TypeHandler<OutboxMessageIdentifier?>
{
    /// <inheritdoc/>
    public override OutboxMessageIdentifier? Parse(object value)
    {
        return value is Guid guid ? OutboxMessageIdentifier.From(guid) : null;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, OutboxMessageIdentifier? value)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value.Value;
            parameter.DbType = DbType.Guid;
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}

/// <summary>
/// Dapper type handler for OutboxWorkItemIdentifier to convert between Guid and OutboxWorkItemIdentifier.
/// </summary>
internal sealed class OutboxWorkItemIdentifierTypeHandler : SqlMapper.TypeHandler<OutboxWorkItemIdentifier>
{
    /// <inheritdoc/>
    public override OutboxWorkItemIdentifier Parse(object value)
    {
        return value is Guid guid ? OutboxWorkItemIdentifier.From(guid) : default;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, OutboxWorkItemIdentifier value)
    {
        parameter.Value = value.Value;
        parameter.DbType = DbType.Guid;
    }
}

/// <summary>
/// Dapper type handler for nullable OutboxWorkItemIdentifier to convert between Guid? and OutboxWorkItemIdentifier?.
/// </summary>
internal sealed class NullableOutboxWorkItemIdentifierTypeHandler : SqlMapper.TypeHandler<OutboxWorkItemIdentifier?>
{
    /// <inheritdoc/>
    public override OutboxWorkItemIdentifier? Parse(object value)
    {
        return value is Guid guid ? OutboxWorkItemIdentifier.From(guid) : null;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, OutboxWorkItemIdentifier? value)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value.Value;
            parameter.DbType = DbType.Guid;
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}

/// <summary>
/// Dapper type handler for JoinIdentifier to convert between Guid and JoinIdentifier.
/// </summary>
internal sealed class JoinIdentifierTypeHandler : SqlMapper.TypeHandler<JoinIdentifier>
{
    /// <inheritdoc/>
    public override JoinIdentifier Parse(object value)
    {
        return value is Guid guid ? JoinIdentifier.From(guid) : default;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, JoinIdentifier value)
    {
        parameter.Value = value.Value;
        parameter.DbType = DbType.Guid;
    }
}

/// <summary>
/// Dapper type handler for nullable JoinIdentifier to convert between Guid? and JoinIdentifier?.
/// </summary>
internal sealed class NullableJoinIdentifierTypeHandler : SqlMapper.TypeHandler<JoinIdentifier?>
{
    /// <inheritdoc/>
    public override JoinIdentifier? Parse(object value)
    {
        return value is Guid guid ? JoinIdentifier.From(guid) : null;
    }

    /// <inheritdoc/>
    public override void SetValue(IDbDataParameter parameter, JoinIdentifier? value)
    {
        if (value.HasValue)
        {
            parameter.Value = value.Value.Value;
            parameter.DbType = DbType.Guid;
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}


/// <summary>
/// Provides registration methods for all Dapper type handlers for strongly-typed IDs.
/// </summary>
public static class DapperTypeHandlerRegistration
{
    private static bool registered = false;
    private static readonly Lock lockObject = new();

    /// <summary>
    /// Registers all Dapper type handlers for strongly-typed ID types.
    /// This method is idempotent and thread-safe.
    /// </summary>
    public static void RegisterTypeHandlers()
    {
        lock (lockObject)
        {
            if (registered)
            {
                return;
            }

            // Register OutboxMessageIdentifier handlers
            SqlMapper.AddTypeHandler(new OutboxMessageIdentifierTypeHandler());
            SqlMapper.AddTypeHandler(new NullableOutboxMessageIdentifierTypeHandler());

            // Register OutboxWorkItemIdentifier handlers
            SqlMapper.AddTypeHandler(new OutboxWorkItemIdentifierTypeHandler());
            SqlMapper.AddTypeHandler(new NullableOutboxWorkItemIdentifierTypeHandler());

            // Register JoinIdentifier handlers
            SqlMapper.AddTypeHandler(new JoinIdentifierTypeHandler());
            SqlMapper.AddTypeHandler(new NullableJoinIdentifierTypeHandler());

            registered = true;
        }
    }
}

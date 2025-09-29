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

namespace Bravellian.Platform;

/// <summary>
/// Work queue interface for Outbox messages.
/// </summary>
public interface IOutboxWorkQueue : IWorkQueue<Guid>
{
}

/// <summary>
/// Work queue interface for Timer items.
/// </summary>
public interface ITimerWorkQueue : IWorkQueue<Guid>
{
}

/// <summary>
/// SQL Server implementation of Outbox work queue.
/// </summary>
internal class SqlOutboxWorkQueue : SqlGuidWorkQueue, IOutboxWorkQueue
{
    public SqlOutboxWorkQueue(SqlOutboxOptions options)
        : base(options.ConnectionString, options.SchemaName, options.TableName, isScheduled: false)
    {
    }
}

/// <summary>
/// SQL Server implementation of Timer work queue.
/// </summary>
internal class SqlTimerWorkQueue : SqlGuidWorkQueue, ITimerWorkQueue
{
    public SqlTimerWorkQueue(string connectionString, string schemaName = "dbo", string tableName = "Timers")
        : base(connectionString, schemaName, tableName, isScheduled: true)
    {
    }
}
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

using System.Data;

/// <summary>
/// Work queue client for the Outbox table using GUID identifiers.
/// </summary>
public class OutboxWorkQueueClient : WorkQueueClientBase<Guid>
{
    public OutboxWorkQueueClient(string connectionString)
        : base(new WorkQueueOptions
        {
            ConnectionString = connectionString,
            SchemaName = "dbo",
            TableName = "Outbox",
            PrimaryKeyColumn = "Id",
            OrderingColumn = "CreatedAt",
            ProcedureNamePrefix = "dbo.Outbox",
        })
    {
    }

    public OutboxWorkQueueClient(WorkQueueOptions options)
        : base(options)
    {
    }

    protected override Guid ConvertFromDatabase(object dbValue)
    {
        return (Guid)dbValue;
    }

    protected override string GetTableValuedParameterTypeName()
    {
        return "dbo.GuidIdList";
    }

    protected override DataTable CreateIdDataTable(IEnumerable<Guid> ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        
        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }
        
        return table;
    }
}

/// <summary>
/// Work queue client for the Timers table using GUID identifiers.
/// </summary>
public class TimersWorkQueueClient : WorkQueueClientBase<Guid>
{
    public TimersWorkQueueClient(string connectionString)
        : base(new WorkQueueOptions
        {
            ConnectionString = connectionString,
            SchemaName = "dbo",
            TableName = "Timers",
            PrimaryKeyColumn = "Id",
            OrderingColumn = "DueTime",
            ProcedureNamePrefix = "dbo.Timers",
        })
    {
    }

    public TimersWorkQueueClient(WorkQueueOptions options)
        : base(options)
    {
    }

    protected override Guid ConvertFromDatabase(object dbValue)
    {
        return (Guid)dbValue;
    }

    protected override string GetTableValuedParameterTypeName()
    {
        return "dbo.GuidIdList";
    }

    protected override DataTable CreateIdDataTable(IEnumerable<Guid> ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        
        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }
        
        return table;
    }
}

/// <summary>
/// Work queue client for the JobRuns table using GUID identifiers.
/// </summary>
public class JobRunsWorkQueueClient : WorkQueueClientBase<Guid>
{
    public JobRunsWorkQueueClient(string connectionString)
        : base(new WorkQueueOptions
        {
            ConnectionString = connectionString,
            SchemaName = "dbo",
            TableName = "JobRuns",
            PrimaryKeyColumn = "Id",
            OrderingColumn = "ScheduledTime",
            ProcedureNamePrefix = "dbo.JobRuns",
        })
    {
    }

    public JobRunsWorkQueueClient(WorkQueueOptions options)
        : base(options)
    {
    }

    protected override Guid ConvertFromDatabase(object dbValue)
    {
        return (Guid)dbValue;
    }

    protected override string GetTableValuedParameterTypeName()
    {
        return "dbo.GuidIdList";
    }

    protected override DataTable CreateIdDataTable(IEnumerable<Guid> ids)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        
        foreach (var id in ids)
        {
            table.Rows.Add(id);
        }
        
        return table;
    }
}
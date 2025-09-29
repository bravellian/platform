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

using Dapper;
using Microsoft.Extensions.Options;
using System.Data;
using System.Threading.Tasks;

internal class SqlOutboxService : IOutbox
{
    private readonly SqlOutboxOptions options;
    private readonly string enqueueSql;

    public SqlOutboxService(IOptions<SqlOutboxOptions> options)
    {
        this.options = options.Value;
        
        // Build the SQL query using configured schema and table names
        this.enqueueSql = $@"
            INSERT INTO [{this.options.SchemaName}].[{this.options.TableName}] (Topic, Payload, CorrelationId, MessageId)
            VALUES (@Topic, @Payload, @CorrelationId, NEWID());";
    }

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null)
    {
        // Note: We use the connection from the provided transaction.
        await transaction.Connection.ExecuteAsync(this.enqueueSql, new
        {
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
        }, transaction: transaction).ConfigureAwait(false);
    }
}

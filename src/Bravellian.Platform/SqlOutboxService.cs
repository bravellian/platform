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
using System.Data;
using System.Threading.Tasks;

internal class SqlOutboxService : IOutbox
{
    // The SQL uses the table schema we defined previously.
    private const string EnqueueSql = @"
            INSERT INTO dbo.Outbox (Topic, Payload, CorrelationId, MessageId)
            VALUES (@Topic, @Payload, @CorrelationId, NEWID());";

    public async Task EnqueueAsync(
        string topic,
        string payload,
        IDbTransaction transaction,
        string? correlationId = null)
    {
        // Note: We use the connection from the provided transaction.
        await transaction.Connection.ExecuteAsync(EnqueueSql, new
        {
            Topic = topic,
            Payload = payload,
            CorrelationId = correlationId,
        }, transaction: transaction).ConfigureAwait(false);
    }
}

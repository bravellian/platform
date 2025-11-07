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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Threading.Tasks;

internal class SqlOutboxService : IOutbox
{
    private readonly IOutboxStoreProvider storeProvider;
    private readonly ILogger<SqlOutboxService> logger;

    public SqlOutboxService(IOutboxStoreProvider storeProvider, ILogger<SqlOutboxService> logger)
    {
        this.storeProvider = storeProvider;
        this.logger = logger;
    }

    public Task EnqueueAsync(string topic, string payload, string? correlationId)
    {
        throw new NotSupportedException("This method is not supported when using multiple outbox stores. Please provide a key to identify the target outbox.");
    }

    public Task EnqueueAsync(string topic, string payload, IDbTransaction transaction, string? correlationId = null)
    {
        throw new NotSupportedException("This method is not supported when using multiple outbox stores. Please provide a key to identify the target outbox.");
    }

    public async Task EnqueueAsync(object key, string topic, string payload, string? correlationId)
    {
        var store = this.storeProvider.GetStore(key);
        await store.EnqueueAsync(topic, payload, correlationId).ConfigureAwait(false);
    }

    public async Task EnqueueAsync(object key, string topic, string payload, IDbTransaction transaction, string? correlationId = null)
    {
        var store = this.storeProvider.GetStore(key);
        await store.EnqueueAsync(topic, payload, correlationId, transaction).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Guid>> ClaimAsync(Guid ownerToken, int leaseSeconds, int batchSize, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Claim is not a valid operation for a multi-outbox service. It should be handled by a dispatcher.");
    }

    public Task AckAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Ack is not a valid operation for a multi-outbox service. It should be handled by a dispatcher.");
    }

    public Task AbandonAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Abandon is not a valid operation for a multi-outbox service. It should be handled by a dispatcher.");
    }

    public Task FailAsync(Guid ownerToken, IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Fail is not a valid operation for a multi-outbox service. It should be handled by a dispatcher.");
    }

    public Task ReapExpiredAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("ReapExpired is not a valid operation for a multi-outbox service. It should be handled by a dispatcher.");
    }
}

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
            CorrelationId = correlationId
        }, transaction: transaction).ConfigureAwait(false);
    }
}
using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

/// <summary>
/// Helper to run Postgres schema migrations outside of a host.
/// </summary>
public static class PostgresSchemaMigrator
{
    /// <summary>
    /// Applies the latest platform schema migrations to a Postgres database.
    /// </summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <param name="schemaName">The target schema name (default: "infra").</param>
    /// <param name="includeControlPlaneBundle">Whether to apply control-plane schema bundle to the same database.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ApplyLatestAsync(
        string connectionString,
        string schemaName = "infra",
        bool includeControlPlaneBundle = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must be provided.", nameof(connectionString));
        }

        if (string.IsNullOrWhiteSpace(schemaName))
        {
            throw new ArgumentException("Schema name must be provided.", nameof(schemaName));
        }

        await PostgresSchemaMigrations
            .ApplyTenantBundleAsync(connectionString, schemaName, NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        await PostgresSchemaMigrations
            .ApplyOperationsAsync(connectionString, schemaName, "Operations", "OperationEvents", NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        await PostgresSchemaMigrations
            .ApplyAuditAsync(connectionString, schemaName, "AuditEvents", "AuditAnchors", NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        await PostgresSchemaMigrations
            .ApplyEmailOutboxAsync(connectionString, schemaName, "EmailOutbox", NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        await PostgresSchemaMigrations
            .ApplyEmailDeliveryAsync(connectionString, schemaName, "EmailDeliveryEvents", NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        if (includeControlPlaneBundle)
        {
            await PostgresSchemaMigrations
                .ApplyControlPlaneBundleAsync(connectionString, schemaName, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

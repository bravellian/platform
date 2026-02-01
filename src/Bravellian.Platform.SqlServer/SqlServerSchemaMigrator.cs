using Microsoft.Extensions.Logging.Abstractions;

namespace Bravellian.Platform;

/// <summary>
/// Helper to run SQL Server schema migrations outside of a host.
/// </summary>
public static class SqlServerSchemaMigrator
{
    /// <summary>
    /// Applies the latest platform schema migrations to a SQL Server database.
    /// </summary>
    /// <param name="connectionString">The SQL Server connection string.</param>
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

        await SqlServerSchemaMigrations
            .ApplyTenantBundleAsync(connectionString, schemaName, NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        await SqlServerSchemaMigrations
            .ApplyEmailOutboxAsync(connectionString, schemaName, "EmailOutbox", NullLogger.Instance, cancellationToken)
            .ConfigureAwait(false);

        if (includeControlPlaneBundle)
        {
            await SqlServerSchemaMigrations
                .ApplyControlPlaneBundleAsync(connectionString, schemaName, NullLogger.Instance, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

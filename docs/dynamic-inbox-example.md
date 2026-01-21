# Dynamic Inbox Configuration - Example Implementation

Use this pattern when tenant inbox databases can change at runtime. The inbox dispatcher discovers active databases on a schedule and updates its routing without redeploying.

## Scenario

- A **global catalog** lists tenants and their connection strings.
- Each tenant owns an `Inbox` table with work-queue columns and stored procedures.
- New tenants appear frequently; inactive tenants should stop processing automatically.

## Global catalog schema

```sql
CREATE TABLE Tenants (
    TenantId NVARCHAR(100) PRIMARY KEY,
    ConnectionString NVARCHAR(1000) NOT NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    Region NVARCHAR(50) NULL,
    CreatedAt DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME()
);
```

## Discovery implementation

Implement `IInboxDatabaseDiscovery` to read the catalog and produce `InboxDatabaseConfig` entries.

```csharp
public class GlobalDatabaseInboxDiscovery : IInboxDatabaseDiscovery
{
    private readonly string globalConnectionString;
    private readonly ILogger<GlobalDatabaseInboxDiscovery> logger;

    public GlobalDatabaseInboxDiscovery(IConfiguration configuration, ILogger<GlobalDatabaseInboxDiscovery> logger)
    {
        this.globalConnectionString = configuration.GetConnectionString("GlobalDatabase")
            ?? throw new InvalidOperationException("GlobalDatabase connection string not found");
        this.logger = logger;
    }

    public async Task<IEnumerable<InboxDatabaseConfig>> DiscoverDatabasesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(this.globalConnectionString);
        await connection.OpenAsync(cancellationToken);

        var tenants = await connection.QueryAsync<TenantRecord>(@"
            SELECT TenantId, ConnectionString, Region
            FROM Tenants
            WHERE IsActive = 1", cancellationToken);

        return tenants.Select(t => new InboxDatabaseConfig
        {
            Identifier = t.TenantId,
            ConnectionString = t.ConnectionString,
            SchemaName = "infra",
            TableName = "Inbox",
            Tags = string.IsNullOrWhiteSpace(t.Region) ? Array.Empty<string>() : new[] { t.Region }
        });
    }

    private sealed class TenantRecord
    {
        public string TenantId { get; init; } = string.Empty;
        public string ConnectionString { get; init; } = string.Empty;
        public string? Region { get; init; }
    }
}
```

## Registration

```csharp
services.AddSingleton<IInboxDatabaseDiscovery, GlobalDatabaseInboxDiscovery>();
services.AddDynamicMultiSqlInbox(
    selectionStrategy: new RoundRobinInboxSelectionStrategy(),
    refreshInterval: TimeSpan.FromMinutes(5));
```

- The dispatcher refreshes the set of inbox stores every 5 minutes.
- New tenants start processing automatically; inactive tenants are dropped.
- Selection strategies (round-robin/drain-first) prevent a single tenant from monopolizing the worker.

## Operational tips

- **Warm start**: Call `DiscoverDatabasesAsync` once during startup to validate connectivity.
- **Health checks**: Emit metrics per tenant (`claims`, `acks`, `abandon`) to spot hotspots.
- **Region-aware routing**: Use `Tags` to keep processing local to a region or shard.

## Related guides

- [Inbox Router Guide](InboxRouter.md)
- [Inbox Examples](inbox-examples.md)
- [Work Queue Implementation](work-queue-implementation.md)
- [Multi-Database Pattern](multi-database-pattern.md)

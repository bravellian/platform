# Bravellian Platform Database Project

This SQL Database Project contains the schema definition for the Bravellian Platform persistence layer.

## Structure

```
Schema/
├── Tables/              # Table definitions
├── StoredProcedures/    # Work queue and utility procedures
│   ├── Outbox/
│   ├── Inbox/
│   ├── Timers/
│   ├── JobRuns/
│   ├── Lease/
│   ├── Lock/
│   └── Semaphore/
└── Types/               # User-defined table types

Migrations/              # Version upgrade scripts
Post-Deployment/         # Data initialization scripts
```

## Building

### Command Line
```bash
# Build dacpac
dotnet build Bravellian.Platform.Database.sqlproj

# Output: bin/Debug/Bravellian.Platform.Database.dacpac
```

### Visual Studio / VS Code
- Open `.sqlproj` file
- Build Solution (Ctrl+Shift+B)

## Deployment

### Using SqlPackage
```bash
sqlpackage /Action:Publish \
  /SourceFile:Bravellian.Platform.Database.dacpac \
  /TargetConnectionString:"Server=localhost;Database=MyApp;Integrated Security=true;"
```

### Using Azure DevOps
```yaml
- task: SqlAzureDacpacDeployment@1
  inputs:
    azureSubscription: 'MySubscription'
    ServerName: 'myserver.database.windows.net'
    DatabaseName: 'MyApp'
    DacpacFile: '$(Build.ArtifactStagingDirectory)/Bravellian.Platform.Database.dacpac'
```

### Using GitHub Actions
```yaml
- name: Deploy Database
  uses: Azure/sql-action@v2
  with:
    connection-string: ${{ secrets.SQL_CONNECTION_STRING }}
    path: ./Bravellian.Platform.Database.dacpac
```

## Schema Documentation

Each SQL file includes inline documentation describing:
- Purpose and usage
- Dependencies
- Performance characteristics
- Related objects

Example from `Outbox.sql`:
```sql
/*
 * Outbox Table - Transactional Outbox Pattern
 * 
 * Purpose: Stores outbound messages for reliable publishing
 * Pattern: Work Queue with claim-ack-abandon semantics
 * ...
 */
```

## Version Control

The SQL Project approach provides:

✅ **Clear Diffs**: Git shows actual SQL changes, not C# string edits  
✅ **File-per-Object**: Each table/procedure in its own file  
✅ **Syntax Highlighting**: Full SQL editor support  
✅ **Intellisense**: Schema-aware autocomplete  
✅ **Validation**: Build-time schema validation

## Compatibility

This SQL Project is **optional**. The runtime deployment via `DatabaseSchemaManager` continues to work:

```csharp
// Still works - no changes required
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    EnableSchemaDeployment = true
});
```

Teams can choose:
- **Development**: Use runtime deployment (`EnableSchemaDeployment = true`)
- **Production**: Use dacpac deployment (this SQL Project)
- **Hybrid**: Runtime deployment with validation against SQL Project

## Schema Versions

Current schema version: **2.0.0**

Major schema changes:
- v1.0.0: Initial release
- v1.1.0: Added LastError column to Inbox
- v2.0.0: Added Metrics and Observability tables

See `Migrations/` folder for upgrade scripts.

## Related Documentation

- [Database Schema Strategy](../../docs/DATABASE_SCHEMA_STRATEGY.md) - Overall approach
- [Schema Configuration](../../docs/schema-configuration.md) - Runtime configuration
- [Work Queue Pattern](../../docs/work-queue-pattern.md) - Design patterns

## Support

For issues or questions:
- GitHub Issues: https://github.com/bravellian/platform/issues
- Email: oss@bravellian.com

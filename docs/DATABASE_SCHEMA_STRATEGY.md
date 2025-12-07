# Database Schema Management Strategy

## Executive Summary

After analyzing the current embedded schema approach (2270 lines in `DatabaseSchemaManager.cs`), I recommend a **Hybrid Approach** that combines the benefits of SQL Database Projects with the convenience of runtime schema deployment. This strategy addresses your concerns about reliability while maintaining the ease of development and deployment.

## Current State Assessment

### Strengths of Current Approach
1. **Zero External Dependencies**: No additional files to deploy
2. **Easy Development Setup**: `EnableSchemaDeployment = true` just works
3. **Runtime Verification**: Can validate schema at startup
4. **Atomic Deployment**: Schema code ships with application code
5. **Test Integration**: Works seamlessly with Testcontainers

### Pain Points
1. **Poor Diff Viewing**: C# strings don't show SQL changes well in PRs
2. **No Syntax Highlighting**: Editing SQL in C# strings is error-prone
3. **Version Tracking**: No explicit schema version management
4. **Professional Tooling**: Can't use SSDT, dacpac, or SQL Compare tools
5. **Schema Size**: 2270 lines makes maintenance challenging

## Recommended: Hybrid Approach

### Architecture

```
Platform/
├── src/
│   ├── Bravellian.Platform/           # Core library
│   │   └── DatabaseSchemaManager.cs   # Runtime deployment & validation
│   └── Bravellian.Platform.Database/  # NEW: SQL Database Project
│       ├── Bravellian.Platform.Database.sqlproj
│       ├── Schema/
│       │   ├── Tables/
│       │   │   ├── Outbox.sql
│       │   │   ├── OutboxState.sql
│       │   │   ├── Inbox.sql
│       │   │   ├── Jobs.sql
│       │   │   ├── JobRuns.sql
│       │   │   ├── Timers.sql
│       │   │   ├── SchedulerState.sql
│       │   │   ├── Lease.sql
│       │   │   ├── DistributedLock.sql
│       │   │   ├── FanoutPolicy.sql
│       │   │   ├── FanoutCursor.sql
│       │   │   ├── Semaphore.sql
│       │   │   └── SemaphoreLease.sql
│       │   ├── StoredProcedures/
│       │   │   ├── Outbox/
│       │   │   │   ├── Outbox_Claim.sql
│       │   │   │   ├── Outbox_Ack.sql
│       │   │   │   ├── Outbox_Abandon.sql
│       │   │   │   ├── Outbox_Fail.sql
│       │   │   │   └── Outbox_ReapExpired.sql
│       │   │   ├── Inbox/
│       │   │   ├── Timers/
│       │   │   ├── JobRuns/
│       │   │   ├── Lease/
│       │   │   └── Lock/
│       │   └── Types/
│       │       ├── GuidIdList.sql
│       │       └── StringIdList.sql
│       ├── Migrations/
│       │   ├── v1.0.0_Initial.sql
│       │   ├── v1.1.0_AddLastError.sql
│       │   └── v2.0.0_Metrics.sql
│       └── Post-Deployment/
│           └── InitialData.sql
└── tests/
    └── Bravellian.Platform.Tests/
        └── DatabaseSchemaValidationTests.cs  # Validates runtime vs. SQL project
```

### Dual Deployment Modes

#### Mode 1: Development (Runtime Deployment)
```csharp
// Keeps working exactly as today
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    ConnectionString = "...",
    EnableSchemaDeployment = true  // Uses embedded scripts
});
```

**Use When:**
- Local development
- Integration tests
- Docker Compose environments
- Quick prototypes
- CI/CD pipelines with ephemeral databases

#### Mode 2: Production (SQL Database Project)
```bash
# Build dacpac from SQL project
dotnet build Bravellian.Platform.Database.sqlproj

# Deploy using SqlPackage
sqlpackage /Action:Publish \
  /SourceFile:Bravellian.Platform.Database.dacpac \
  /TargetConnectionString:"Server=prod;Database=MyApp;..."

# Or use Azure DevOps/GitHub Actions SQL Deploy task
```

**Use When:**
- Production deployments
- Staging environments
- Database change reviews
- Schema comparisons
- Regulatory compliance (audit trail)

### Schema Validation at Runtime

```csharp
// DatabaseSchemaManager adds validation mode
public static async Task ValidateSchemaAsync(
    string connectionString, 
    SchemaValidationMode mode = SchemaValidationMode.Warn)
{
    // Check all required tables exist
    // Verify stored procedures have correct signatures
    // Validate indexes are present
    // Check table types exist
    
    // Mode.Warn: Log warnings
    // Mode.Strict: Throw exception
    // Mode.Off: Skip validation
}
```

### Build Process Integration

#### Local Development
```bash
# Developers can work as they do today
dotnet test  # Tests create schema via runtime deployment
```

#### CI/CD Pipeline
```yaml
# .github/workflows/ci.yml
- name: Build SQL Project
  run: dotnet build src/Bravellian.Platform.Database/Bravellian.Platform.Database.sqlproj
  
- name: Validate Schema Parity
  run: dotnet test --filter Category=SchemaValidation

- name: Publish Dacpac Artifact
  uses: actions/upload-artifact@v3
  with:
    name: database-dacpac
    path: src/Bravellian.Platform.Database/bin/Release/Bravellian.Platform.Database.dacpac
```

## Implementation Roadmap

### Phase 1: Extract Schema to SQL Files (Week 1)
- [ ] Create SQL Database Project
- [ ] Extract all CREATE TABLE statements to individual .sql files
- [ ] Extract stored procedures to organized folders
- [ ] Extract user-defined types
- [ ] Add inline documentation to SQL files
- [ ] **Output**: Bravellian.Platform.Database.sqlproj builds successfully

### Phase 2: Dual Deployment Support (Week 2)
- [ ] Update DatabaseSchemaManager to read from embedded SQL resources
- [ ] Add build task to embed .sql files into assembly
- [ ] Create SchemaValidator for runtime verification
- [ ] **Output**: Both modes work identically

### Phase 3: Migration Strategy (Week 3)
- [ ] Design version tracking table `SchemaVersion`
- [ ] Create migration script template
- [ ] Add upgrade path from v1.0 → v2.0
- [ ] Document rollback procedures
- [ ] **Output**: Production-ready migration system

### Phase 4: Testing & Documentation (Week 4)
- [ ] Add schema validation tests
- [ ] Update all documentation
- [ ] Create deployment guides for both modes
- [ ] Add troubleshooting guide
- [ ] **Output**: Complete solution ready for adoption

## Migration Path for Existing Deployments

### For Existing Users

**No Breaking Changes Required**:
```csharp
// V1.x behavior - continues to work
services.AddSqlScheduler(new SqlSchedulerOptions
{
    EnableSchemaDeployment = true  // Still works!
});
```

**Optional Upgrade Path**:
```csharp
// V2.x enhancement - optional
services.AddSqlScheduler(new SqlSchedulerOptions
{
    EnableSchemaDeployment = true,
    SchemaValidationMode = SchemaValidationMode.Strict  // NEW
});
```

### Database Version Tracking

```sql
CREATE TABLE dbo.SchemaVersion (
    Component NVARCHAR(50) NOT NULL,
    Version NVARCHAR(20) NOT NULL,
    AppliedAt DATETIME2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
    AppliedBy NVARCHAR(100) NOT NULL DEFAULT SYSTEM_USER,
    CONSTRAINT PK_SchemaVersion PRIMARY KEY (Component)
);

-- Example data
INSERT INTO SchemaVersion (Component, Version) VALUES
('Outbox', '2.0.0'),
('Scheduler', '2.0.0'),
('Inbox', '1.1.0');
```

## Benefits of This Approach

### Development Experience
✅ **Easy setup**: `EnableSchemaDeployment = true` still works  
✅ **Fast iteration**: No manual SQL scripts to run  
✅ **Test friendly**: Perfect for Testcontainers  
✅ **Docker-ready**: Containers can auto-deploy schema

### Production Operations
✅ **Change tracking**: Git diffs show actual SQL changes  
✅ **Professional tools**: SSDT, SQL Compare, Schema Compare  
✅ **Review process**: DBAs can review .sql file changes  
✅ **Compliance**: Clear audit trail of schema changes  
✅ **Rollback**: Can compare and revert via dacpac

### Code Quality
✅ **Syntax highlighting**: Edit SQL in .sql files  
✅ **Intellisense**: SQL tooling understands schema  
✅ **Separation**: Schema separated from C# code  
✅ **Maintainability**: Each object in its own file  
✅ **Documentation**: Can add extensive comments in SQL

### DevOps Integration
✅ **CI/CD**: Dacpac artifacts for deployment  
✅ **Azure DevOps**: Native SQL Deploy tasks  
✅ **GitHub Actions**: SQL deployment workflows  
✅ **Environment parity**: Same schema across all envs  
✅ **Validation**: Can diff before deploy

## Alternative Approaches Considered

### Alternative 1: Pure SQL Database Project (Not Recommended)
**Why Not:**
- Breaks `EnableSchemaDeployment = true` convenience
- Forces all users to manage SQL scripts manually
- Makes testing harder (no Testcontainers auto-deploy)
- Removes a key selling point of the library

### Alternative 2: Entity Framework Core Migrations (Not Recommended)
**Why Not:**
- Platform uses Dapper for performance
- EF migrations don't handle stored procedures well
- Complex procedures (work queue pattern) are SQL-specific
- Migration files would still be C# with SQL strings
- Adds heavy dependency for marginal benefit

### Alternative 3: DbUp or FluentMigrator (Considered)
**Why Not:**
- Adds another dependency
- Still requires writing SQL in C# strings for scripts
- Hybrid approach gives us the best of both worlds
- Doesn't solve the "reviewing SQL changes" problem

### Alternative 4: Keep Current Approach (Status Quo)
**Why Not:**
- You've identified pain points
- Schema is getting complex (2270 lines)
- Professional tooling matters as product matures
- Hard to review PRs with SQL changes in C# strings

## Example: PR Review Comparison

### Current Approach
```diff
// PR shows C# string changes
-            CREATE TABLE dbo.Outbox (
-                Status TINYINT NOT NULL DEFAULT 0,
+            CREATE TABLE dbo.Outbox (
+                Status TINYINT NOT NULL DEFAULT 0,
+                Priority INT NOT NULL DEFAULT 0,
```

### With SQL Project
```diff
-- PR shows actual SQL file changes
 CREATE TABLE dbo.Outbox (
     ...
     Status TINYINT NOT NULL DEFAULT 0,
+    Priority INT NOT NULL DEFAULT 0,
     LockedUntil DATETIME2(3) NULL,
```

**Much clearer** what changed and easier for DBAs to review!

## Conclusion

The **Hybrid Approach** provides:

1. **Best of Both Worlds**: Keep runtime deployment convenience while adding professional schema management
2. **Zero Breaking Changes**: Existing users unaffected
3. **Gradual Adoption**: Teams can migrate at their own pace
4. **Professional Tooling**: Enable SSDT, dacpac, and enterprise workflows
5. **Better Code Reviews**: SQL changes visible in diffs

This strategy positions the platform for long-term success while respecting the simplicity that makes it valuable today.

## Recommendation

**Proceed with Hybrid Approach** implementation over 4 weeks:
- Week 1: Extract schema to SQL files
- Week 2: Enable dual deployment modes
- Week 3: Add migration system
- Week 4: Documentation and validation

The investment pays off through better maintainability, clearer code reviews, and enabling enterprise adoption while keeping the developer-friendly experience that makes the platform great.

---

**Next Steps:**
1. Review this proposal
2. Approve implementation plan
3. Create tracking issues for 4 phases
4. Begin Phase 1: SQL Project creation

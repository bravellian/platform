# Investigation Summary: Database Schema Management

## Question
> Can you investigate if there is a better way to handle database schemas? Originally the thought was this was going to be a lot simpler, and so it would be easy to just have it embedded in the code for both deployment, verifying if it's up to date, and so on. I would still like to have those, but with the scheme becoming a little more complex as time goes on, I want to potentially make something that's more reliable than just easy. I'm wondering if we should switch to a database project, so we can have the DDL and then Entity Framework Core. But you tell me, because in that model it becomes a lot harder to just spin up a database schema, but I'm less worried about that these days.

## Answer: Hybrid Approach (Best of Both Worlds)

### TL;DR
**Yes, adopt a SQL Database Project, BUT keep the runtime deployment too.** This gives you professional schema management without breaking the ease of use that makes your platform valuable.

### Current State
- ✅ **Works great**: `EnableSchemaDeployment = true` → instant setup
- ✅ **Test friendly**: Perfect for Testcontainers and Docker
- ❌ **Hard to review**: SQL changes buried in C# strings (2270 lines)
- ❌ **No tooling**: Can't use SSDT, SQL Compare, dacpac
- ❌ **Getting complex**: Schema is outgrowing the embedded approach

### Recommended Solution: Hybrid Approach

#### Architecture
```
Development/Testing               Production
─────────────────                ──────────
Runtime Deployment    <────OR────>    Dacpac Deployment
(DatabaseSchemaManager)           (SQL Database Project)
        │                                  │
        └──────────┬──────────────────────┘
                   │
        Both use SAME .sql files
    (embedded as resources in assembly)
```

#### For Developers (Nothing Changes)
```csharp
// This continues to work exactly as today
builder.Services.AddSqlScheduler(new SqlSchedulerOptions
{
    EnableSchemaDeployment = true  // ← Still works!
});
```

#### For DBAs / Production (New Capability)
```bash
# Build dacpac from SQL Project
dotnet build Bravellian.Platform.Database.sqlproj

# Deploy with full diff reporting
sqlpackage /Action:Publish \
  /SourceFile:Bravellian.Platform.Database.dacpac \
  /TargetConnectionString:"..." \
  /p:GenerateDeploymentReport=True
```

### Why NOT Entity Framework Core?

You asked about EF Core. **I recommend against it** for these reasons:

1. **Performance**: Your platform uses Dapper for speed
2. **Stored Procedures**: EF migrations handle these poorly
3. **Work Queue Pattern**: The complex SQL (READPAST, UPDLOCK) is database-specific
4. **Migration Files**: Still C# code with SQL strings—doesn't solve the review problem
5. **Heavy Dependency**: Adds EF Core for marginal benefit
6. **Philosophy Mismatch**: Platform is "close to the metal" SQL—EF is abstraction

**SQL Database Project gives you:**
- ✅ Real .sql files (not C# strings)
- ✅ Professional tooling
- ✅ No runtime dependency
- ✅ Works with Dapper
- ✅ Full control over complex SQL

### What You Get

#### Developer Experience
✅ Easy setup unchanged  
✅ Fast iteration unchanged  
✅ Test-friendly unchanged  
✅ **NEW**: Real .sql files with intellisense  
✅ **NEW**: Syntax highlighting and validation  

#### Operations & DevOps
✅ **NEW**: Clear SQL diffs in PRs  
✅ **NEW**: DBA-friendly code reviews  
✅ **NEW**: Professional tools (SSDT, SQL Compare)  
✅ **NEW**: Enterprise-ready deployments  
✅ **NEW**: Compliance/audit trail  

#### Code Quality
✅ **NEW**: Separation of concerns  
✅ **NEW**: Each object in its own file  
✅ **NEW**: Rich inline documentation  
✅ **NEW**: Build-time validation  

### Implementation Plan (4 Weeks)

**Week 1: Extract Schema**
- Create SQL Project structure
- Move all DDL to .sql files
- Organize by type (Tables, Procedures, Types)

**Week 2: Dual Deployment**
- Update DatabaseSchemaManager to read embedded SQL
- Add build task to embed .sql files
- Create runtime validation

**Week 3: Migrations**
- Design version tracking (SchemaVersion table)
- Create migration script template
- Add upgrade/rollback procedures

**Week 4: Testing & Docs**
- Schema validation tests
- Update all documentation
- Deployment guides for both modes

### Zero Breaking Changes

**Existing users:**
- No changes required
- Keep using `EnableSchemaDeployment = true`
- Everything works exactly as before

**New capabilities (optional):**
- Can adopt SQL Project for production
- Can use dacpac deployment
- Can validate with professional tools

### Example: PR Review Improvement

**Before (current):**
```diff
// DatabaseSchemaManager.cs
-            CREATE TABLE [{schemaName}].[{tableName}] (
+            CREATE TABLE [{schemaName}].[{tableName}] (
+                Priority INT NOT NULL DEFAULT 0,
```

**After (SQL Project):**
```diff
-- Schema/Tables/Outbox.sql
 CREATE TABLE [dbo].[Outbox] (
     [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
     [Payload] NVARCHAR(MAX) NOT NULL,
+    [Priority] INT NOT NULL DEFAULT 0,
```

Much clearer what changed! ✨

### Files Created in This Investigation

1. **`docs/DATABASE_SCHEMA_STRATEGY.md`** (11KB)
   - Comprehensive strategy document
   - Architecture diagrams
   - Implementation roadmap
   - Alternative approaches analyzed

2. **`src/Bravellian.Platform.Database/`** (Complete POC)
   - SQL Project file (.sqlproj)
   - Example tables (Outbox.sql)
   - Example procedures (Outbox_Claim, Outbox_Ack)
   - Types (GuidIdList, StringIdList)
   - Migration framework
   - README with instructions

3. **`docs/SQL_PROJECT_COMPARISON.md`** (8KB)
   - Before/after examples
   - Side-by-side workflows
   - Real-world scenarios
   - Feature comparison matrix

### Decision Time

**I recommend:** ✅ **Proceed with Hybrid Approach**

**Reasons:**
1. ✅ Addresses all pain points you mentioned
2. ✅ No breaking changes for existing users
3. ✅ Positions platform for enterprise adoption
4. ✅ Improves developer AND operator experience
5. ✅ Better than alternatives (EF, DbUp, status quo)
6. ✅ Clear 4-week implementation path

**What NOT to do:**
- ❌ Pure SQL Project (breaks ease of use)
- ❌ Entity Framework Core (wrong tool for the job)
- ❌ Status quo (pain points will grow with complexity)

### Next Steps (If You Approve)

1. Review the three documents I created
2. Discuss with your team
3. Create GitHub issues for 4 phases
4. I can help implement Phase 1 (schema extraction)

### Questions?

**Q: Will this slow down development?**  
A: No. Developers keep using `EnableSchemaDeployment = true`. The SQL Project is optional.

**Q: What about existing deployments?**  
A: Zero impact. They continue working as-is.

**Q: Can we do this incrementally?**  
A: Yes! Extract schema gradually. Both modes work in parallel.

**Q: What if we want to roll back?**  
A: Runtime deployment still works. SQL Project is additive, not replacing.

**Q: Is this standard practice?**  
A: Yes. SQL Projects are industry standard for professional SQL Server development.

---

## Conclusion

Your instinct is correct—the schema needs better management as it grows. But **don't give up the runtime deployment convenience**. The Hybrid Approach gives you:

- Professional schema management (SQL Project)
- Developer-friendly deployment (`EnableSchemaDeployment`)
- No breaking changes
- Clear migration path

**This is the right move at the right time.** The platform is maturing, and its schema management should too—without sacrificing what makes it great.

---

**Author**: GitHub Copilot  
**Date**: 2025-12-07  
**Branch**: `copilot/investigate-database-schema-handling`

# Prompt: Extract Join Coordination spec and make Outbox join-agnostic

You are editing the Markdown file that defines the "Outbox Component – Functional Specification".

## Objective

Make the Outbox spec **join-agnostic** by removing all join/fan-in concepts, APIs, and behaviors, then create a separate "Join Coordination Component – Functional Specification" document.

## Part 1: Remove Join Content from Outbox Spec

Make the following removals from the Outbox spec:

1. **§2.2** - Remove the note about join/fan-in being "an optional feature built on top of the core Outbox functionality."

2. **§2.3** - Remove the "Architecturally Separate (but included for completeness)" bullet about join/fan-in coordination.

3. **§4.2** - Remove `JoinIdentifier` from the Strongly-Typed Identifiers section.

4. **§4.5** - Delete the entire "Join/Fan-In Concepts" section.

5. **§5.1.1** - In the `IOutbox` interface section:
   - Remove the entire "Note on Join Operations" paragraph.
   - Remove the following method signatures:
     - `Task<JoinIdentifier> StartJoinAsync(...)`
     - `Task AttachMessageToJoinAsync(...)`
     - `Task ReportStepCompletedAsync(...)`
     - `Task ReportStepFailedAsync(...)`

6. **§6.3** - In "Message Acknowledgment", remove:
   - **OBX-033** (increment join counters on ack)
   - **OBX-034** (atomic join counter increment)

7. **§6.5** - In "Message Failure", remove:
   - **OBX-046** (increment join counters on fail)
   - **OBX-047** (atomic join counter increment)

8. **§6.11** - Delete the entire "Join/Fan-In Coordination" section (OBX-080 through OBX-096).

9. **§6.14** - Update **OBX-111** from:
   > The Outbox schema MUST include the following tables: `Outbox`, `OutboxJoin`, `OutboxJoinMember`.
   
   to:
   > The Outbox schema MUST include the `Outbox` table and associated indexes needed for claiming and processing messages.
   >
   > Join-related tables (e.g., `OutboxJoin`, `OutboxJoinMember`) are owned by the Join component and are specified in the Join Coordination specification.

10. **§8** - Move the following open questions to the Join spec:
    - **8.1 Join Store Singleton Limitation**
    - **8.2 Automatic vs. Manual Join Reporting**
    
    Renumber remaining questions accordingly.

11. **Appendix A** - Remove:
    - **A.2 OutboxJoin Table**
    - **A.3 OutboxJoinMember Table**

12. **Appendix D** - Remove the entire "Join/Fan-In Example" appendix.

## Part 2: Add Related Components Note

In **§2.3**, after the "In Scope / Out of Scope" bullets, add:

```markdown
**Related Components (documented separately):**
- Join / fan-in coordination built on top of Outbox using `OutboxMessageIdentifier` and Outbox stored procedures. The Outbox component itself remains join-agnostic.
```

## Part 3: Create New Join Coordination Spec

Create a **new document** called `join-coordination-specification.md` with the following structure:

### Document Structure

```markdown
# Join Coordination Component - Functional Specification

## 1. Meta

| Property | Value |
|----------|-------|
| **Component** | Join Coordination |
| **Version** | 1.0 |
| **Status** | Active |
| **Owner** | Bravellian Platform Team |
| **Last Updated** | 2025-12-07 |
| **Dependencies** | Outbox Component (v1.0) |

## 2. Purpose and Scope

### 2.1 Purpose

The Join Coordination component provides fan-in coordination for workflow orchestration, enabling systems to wait for the completion of multiple related messages before proceeding to the next step.

### 2.2 Architecture

Join coordination is built **on top of** the Outbox component:

- Joins use `OutboxMessageIdentifier` to track message relationships.
- Join tables (`OutboxJoin`, `OutboxJoinMember`) are separate from the `Outbox` table.
- Outbox messages do not contain join identifiers; the association is maintained in the `OutboxJoinMember` table.
- The Join component integrates with Outbox by hooking into the same stored procedures that mark messages as completed or failed, automatically updating join counters.
- The Outbox component itself has no knowledge of joins and remains join-agnostic.

## 3. Key Concepts

[Move §4.5 "Join/Fan-In Concepts" content here]

## 4. Public API Surface

[Move the join-related methods from IOutbox here, documenting them as part of a join-specific interface or extension]

## 5. Behavioral Requirements

[Move requirements OBX-033, OBX-034, OBX-046, OBX-047, and OBX-080 through OBX-096 here]
[Rename all requirement IDs from OBX-xxx to JOIN-xxx]

## 6. Database Schema

[Move Appendix A.2 and A.3 here - OutboxJoin and OutboxJoinMember tables]

## 7. Open Questions

[Move §8.1 and §8.2 here - Join Store Singleton Limitation and Automatic vs. Manual Join Reporting]

## 8. Usage Example

[Move Appendix D content here - the Join/Fan-In Example]
```

### Content Migration Details

**Join/Fan-In Concepts** - Move this entire block from the Outbox spec:
- **Join**: A coordination primitive that tracks completion of multiple related messages
- **Join Member**: An association between a join and a specific outbox message
- **Expected Steps**: The total number of messages that must complete for a join to finish
- **Completed Steps**: Count of messages that have been successfully processed
- **Failed Steps**: Count of messages that have permanently failed
- **Join Status**: Current state of the join (Pending, Completed, Failed, Cancelled)
- **Grouping Key**: An optional string identifier used to scope joins to a specific context

**Strongly-Typed Identifier** - Add `JoinIdentifier`:
- **JoinIdentifier**: A unique identifier for a join coordination primitive. Joins use this ID to track groups of related messages and coordinate fan-in operations.

**Integration with Outbox** - Clearly document:
- Joins hook into `Outbox_Ack` and `Outbox_Fail` stored procedures
- These procedures automatically increment `CompletedSteps` and `FailedSteps` counters
- This is why the Outbox spec mentions that join counters are updated "automatically by the database"
- The Outbox component's public API surface does not expose join operations

## Part 4: Cross-Linking

Ensure both specs reference each other:

**In Outbox spec** (already added in Part 2):
- §2.3 "Related Components" note pointing to Join spec

**In Join spec**:
- §1 Meta table: explicit dependency on "Outbox Component (v1.0)"
- §2.2 Architecture: clear explanation that joins are built on top of Outbox

## Validation

After making changes, verify:

1. The Outbox spec contains **no references** to:
   - `JoinIdentifier`
   - `StartJoinAsync`, `AttachMessageToJoinAsync`, `ReportStepCompletedAsync`, `ReportStepFailedAsync`
   - `OutboxJoin` or `OutboxJoinMember` tables
   - Requirements OBX-033, OBX-034, OBX-046, OBX-047, OBX-080 through OBX-096

2. The Join spec clearly states:
   - It depends on the Outbox component
   - It uses `OutboxMessageIdentifier` from the Outbox component
   - It integrates with Outbox stored procedures but is architecturally separate
   - The Outbox component has no knowledge of joins

3. Both specs cross-reference each other appropriately.

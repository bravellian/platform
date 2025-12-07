# Outbox Specification Update Summary

**Date**: 2025-12-07  
**Files Modified**: 
- `specs/outbox-specification.md`
- `specs/prompt-extract-join-spec.md` (new)
- `specs/prompt-normalize-parameters.md` (new)

## Changes Applied

### 1. Separation of Join Concerns from Outbox

The Outbox specification has been made join-agnostic. All join/fan-in coordination concepts, APIs, and behaviors have been removed.

#### Removed from Outbox Spec:

**§2.2 Core Responsibilities**
- Removed note about join/fan-in being "optional feature built on top"

**§2.3 Scope**
- Removed "Architecturally Separate (but included for completeness)" section
- Added new "Related Components" section noting Join coordination exists separately

**§3 Non-Goals**
- Updated item 4 to remove "joins across databases" mention, simplified to just cross-database transactions

**§4.2 Strongly-Typed Identifiers**
- Removed `JoinIdentifier`

**§4.5 Join/Fan-In Concepts**
- Entire section removed (was defining Join, Join Member, Expected Steps, etc.)

**§5.1.1 IOutbox Interface**
- Removed entire "Note on Join Operations" paragraph
- Removed `StartJoinAsync`, `AttachMessageToJoinAsync`, `ReportStepCompletedAsync`, `ReportStepFailedAsync` methods

**§6.3 Message Acknowledgment**
- Removed OBX-033 (increment join counters on ack)
- Removed OBX-034 (atomic join counter increment)

**§6.5 Message Failure**
- Removed OBX-046 (increment join counters on fail)
- Removed OBX-047 (atomic join counter increment)

**§6.11 Join/Fan-In Coordination**
- Entire section removed (OBX-080 through OBX-096)

**§6.14 Schema Deployment**
- Updated OBX-111 to only mention `Outbox` table
- Added note that join tables are owned by Join component

**§8 Open Questions**
- Removed "8.1 Join Store Singleton Limitation"
- Removed "8.2 Automatic vs. Manual Join Reporting"
- Renumbered remaining questions

**Appendix A**
- Removed A.2 OutboxJoin Table
- Removed A.3 OutboxJoinMember Table

**Appendix D**
- Entire "Join/Fan-In Example" appendix removed

### 2. Method Overload Consolidation and Parameter Semantics

All public interfaces now have comprehensive, explicit parameter documentation.

#### §5.1.1 IOutbox - EnqueueAsync

**Added**: Implementation note explaining that convenience overloads MAY exist but are defined in terms of the canonical signature.

#### §5.1.1 IOutbox - ClaimAsync

**Enhanced parameter documentation**:
- `ownerToken`: Full type info, constraints (non-empty), purpose
- `leaseSeconds`: Type, range constraints (>0, 10-300 recommended), exception behavior
- `batchSize`: Type, range constraints (>0, 1-100 recommended), exception behavior
- `cancellationToken`: Purpose clarification

#### §5.1.1 IOutbox - AckAsync, AbandonAsync, FailAsync

**Enhanced parameter documentation** (consistent across all three):
- `ids`: Full constraints (non-null, may be empty = no-op, duplicates tolerated)
- `ownerToken`: Matching requirements, silent ignore behavior
- `cancellationToken`: Standard usage

#### §5.1.1 IOutbox - ReapExpiredAsync

**Added parameter documentation**:
- `cancellationToken`: Clarified that method must stop processing when cancellation requested

#### §5.1.2 IOutboxHandler

**Added**:
- Topic Property Constraints (non-null, non-empty, case-sensitive, same rules as EnqueueAsync topic)
- HandleAsync Parameters (message must not be null, cancellation should be honored)

#### §5.1.3 IOutboxRouter

**Consolidated to canonical signature**:
- Only `GetOutbox(string routingKey)` is documented
- Full parameter semantics for routingKey
- Implementation note that `GetOutbox(Guid)` overload MAY exist as convenience

#### §5.1.4 IOutboxStore

**Added Parameter Semantics section**:
- `ClaimDueAsync`: limit constraints and behavior
- `RescheduleAsync`: delay constraints, lastError normalization
- `FailAsync`: lastError normalization

### 3. New Behavioral Requirements

**Added to §6.2 Message Claiming**:
- **OBX-127**: ClaimAsync MUST throw ArgumentOutOfRangeException if leaseSeconds <= 0
- **OBX-128**: ClaimAsync MUST throw ArgumentOutOfRangeException if batchSize <= 0
- **OBX-130**: ClaimAsync MUST only claim messages where NextAttemptAt <= current UTC time

**Added to §6.3 Message Acknowledgment**:
- **OBX-129**: AckAsync/AbandonAsync/FailAsync MUST throw ArgumentNullException if ids is null, and treat empty ids as no-op

### 4. Updated Scheduling Concepts

**§4.5 Scheduling Concepts** (renumbered from 4.6):
- Updated "Next Attempt Time" definition to clarify it applies to all retries, not just failed messages
- Changed from "For failed messages, the calculated time..." to "The earliest UTC time when a message (including previously failed or abandoned messages) becomes eligible to be claimed again"

### 5. Updated Examples

**Appendix C: Multi-Tenant Usage Example**:
- Updated `EnqueueAsync` call to use canonical signature with named parameters
- Shows: `topic:`, `payload:`, `transaction:`, `correlationId:`, `dueTimeUtc:`, `cancellationToken:`

## Prompt Files Created

Two comprehensive prompt files were created for future use or validation:

### `prompt-extract-join-spec.md`
Complete instructions for:
- Extracting all join-related content from Outbox spec
- Creating new `join-coordination-specification.md`
- Migrating all join concepts, APIs, requirements, schema, and examples
- Renaming requirement IDs from OBX-xxx to JOIN-xxx
- Establishing proper dependencies and cross-references

### `prompt-normalize-parameters.md`
Complete instructions for:
- Normalizing to canonical method signatures
- Adding comprehensive parameter semantics to all interfaces
- Creating testable behavioral requirements
- Updating examples to match canonical signatures
- Validation checklist for completeness

## Result

The Outbox specification is now:

1. **Join-agnostic**: No knowledge of or dependency on join coordination
2. **Precise**: Every parameter has explicit type, constraints, null/empty semantics, and exception behavior
3. **Testable**: All constraints have corresponding behavioral requirements with unique IDs
4. **Consistent**: Examples match canonical signatures, no conflicting overload definitions
5. **Maintainable**: Clear separation of concerns enables independent evolution of Outbox and Join components

The specification is ready for implementation validation and can serve as a comprehensive contract for the Outbox component.

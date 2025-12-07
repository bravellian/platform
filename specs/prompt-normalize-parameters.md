# Prompt: Normalize method signatures and parameter semantics in Outbox spec

You are editing the "Outbox Component – Functional Specification" Markdown file.

## Objective

Normalize all method signatures to have one canonical definition per operation and provide comprehensive parameter semantics for all public interfaces. The goal is to make every parameter constraint explicit, testable, and unambiguous.

## Principles

1. **One canonical signature per operation**: If multiple overloads exist, document only the most verbose canonical signature. Add an implementation note that convenience overloads MAY exist and are defined in terms of the canonical signature.

2. **Comprehensive parameter semantics**: For every method and property, document:
   - Type
   - Purpose
   - Whether null is allowed
   - Whether empty strings are allowed
   - How null vs empty strings are normalized
   - Length limits
   - Case sensitivity rules
   - Recommended and required numeric ranges
   - Exception behavior for invalid values

3. **Testable requirements**: Where parameter constraints imply observable behavior, add corresponding behavioral requirements in §6.

## Part 1: IOutbox Interface

### EnqueueAsync

**Status**: ✅ Already done in the spec updates.

The canonical signature is:
```csharp
Task EnqueueAsync(
    string topic,
    string payload,
    IDbTransaction? transaction,
    string? correlationId,
    DateTimeOffset? dueTimeUtc,
    CancellationToken cancellationToken)
```

Add after parameter descriptions:
```markdown
_Implementation note_: The runtime MAY expose convenience overloads of `EnqueueAsync` that omit `transaction`, `correlationId`, and/or `dueTimeUtc`. For the purposes of this specification, all overloads are defined in terms of this canonical signature. Omitted arguments are treated as if `null` (or `CancellationToken.None`) was passed.
```

### ClaimAsync

**Current state**: Basic parameter list exists.

**Required changes**: Expand to full parameter semantics:

```markdown
**Parameters:**

- `ownerToken` (required): A stable identifier for the worker instance.
  - Type: `OwnerToken` (strongly-typed GUID).
  - Constraints:
    - MUST NOT be the default/empty value.
    - SHOULD remain constant for the lifetime of the worker process instance.
  - Purpose: Used to enforce that only the owning worker can ack/abandon/fail the messages it claimed.

- `leaseSeconds` (required): Duration in seconds for which the claim is valid.
  - Type: `int`
  - Constraints:
    - MUST be greater than 0.
    - SHOULD be between 10 and 300 seconds for typical workloads.
    - If `leaseSeconds <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - Purpose: Controls how long messages are locked before they can be reaped or reclaimed.

- `batchSize` (required): Maximum number of messages to claim.
  - Type: `int`
  - Constraints:
    - MUST be greater than 0.
    - SHOULD be between 1 and 100 for typical workloads.
    - If `batchSize <= 0`, the method MUST throw an `ArgumentOutOfRangeException`.
  - Purpose: Batches work for efficiency while bounding per-iteration load.

- `cancellationToken` (required): Cancellation token for the operation.
  - Type: `CancellationToken`
  - Purpose: Allows cancelling the claim operation.
```

**Add requirements** in §6.2:
- **OBX-127**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `leaseSeconds <= 0`.
- **OBX-128**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `batchSize <= 0`.

### AckAsync, AbandonAsync, FailAsync

**Current state**: Very basic parameter descriptions.

**Required changes**: Use this shared pattern for all three methods:

```markdown
**Parameters:**

- `ids` (required): The set of work item identifiers to acknowledge/abandon/fail.
  - Type: `IEnumerable<OutboxWorkItemIdentifier>`
  - Constraints:
    - MUST NOT be null.
    - MAY be empty; an empty sequence is treated as a no-op.
    - Duplicate IDs within the sequence MUST be tolerated and MUST NOT cause errors.
  - Purpose: Indicates which claimed messages to transition to the next state.

- `ownerToken` (required): The worker identity that previously claimed the messages.
  - Constraints:
    - MUST match the `OwnerToken` currently stored on each message to be updated.
  - Behavior:
    - Messages whose `OwnerToken` does not match are silently ignored and MUST NOT cause the method to fail.

- `cancellationToken` (required): Cancellation token for the operation.
```

**Add requirement** in §6.3 (or appropriate section):
- **OBX-129**: `AckAsync`, `AbandonAsync`, and `FailAsync` MUST throw an `ArgumentNullException` if `ids` is null, and MUST treat an empty `ids` collection as a no-op.

### ReapExpiredAsync

**Required changes**: Add parameter description:

```markdown
**Parameters:**

- `cancellationToken` (required): Cancellation token for the operation.
  - Purpose: Allows cancellation of the reap operation. If cancellation is requested, the method MUST stop processing as soon as practical.
```

## Part 2: IOutboxHandler Interface

**Current state**: Just the interface definition.

**Required changes**: Add parameter and property semantics:

```markdown
**Topic Property Constraints:**

- `Topic` MUST be non-null and non-empty.
- The handler's `Topic` MUST match the `topic` value used in `EnqueueAsync` calls in a case-sensitive manner.
- The same length and character recommendations as `topic` (see §5.1.1) apply.

**HandleAsync Parameters:**

- `message`: The claimed outbox message to process. MUST NOT be null.
- `cancellationToken`: Standard cancellation token. Handlers SHOULD honor cancellation where practical.
```

## Part 3: IOutboxRouter Interface

**Current state**: Shows two overloads (`GetOutbox(string)` and `GetOutbox(Guid)`).

**Required changes**: Define one canonical method:

```csharp
IOutbox GetOutbox(string routingKey)
```

```markdown
**Parameters:**

- `routingKey` (required): Opaque routing identifier used to choose a tenant/database.
  - Type: `string`
  - Constraints:
    - MUST NOT be null or empty.
    - SHOULD be stable and unique per outbox store (e.g., tenant ID, database name).
  - Behavior:
    - If no outbox exists for the specified `routingKey`, the method MUST throw `InvalidOperationException`.

_Implementation note_: Some implementations MAY provide additional overloads (e.g., `GetOutbox(Guid tenantId)`) that internally convert other key types to strings before delegating to this canonical method. Such overloads are convenience APIs and do not change the specified behavior.
```

## Part 4: IOutboxStore Interface

**Required changes**: Add parameter semantics block after the method signatures:

```markdown
**Parameter Semantics:**

- `ClaimDueAsync(int limit, ...)`
  - `limit` MUST be greater than 0. If `limit <= 0`, the store MUST throw an `ArgumentOutOfRangeException`.
  - The store MUST NOT return more than `limit` messages.

- `RescheduleAsync(OutboxWorkItemIdentifier id, TimeSpan delay, string lastError, ...)`
  - `delay` MUST be greater than zero; zero or negative values MUST result in an `ArgumentOutOfRangeException`.
  - `lastError` MAY be null or empty. Implementations SHOULD normalize empty strings to null to avoid noise.

- `FailAsync(OutboxWorkItemIdentifier id, string lastError, ...)`
  - `lastError` MAY be null or empty; same normalization rule as above.
```

## Part 5: Update Examples to Match Canonical Signatures

### Appendix C: Multi-Tenant Usage Example

**Current code**:
```csharp
await outbox.EnqueueAsync(
    "order.created",
    JsonSerializer.Serialize(order),
    order.Id.ToString(),
    cancellationToken: CancellationToken.None);
```

**Update to**:
```csharp
await outbox.EnqueueAsync(
    topic: "order.created",
    payload: JsonSerializer.Serialize(order),
    transaction: null,
    correlationId: order.Id.ToString(),
    dueTimeUtc: null,
    cancellationToken: CancellationToken.None);
```

This makes the example consistent with the canonical signature and demonstrates the use of named arguments for clarity.

## Part 6: Additional Behavioral Requirements

Add the following requirements to appropriate sections in §6:

### Message Claiming (§6.2)

- **OBX-127**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `leaseSeconds <= 0`.
- **OBX-128**: `ClaimAsync` MUST throw an `ArgumentOutOfRangeException` if `batchSize <= 0`.
- **OBX-130**: `ClaimAsync` MUST only claim messages where `NextAttemptAt` is less than or equal to the current UTC time.

### Message Acknowledgment (§6.3)

- **OBX-129**: `AckAsync`, `AbandonAsync`, and `FailAsync` MUST throw an `ArgumentNullException` if `ids` is null, and MUST treat an empty `ids` collection as a no-op.

## Part 7: Update Next Attempt Time Definition

In **§4.5 Scheduling Concepts**, update the bullet:

**From**:
> **Next Attempt Time**: For failed messages, the calculated time when the next retry should occur

**To**:
> **Next Attempt Time**: The earliest UTC time when a message (including previously failed or abandoned messages) becomes eligible to be claimed again

This makes it clear that `NextAttemptAt` applies to all retries, not just permanent failures.

## Validation Checklist

After making changes, verify:

1. ✅ Every public method has a **Parameters** section listing all parameters with:
   - Type
   - Purpose
   - Constraints (null, empty, length, range)
   - Exception behavior

2. ✅ For methods with multiple overloads in the implementation:
   - Only the canonical (most verbose) signature is documented
   - An "implementation note" explains that other overloads exist

3. ✅ All examples in appendices use the canonical signatures or named arguments for clarity

4. ✅ All testable parameter constraints have corresponding requirements in §6 with unique OBX-xxx IDs

5. ✅ No ambiguity remains about:
   - Null vs empty string handling
   - Case sensitivity
   - Length limits
   - Numeric range validations
   - Exception types and conditions

## Implementation Strategy

For maximum precision, make changes in this order:

1. Update all method signatures to canonical form (remove overload definitions)
2. Add comprehensive **Parameters** sections to each method
3. Add **implementation note** blocks where overloads exist
4. Add new behavioral requirements to §6
5. Update all examples to match canonical signatures
6. Update concept definitions (e.g., Next Attempt Time)

This ensures a consistent, testable, and unambiguous specification.

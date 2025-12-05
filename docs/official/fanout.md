I’ll treat this as an **Explanation** page and draft a single, self‑contained doc that consolidates fanout and its relationship to the outbox.

---

# Understanding fanout and the outbox

This page explains what “fanout” means in the Bravellian Platform–based architecture, how it is built on top of the outbox pattern, and how the multi‑outbox and outbox router pieces fit into the picture.

The goal is to give you a mental model you can use when designing or reading code that uses fanout.

---

## 1. Recap: the outbox as the foundation

The **outbox pattern** is the core building block. It solves the dual‑write problem by writing business data and outbound messages in the same transaction, and then processing those messages asynchronously.

At a high level:

```text
┌─────────────────┐
│  Your Code      │   Begin transaction
│  (Transaction)  │
└────────┬────────┘
         │
         ├─► Save business data
         │
         └─► Enqueue outbox message
                │
                │ (Commit transaction)
                ▼
         ┌──────────────┐
         │ Outbox table │
         └──────┬───────┘
                │
                │ (Background worker)
                ▼
         ┌──────────────┐
         │  Handler(s)  │
         └──────┬───────┘
                │
                └─► Publish to broker / call APIs / etc.
```

Key outbox concepts:

* **Outbox table**: Holds messages with `Topic`, `Payload`, timestamps, and work‑queue metadata such as status and retry counts.
* **`IOutbox`**: API for enqueueing and processing messages (`EnqueueAsync`, `ClaimAsync`, `AckAsync`, `AbandonAsync`, `FailAsync`, `ReapExpiredAsync`).
* **`IOutboxHandler`**: Your code that handles messages for a given `Topic`.
* **Background service**: Claims messages, calls the appropriate handler, and acknowledges or retries with backoff.

The important point for fanout: **the outbox gives you a durable, retryable work queue of events**, but it does not dictate how many things each event triggers. Fanout builds on that.

---

## 2. What we mean by “fanout”

In this architecture, **fanout** is the way a *single logical event* turns into *many downstream actions*, for example:

* One business operation generates an `order.created` event.
* That event drives:

  * A message to a broker.
  * A customer email.
  * A billing or reporting update.
  * Potentially the same actions across multiple tenant databases.

We can think of fanout along two axes:

1. **Fanout across handlers (within one database)**
   One outbox message can conceptually drive multiple independent behaviors by being processed by one or more handlers associated with its `Topic`.

2. **Fanout across databases / tenants (multi‑outbox)**
   The same handler logic is applied to outbox messages in many tenant databases via the multi‑outbox infrastructure: `IOutboxStoreProvider`, `IOutboxSelectionStrategy`, `MultiOutboxDispatcher`, and `MultiOutboxPollingService`.

Fanout is **not** a separate reliability mechanism. It is an **architecture built on top of the outbox** that spreads the effects of events across multiple consumers and, often, multiple data stores.

---

## 3. Single‑database fanout: from an outbox message to multiple actions

In a single database, the platform already gives you a natural place for fanout: **outbox handlers**.

### 3.1 Flow

1. Your business code saves its data and enqueues an outbox message in the same transaction, using `IOutbox.EnqueueAsync`.
2. The background service claims messages from the outbox table, using `ClaimAsync`.
3. It dispatches each message to the handler(s) that process the corresponding `Topic`.
4. The handler does *whatever you want*:

   * Publish to a message broker.
   * Call an HTTP API.
   * Write to another table.
   * Trigger internal workflows.
5. The worker finishes by calling `AckAsync`, `AbandonAsync`, or `FailAsync` to update status and retries.

### 3.2 Where fanout happens

There are two common fanout patterns at this level:

* **One topic, handler that does multiple things**
  For example, `OrderCreatedHandler` publishes to a broker *and* writes to a reporting table, or sends an email.

* **Multiple topics representing different “branches” of work**
  Your transactional code may enqueue several messages (`order.created`, `order.audit.logged`, `email.welcome`, etc.). Different handlers subscribe to each topic and implement the branching of behavior.

In both cases, fanout is **driven by handlers** and **backed by the outbox**:

```text
Order transaction
    └─► Outbox messages:
           - order.created
           - email.welcome
           - audit.order.created
                          ▼
                     Handlers:
           ┌────────────────────────────────┐
           │ OrderCreatedHandler           │
           │  - Publish to broker          │
           └────────────────────────────────┘
           ┌────────────────────────────────┐
           │ WelcomeEmailHandler           │
           │  - Send email                 │
           └────────────────────────────────┘
           ┌────────────────────────────────┐
           │ AuditLogHandler               │
           │  - Write audit entry          │
           └────────────────────────────────┘
```

From the application’s point of view, **you wrote once (to the outbox)**. Fanout is purely a function of which handlers you attach to those messages.

---

## 4. Multi‑tenant fanout: multi‑outbox processing

In multi‑tenant systems you often have **one database per tenant**, each with its own outbox table. Fanout now has an extra dimension: you want one piece of code to react to events coming from *many* tenant outboxes.

The multi‑outbox feature solves this.

### 4.1 Core abstractions

Multi‑outbox is built on four key abstractions:

1. `IOutboxStoreProvider` – returns the list of outbox stores (one per database) that should be processed.
2. `IOutboxSelectionStrategy` – decides **which** outbox to poll next (round‑robin, drain‑first, or a custom strategy).
3. `MultiOutboxDispatcher` – for a selected outbox store, uses the regular `IOutbox` API to claim and dispatch messages.
4. `MultiOutboxPollingService` – a background service that loops forever, asking the selection strategy for the next store and kicking off dispatch.

The important part: **each individual outbox store is still just a normal `IOutbox`** with the same claim/ack/abandon semantics. Fanout across tenants happens because the multi‑outbox worker keeps moving between stores.

### 4.2 Static vs dynamic databases

Multi‑outbox supports two discovery models:

* **Static configuration** – you supply a fixed list of `SqlOutboxOptions`, one per tenant, and call `AddMultiSqlOutbox(...)`.
* **Dynamic discovery** – you implement `IOutboxDatabaseDiscovery` and hook it up via `DynamicOutboxStoreProvider` and `AddDynamicMultiSqlOutbox(...)`. The provider periodically asks your registry which tenant databases exist and updates the set of outbox stores.

That gives you **fanout over time and tenants**: the same handler code processes events for new tenants as soon as they appear in your registry.

### 4.3 Fanout across tenants: end‑to‑end picture

Putting it together:

```text
Tenant DBs:
┌────────────────────┐    ┌────────────────────┐    ┌────────────────────┐
│ Customer1 DB       │    │ Customer2 DB       │    │ Customer3 DB       │
│  dbo.Outbox        │    │  dbo.Outbox        │    │  dbo.Outbox        │
└─────────┬──────────┘    └─────────┬──────────┘    └─────────┬──────────┘
          │                         │                         │
          ▼                         ▼                         ▼
    IOutbox (C1)              IOutbox (C2)              IOutbox (C3)
          \                         |                         /
           \                        |                        /
            └───────── IOutboxStoreProvider ────────────────┘
                             │
                             ▼
                  IOutboxSelectionStrategy
                             │
                             ▼
                   MultiOutboxDispatcher
                             │
                             ▼
                      IOutboxHandler(s)
```

From the handlers’ perspective, **tenant differences are invisible**: they receive `OutboxMessage` instances and behave the same regardless of which database they came from.

---

## 5. Routing writes in a fanout topology: `IOutboxRouter`

Multi‑outbox explains how we **read** from many outbox tables. To complete the picture, we need a way to **write** to the right outbox table when a tenant‑specific event occurs.

This is the job of `IOutboxRouter`.

* In a single‑database app you inject `IOutbox` directly and enqueue messages normally.
* In a multi‑database app you register multi‑outbox and inject `IOutboxRouter`. You then call `GetOutbox(key)` (usually with a tenant ID) and enqueue to the returned `IOutbox`.

Example:

```csharp
public class MyMultiTenantService
{
    private readonly IOutboxRouter _outboxRouter;

    public MyMultiTenantService(IOutboxRouter outboxRouter)
    {
        _outboxRouter = outboxRouter;
    }

    public async Task CreateOrderAsync(string tenantId, Order order)
    {
        var outbox = _outboxRouter.GetOutbox(tenantId);

        await outbox.EnqueueAsync(
            topic: "order.created",
            payload: JsonSerializer.Serialize(order),
            correlationId: order.Id.ToString());
    }
}
```

This gives you a **full fanout topology**:

* **Writes**: `IOutboxRouter` guarantees that each tenant’s events land in that tenant’s own outbox table.
* **Reads**: `MultiOutboxPollingService` and `MultiOutboxDispatcher` process all tenants’ outboxes using the same set of handlers.

---

## 6. How fanout relates to the outbox pattern

You can think of fanout as “outbox **plus** distribution rules”. The key relationships:

### 6.1 Outbox provides the reliability; fanout adds shape

* Outbox guarantees that **if your transaction commits, your message will eventually be processed**, with retry and backoff.
* Fanout does **not** change those guarantees; it simply defines:

  * *How many* messages you enqueue in a transaction (multiple topics, multiple tenants).
  * *Which* handlers and external systems those messages reach.

### 6.2 Same semantics, more places

All of the core work‑queue semantics are unchanged in a fanout setup:

* Claiming work with leases (`ClaimAsync`).
* Acknowledging success (`AckAsync`), retrying temporary failures (`AbandonAsync`), and marking permanent failures (`FailAsync`).
* Reaping expired leases to recover from crashes (`ReapExpiredAsync`).

Fanout just means those same semantics apply:

* Per message.
* Per handler.
* Per tenant outbox (when you use multi‑outbox).

### 6.3 No new transactional boundaries

Fanout **does not create cross‑tenant transactions**:

* Each outbox write participates only in the local database transaction where it was enqueued.
* Multi‑outbox processes messages from many databases, but always one database at a time, using its own `IOutbox` instance.

If you need cross‑tenant invariants, you must model them explicitly (for example, through idempotent compensating actions) instead of relying on the outbox/fanout infrastructure to provide distributed transactions.

### 6.4 Idempotency and at‑least‑once delivery

Because the outbox delivers messages with **at‑least‑once** semantics, fanout inherits that:

* Handlers must be **idempotent**: the same message may be processed more than once.
* When fanout leads to multiple external systems (message brokers, webhooks, email providers), those downstream calls should also be idempotent or tolerate duplicates.

The outbox takes care of retries and error recording; fanout determines where those retries are visible.

---

## 7. Example scenarios

Here are two concrete scenarios that put everything together.

### 7.1 Single‑database: event fanout to internal and external systems

**Scenario:** An order is created in a single‑tenant app.

1. The order service saves the order and enqueues:

   * `order.created` (for a broker and internal projections).
   * `email.order-confirmation` (for email).
2. Outbox background worker claims both messages.
3. Handlers:

   * `OrderCreatedHandler` publishes to the broker.
   * `OrderProjectionHandler` updates a read model.
   * `OrderConfirmationEmailHandler` sends an email.

Fanout happens because **one transaction wrote several outbox messages**, and each message is wired to its own handler(s).

### 7.2 Multi‑tenant: fanout across tenant outboxes

**Scenario:** A hosted SaaS app where each customer has its own database and outbox. You want a single handler implementation that sends billing events to a broker for *all* tenants. This is the kind of problem described in the implementation summary (five customers, each with their own database and outbox, and a single handler for a given event).

1. At write time:

   * Application code uses `IOutboxRouter` to get the tenant’s outbox and enqueues `billing.invoice-created` there.
2. At read time:

   * `MultiOutboxPollingService` uses `IOutboxStoreProvider` and `IOutboxSelectionStrategy` to pick a tenant outbox.
   * `MultiOutboxDispatcher` processes messages via `BillingInvoiceCreatedHandler`.
   * The handler publishes to the billing message broker.

Fanout happens in two dimensions:

* **Across tenants** – the same handler serves all tenant outboxes.
* **Across systems** – each processed message ends up in the external billing system, in addition to being recorded in the tenant database.

The underlying reliability is still provided by each tenant’s outbox.

---

## 8. Related documentation

For more detail on specific components:

* **Outbox Quick Start** – practical setup and usage of `IOutbox` and handlers.
* **Outbox API Reference** – full API surface for `IOutbox`, `IOutboxHandler`, and `IOutboxRouter`.
* **Multi‑Outbox Processing Guide** – deeper dive into `IOutboxStoreProvider`, selection strategies, and dynamic discovery.
* **Outbox Router Usage Guide** – patterns for routing writes to the correct tenant outbox.

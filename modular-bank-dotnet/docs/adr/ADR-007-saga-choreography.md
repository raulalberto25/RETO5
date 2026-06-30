# ADR-007: Distributed Transaction Pattern (Saga Choreography)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Transfers involves multiple microservices:
1. Transfers MS: record transfer (own DB)
2. Accounts MS: debit source, credit target (owns balances)
3. Notifications MS: send notification to user
4. Audit MS: record operation

These services cannot use traditional ACID transactions (not in same DB). A saga pattern is required to coordinate consistent state across services.

### Saga Definition
A saga is a sequence of transactions across multiple services, where:
- Each service completes its local transaction independently
- Compensating transactions handle failures
- Coordination logic ensures eventual consistency

---

## Decision

**Use Saga Choreography pattern.**

Services publish and consume events; no central orchestrator service.

### Flow: Transfer Execution

```
1. Client POST /transfers
   ↓
2. Transfers MS:
   - Validates ownership (HTTP call to Accounts MS)
   - Inserts Transfer + OutboxEntry (single transaction)
   - OutboxWorker publishes TransferExecutedEvent
   ↓
3. Accounts MS consumes TransferExecutedEvent:
   - Debits source account
   - Credits target account
   - Publishes AccountsUpdatedEvent (or error event)
   ↓
4. Notifications MS consumes TransferExecutedEvent:
   - Creates notification record
   - No error recovery needed (side effect)
   ↓
5. Audit MS consumes TransferExecutedEvent:
   - Records audit entry
   - No error recovery needed (side effect)
```

### Rationale

| Criterion | Choreography | Orchestration |
|-----------|--------------|---------------|
| **Coupling** | Loosely coupled (event-driven) | Tightly coupled (to orchestrator) |
| **Failure Handling** | Each service owns compensation | Central orchestrator owns compensation |
| **Testing** | Integration tests complex | Unit tests easier |
| **Debugging** | Distributed tracing needed | Centralized logs |
| **Scalability** | Scales with #services | Orchestrator becomes bottleneck |
| **Complexity** | Distributed complexity | Centralized complexity |

**Chosen: Choreography** because:
1. Loose coupling (services independent)
2. No orchestrator SPOF
3. Scales horizontally
4. Aligns with event-driven architecture goal

---

## Alternative: Orchestration ❌

Dedicated Orchestrator service coordinates saga steps:
- Reads TransferRequested event
- Calls Accounts MS: debit source
- On success: calls Accounts MS: credit target
- On any failure: calls compensating transactions

**Why not:**
- Single point of failure (Orchestrator down = all transfers blocked)
- Orchestrator becomes bottleneck
- Tight coupling (Orchestrator knows all service contracts)
- Difficult to test (mocking multiple services)

**When to use:**
- Complex saga with many conditional branches
- Shared transaction deadlock concerns
- Team prefers centralized control

---

## Compensation Logic

### Success Path
```
Transfer recorded → Both accounts updated → Notifications/Audit created → Client OK
```

### Failure Path: Accounts unavailable

```
Transfer recorded + OutboxEntry
  ↓
Accounts MS down
  ↓
OutboxWorker keeps retrying (Circuit Breaker stops after 5 failures)
  ↓
Accounts MS comes back up
  ↓
OutboxWorker republishes from Outbox
  ↓
Accounts finally receives: debit + credit succeed
  ↓
Notifications/Audit eventually process
```

### Failure Path: Insufficient balance

```
Transfer recorded + OutboxEntry published
  ↓
Accounts MS receives: debit fails (insufficient funds)
  ↓
Accounts publishes AccountsUpdateFailedEvent with error reason
  ↓
Transfers MS consumes failure event:
  - Can publish refund/compensation if needed
  - Updates Transfer status to FAILED
  - Notifies user via existing notification system
```

---

## Event Semantics

**At-Least-Once Delivery:**
- Consumer receives event at least once (may receive duplicates)
- Accounts MS must be idempotent: debit same transfer twice = side effect
- Solution: OutboxEntry tracks confirmation ID, consumer deduplicates

**Eventually Consistent:**
- User sees transfer recorded immediately (Transfer table)
- Accounts balance updates within seconds (Accounts MS processes event)
- Notification sent within seconds
- Audit record within seconds
- Acceptable for banking (not real-time settlement)

---

## Consequences

### Positive

✅ **Loose Coupling**
- Services don't know about each other
- Only dependency: event contract (CloudEvents schema)
- Can add/remove consumers without affecting producers

✅ **Scalability**
- No central orchestrator bottleneck
- Each service scales independently
- Consumers can scale to handle event volume

✅ **Resilience**
- Broker decoupling: service can be temporarily down
- Outbox pattern: no event loss
- Automatic retry via OutboxWorker

✅ **Testability (Eventually)**
- Integration tests: publish event, assert all services react
- Consumer tests: publish event, verify state changes
- Producer tests: publish event, verify outbox entry

### Negative

❌ **Distributed Complexity**
- Eventual consistency harder to reason about than ACID
- Compensation logic must be explicitly coded
- Debugging across services requires distributed tracing

❌ **Testing Difficulty**
- Unit tests isolated; integration tests require all services
- Hard to test failure scenarios (need to simulate broker/service failures)
- Contract testing needed (event schemas must match)

❌ **Observability Required**
- Must implement distributed tracing (OpenTelemetry)
- Must monitor consumer lag
- Must alert on repeated failures

---

## Implementation Checklist

- [ ] Define TransferExecutedEvent (CloudEvents format)
- [ ] Implement OutboxEntry + OutboxWorker in Transfers MS
- [ ] Implement Accounts consumer (processes TransferExecutedEvent)
- [ ] Implement Notifications consumer (publishes notifications)
- [ ] Implement Audit consumer (publishes audit entries)
- [ ] Idempotency: OutboxEntry tracks confirmation, consumer deduplicates
- [ ] Failure handling: AccountsUpdateFailedEvent + compensation
- [ ] Distributed tracing: W3C TraceContext across services
- [ ] Integration tests: full saga paths (happy + failure)

---

## Related Decisions

**Depends On:**
- ADR-002 (Transfers extraction)
- ADR-004 (RabbitMQ as event broker)

**Enables:**
- ADR-010 (CloudEvents event contract)
- ADR-009 (Distributed tracing for saga visibility)

---

## References
- **Pattern:** Saga (Chris Richardson, Building Microservices)
- **Choreography Model:** Event-Driven Architecture (O'Reilly)
- **Outbox Pattern:** Vaughn Vernon, IDDD

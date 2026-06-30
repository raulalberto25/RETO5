# ADR-006: Transfers Microservice Database (PostgreSQL)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Transfers MS requires a dedicated database with additional architectural complexity:
- Must record transfers with strong consistency
- Must support Outbox pattern (atomic: Transfer + OutboxEntry in single transaction)
- Must support Saga choreography (eventual consistency across services)
- Must handle high-volume write operations (every transfer = write)

### Unique Requirements
- Outbox table: guaranteed event delivery even if broker crashes
- Foreign keys optional (Accounts/Notifications are separate services)
- Must support transactional guarantees (Outbox atomicity)

---

## Decision

**Use PostgreSQL as Transfers MS exclusive database.**

Same as Accounts MS, optimized for transaction handling and Outbox pattern requirements.

### Rationale

1. **Outbox Pattern Requirement**
   - Outbox pattern requires atomicity: single transaction commits Transfer + OutboxEntry
   - PostgreSQL's ACID guarantees this natively
   - Must be relational for transactional semantics

2. **Saga Coordination**
   - Transfer publishing must be 100% reliable (financial domain)
   - Outbox prevents "lost event" scenarios
   - PostgreSQL's durability (fsync to disk) essential
   - Transaction isolation levels prevent race conditions

3. **Consistency Model**
   - Transfers table: strong consistency (ACID)
   - Event propagation: eventual consistency (async via broker)
   - PostgreSQL handles strong consistency tier perfectly

4. **Team Familiarity**
   - Same database technology as Accounts MS
   - Operational knowledge transfers directly
   - Single monitoring/backup strategy for both services

---

## Consequences

### Positive

✅ **Guaranteed Event Delivery**
- Outbox table stores events in same transaction as transfer
- OutboxWorker republishes unconfirmed entries
- No events lost even if broker unavailable

✅ **Operational Consistency**
- Both MS1 (Accounts) and MS2 (Transfers) use PostgreSQL
- Single backup/recovery strategy
- Unified monitoring approach

✅ **Clear Separation**
- Transfers DB separate from Accounts DB (Database-per-Service)
- Transfers: source of truth for transfer records
- No cross-database foreign keys (maintains independence)

### Negative

❌ **Outbox Complexity**
- Requires background worker (OutboxWorker) to process outbox entries
- Additional operational component to monitor
- Slightly higher database load (extra table, polling)

---

## Implementation Details

### Schema

```sql
CREATE TABLE transfers (
    id UUID PRIMARY KEY,
    source_account_id UUID NOT NULL,
    target_account_id UUID NOT NULL,
    amount numeric(19,4) NOT NULL,
    reference TEXT,
    created_at TIMESTAMP DEFAULT NOW(),
    INDEX idx_source_account_id (source_account_id),
    INDEX idx_target_account_id (target_account_id)
);

CREATE TABLE outbox_entries (
    id UUID PRIMARY KEY,
    aggregate_id UUID NOT NULL,  -- transfer ID
    event_type VARCHAR(255) NOT NULL,
    payload JSONB NOT NULL,
    published_at TIMESTAMP NULL,
    created_at TIMESTAMP DEFAULT NOW(),
    INDEX idx_unpublished (published_at)
);
```

### Transactional Guarantee

```csharp
// Single transaction: Transfer + Outbox entry
using var transaction = await db.Database.BeginTransactionAsync();

try
{
    // Write transfer
    db.Transfers.Add(transfer);
    await db.SaveChangesAsync();
    
    // Write outbox entry (same transaction)
    var outboxEntry = new OutboxEntry
    {
        AggregateId = transfer.Id,
        EventType = "transfer.executed",
        Payload = JsonSerializer.Serialize(transferEvent)
    };
    db.OutboxEntries.Add(outboxEntry);
    await db.SaveChangesAsync();
    
    // Commit both or neither
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

---

## Related Decisions

**Depends On:**
- ADR-002 (Transfers extraction)
- ADR-007 (Saga choreography pattern, requires Outbox)

**Enables:**
- Guaranteed event delivery (Outbox pattern)
- Multi-step saga coordination

---

## References
- **Pattern:** Outbox (Event Sourcing) — Vaughn Vernon
- **Database:** PostgreSQL 16

# ADR-005: Accounts Microservice Database (PostgreSQL)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Accounts MS requires a dedicated database following the Database-per-Service pattern. Key requirements:
- Must support strong ACID transactions (banking domain)
- Must handle concurrent balance updates safely
- Must support value objects (Money, AccountId)
- Must enable zero-downtime migration from monolith's existing `accounts.*` schema
- Team has existing PostgreSQL expertise (used in monolith)

### Data Model
- Table: `accounts` (id, user_id, account_number, balance, created_at)
- Indexes: user_id (ownership lookup), account_number (unique identifier)
- Column type for balance: `numeric(19,4)` (precise decimal for monetary values)

---

## Decision

**Use PostgreSQL as Accounts MS exclusive database.**

Same technology as the monolith, but separate instance with dedicated schema.

### Rationale

| Criterion | PostgreSQL | MySQL | SQLite | MongoDB | NoSQL |
|-----------|------------|-------|--------|---------|-------|
| **ACID Transactions** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐ |
| **Data Consistency** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ |
| **Team Expertise** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| **Production Readiness** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| **Scale Compatibility** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Backup/Recovery** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Cost** | Free | Free | Free | Free/Paid | Free/Paid |

### Specific Advantages

1. **Zero-Downtime Migration**
   - Data already exists in monolith's `accounts` schema
   - Can copy data from existing tables with minimal downtime
   - Dual-write strategy compatible (monolith writes to both, then switches)
   - Foreign key relationships already established

2. **Banking Domain Requirements**
   - ACID transactions essential for balance updates
   - Concurrent debit/credit operations must be serializable
   - PostgreSQL's row-level locking prevents lost updates
   - Strong consistency guarantees (no eventual consistency issues)

3. **Team Knowledge & Operations**
   - Team already runs PostgreSQL 16 (existing monolith)
   - Operational expertise available (backup, monitoring, patching)
   - Same connection string format, authentication, tooling
   - No additional learning curve for ops team

4. **EF Core Native Support**
   - Npgsql (Entity Framework Core provider) is production-grade
   - Same as monolith: `UseNpgsql(connectionString)`
   - Value object support via `HasConversion()` (Money → decimal)
   - JSON columns if needed (JSONB support)

5. **Simple Architecture**
   - No polyglot persistence complexity
   - No need to learn NoSQL query patterns
   - Relational data model maps cleanly to banking domain
   - Schema-per-service still possible (separate PostgreSQL database/schema)

---

## Alternative Options Considered

### Option A: MySQL ❌
**Why Not Recommended:**
- Similar to PostgreSQL, but PostgreSQL advantages:
  - Better ACID semantics (MySQL InnoDB limitations)
  - More advanced locking (row-level, better concurrency)
  - JSON/JSONB support superior
  - Replication more robust
- MySQL viable for new team, but PostgreSQL better

**When to use instead:**
- Team has MySQL expertise (not our case)
- Hosting constraints require MySQL

### Option B: SQLite ❌
**Why Not Recommended:**
- Cannot be used in distributed system (file-based, single-writer)
- Accounts MS needs shared database for multiple instances (Phase 3+)
- No support for concurrent writer processes (locking issues)
- Not suitable for production microservices

**When to use instead:**
- Development environment (local testing only)
- Embedded single-user applications

### Option C: MongoDB (NoSQL) ❌
**Why Not Recommended:**
- **Critical issue:** Weaker ACID semantics (no distributed transactions pre-MongoDB 4.0)
- Even with multi-document ACID: no equivalent to PostgreSQL's row-level locking
- Race condition risk: concurrent balance updates could lose increments
- Example failure:
  ```
  Thread 1: read balance=1000, add 100 → 1100
  Thread 2: read balance=1000, add 50 → 1050 (writes, overwriting Thread 1)
  Final: balance=1050 (lost 100)
  ```
- Schema flexibility unnecessary (accounts table is stable)
- NoSQL adds operational complexity without benefit

**When to use instead:**
- Highly flexible schema (evolving rapidly)
- Unstructured data (documents, graphs)
- Horizontal scaling prioritized over consistency

### Option D: Cassandra (NoSQL) ❌
**Why Not Recommended:**
- Designed for write-heavy, immutable time-series (not our profile)
- Eventual consistency model incompatible with banking (balance must be accurate immediately)
- Weak transactional guarantees
- Operational complexity (ring architecture, rebalancing)

**When to use instead:**
- Event logging at massive scale
- Time-series data (metrics, logs)

---

## Consequences

### Positive

✅ **Data Safety**
- ACID transactions guarantee consistency
- Concurrent updates serialized correctly
- No lost updates or phantom reads
- Regulatory compliance (strong consistency)

✅ **Team Confidence**
- Familiar technology (already operational in monolith)
- Existing backup/restore procedures applicable
- Monitoring tools same as monolith
- Debugging/troubleshooting familiar

✅ **Migration Simplicity**
- Copy schema from monolith: same structure, same data types
- Dual-write achievable with monolith's EF Core
- Backfill straightforward (INSERT/SELECT)
- Rollback simple (stop writes, restore from backup)

✅ **Query Familiarity**
- SQL queries same style as monolith
- No impedance mismatch (ORM translates cleanly)
- Performance optimization familiar (indexes, query plans)

✅ **Cost**
- Open source (no licensing)
- Modest resource footprint
- Docker image readily available

### Negative

❌ **Polyglot Persistence Not Possible**
- If future microservice needs different data model (document, graph, time-series): must accept architectural debt
- Switching technologies later requires migration
- Lock-in to relational model

❌ **Horizontal Scaling Complexity**
- PostgreSQL sharding not built-in (vs Cassandra, MongoDB)
- If Accounts table grows beyond single node: requires manual sharding strategy
- Unlikely in Phase 1-2, but long-term consideration

❌ **NoSQL Features Unavailable**
- If future need for graph queries (relationship analysis): requires redesign
- Schemaless evolution not available (migrations required for schema changes)
- Acceptable given current domain

---

## Implementation Details

### Database Setup

```sql
-- Create dedicated database for Accounts MS
CREATE DATABASE finbank_accounts 
  WITH ENCODING 'UTF8' 
  LOCALE 'en_US.UTF-8';

-- Connection string for Accounts MS
-- Host=postgres-accounts;Port=5432;Database=finbank_accounts;Username=bank;Password=bank
```

### EF Core Schema

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.Entity<Account>(builder =>
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.UserId).IsRequired().HasIndex();
        builder.Property(a => a.AccountNumber).IsRequired().HasMaxLength(20).HasIndex(isUnique: true);
        builder.Property(a => a.Balance).HasColumnType("numeric(19,4)").IsRequired();
        builder.Property(a => a.CreatedAt).HasDefaultValueSql("now()");
        
        // Value object conversion
        builder.Property(a => a.Balance)
            .HasConversion(money => money.Amount, amount => Money.Of(amount));
    });
}
```

### Durability Configuration

```csharp
// Enable durable writes (fsync to disk)
// Default PostgreSQL configuration is safe for transactions
// Connection pooling recommended (Npgsql default: 30 connections)
var options = new NpgsqlDbContextOptionsBuilder()
    .CommandTimeout(30)
    .UseAdminDatabase(false); // Separate user for Accounts MS
```

### Backup Strategy

- Daily full backups via `pg_dump`
- Transaction logs for point-in-time recovery
- 30-day retention
- Tested recovery process (quarterly)

---

## Scaling Considerations (Phase 3+)

If Accounts table grows beyond single-node capacity:

**Option 1: Vertical Scaling (Easier)**
- Increase PostgreSQL instance resources (CPU, RAM, disk)
- Feasible until 10TB+ (typical banking scale)

**Option 2: Read Replicas**
- Deploy read-only PostgreSQL replicas
- Route read-heavy queries (FindByOwner) to replicas
- Writes still go to primary

**Option 3: Sharding (Complex)**
- Shard by UserId (hash of user ID determines partition)
- Requires application-level shard routing
- Acceptable if single-node saturated (unlikely)

**Recommendation:** Stay with single node + replicas until clear scaling limit reached.

---

## Related Decisions

**Depends On:**
- ADR-001 (Accounts extraction as MS1)
- ADR-011 (Zero-downtime migration strategy)

**Enables:**
- PostgreSQL-specific features (JSONB for future features)
- Standard backup/restore procedures

---

## References
- **PostgreSQL:** https://www.postgresql.org/
- **EF Core Provider:** https://www.npgsql.org/efcore/
- **Docker Image:** `postgres:16`
- **Banking Schema:** Industry best practices for monetary columns (ISO 20022)

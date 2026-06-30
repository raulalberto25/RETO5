# ADR-011: Zero-Downtime Data Migration Strategy (Dual-Write → Backfill → Cutover)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Accounts module currently exists in monolith's shared `accounts` schema. Extracting to standalone Accounts MS requires migrating data from monolith's PostgreSQL to Accounts MS's dedicated PostgreSQL **without service downtime**.

### Challenge
- Cannot stop the system during migration (banking domain: 24/7 availability expected)
- Cannot lose data (regulatory requirement)
- Cannot have inconsistency window (both DBs out of sync)
- Must be reversible (if Accounts MS fails, can fall back to monolith)

### Data to Migrate
- Accounts table: ~N accounts with owner (UserId), balances, metadata
- Must maintain referential integrity (UserId references Auth DB users)

---

## Decision

**Use Three-Phase Strategy: Dual-Write → Backfill → Read Switchover → Stop Dual-Write**

### Phase 1: Dual-Write (Compatibility Mode)

```
Monolith writes to:
  ├─ Original DB (accounts schema) ← existing
  └─ Accounts MS DB (new instance) ← new, via HTTP
     (via HttpAccountsService)
```

**Implementation:**
1. Deploy Accounts MS (empty database)
2. Deploy Gateway (routes `/accounts/**` to Accounts MS)
3. Monolith: switch to HttpAccountsService immediately
4. HttpAccountsService: writes to Accounts MS via HTTP

**Guarantees:**
- Accounts MS handles all new writes
- No data loss
- Monolith reads from Accounts MS (via HTTP)
- Can roll back: switch back to in-process AccountsService anytime

**Duration:** 1-24 hours (depends on current data volume)

### Phase 2: Backfill (Historical Data)

```
While Phase 1 running:

Backfill job:
  ├─ Read: Accounts from monolith DB (accounts schema)
  ├─ Filter: Already in Accounts MS DB (by ID)
  ├─ Write: Missing accounts to Accounts MS DB
  └─ Verify: Counts match
```

**Implementation:**
```csharp
// Run as one-time job (can be automated or manual)
var monolithAccounts = await monolithDb.Accounts.ToListAsync();
var accountsServiceAccounts = await accountsServiceDb.Accounts
    .Select(a => a.Id)
    .ToListAsync();

var missing = monolithAccounts
    .Where(a => !accountsServiceAccounts.Contains(a.Id))
    .ToList();

foreach (var account in missing)
{
    accountsServiceDb.Accounts.Add(account);
}

await accountsServiceDb.SaveChangesAsync();
```

**Verification:**
```csharp
var monolithCount = await monolithDb.Accounts.CountAsync();
var accountsServiceCount = await accountsServiceDb.Accounts.CountAsync();
Assert.Equal(monolithCount, accountsServiceCount);
```

**Duration:** Minutes to hours (depends on account count and network latency)

### Phase 3: Read Switchover (Optional, for Performance)

```
Monolith switches from:
  ├─ Debit/Credit: already via HTTP (Accounts MS)
  └─ Read (FindByOwner): can stay via HTTP (already is)
```

**Technically:** No additional change needed (already reading via HTTP)

**Can optionally:** Cache Accounts MS queries in monolith for performance

### Phase 4: Stop Dual-Write (Final Cleanup)

```
Once Accounts MS database verified stable (24+ hours):
  ├─ Stop writing to monolith DB accounts schema
  ├─ Keep monolith DB for reference/audit only
  ├─ Verify monolith DB accounts not accessed
  └─ Can archive/drop after 30-day safety period
```

**Rollback Window:** 30 days (keep old data, can restore if critical issue found)

---

## Alternative Strategies Considered

### Option A: Big Bang Cutover ❌

"Migrate all data, then flip the switch"

**Why not:**
- Risk: if migration fails, entire system down
- No rollback plan (old system already shut down)
- Data loss if sync incomplete
- Unacceptable for banking domain (24/7)

### Option B: Dual-Write with Version Flags ❌

"Keep monolith code, add flag to write both DBs"

**Why not:**
- More complex (flag logic spread across codebase)
- Cannot revert cleanly (flag logic stays forever)
- Easier to accidentally forget to clean up

**Better:** Extract HTTP layer (HttpAccountsService) - cleaner, simpler

### Option C: Event Sourcing Replay ❌

"Rebuild Accounts MS state from audit logs"

**Why not:**
- No event sourcing currently (monolith is transactional)
- Would require adding event sourcing to monolith first
- Extra work, extra complexity

**Better:** Direct copy, dual-write during transition

---

## Failure Scenarios & Recovery

### Scenario 1: Accounts MS DB Corruption During Backfill

**Detection:**
```csharp
// Verification query
var monolithIds = await monolithDb.Accounts.Select(a => a.Id).ToListAsync();
var accountsServiceIds = await accountsServiceDb.Accounts.Select(a => a.Id).ToListAsync();

var missing = monolithIds.Except(accountsServiceIds).ToList();
var extra = accountsServiceIds.Except(monolithIds).ToList();

if (missing.Any() || extra.Any())
    throw new InvalidOperationException("Data mismatch detected!");
```

**Recovery:**
1. Drop Accounts MS DB (with proper backup)
2. Restore from backup or recreate
3. Re-run backfill
4. No service downtime (monolith still has original data)

### Scenario 2: Accounts MS Unavailable After Switchover

**Issue:** Monolith depends on Accounts MS via HTTP

**Prevention:**
1. Circuit breaker + fallback (if available)
2. Rollback plan: switch back to in-process service (with old data)
3. Health checks before declaring migration complete

**Recovery (if needed):**
```csharp
// Revert to in-process service
services.AddAccountsModule(connectionString);  // Instead of HttpAccountsService
```

### Scenario 3: Balance Mismatch Detected After Migration

**Example:** Account shows balance=1000 in monolith, 1005 in Accounts MS

**Investigation:**
```csharp
// Check monolith audit log
var monolithAudits = await monolithDb.AuditEntries
    .Where(a => a.Action.Contains("DEBIT") || a.Action.Contains("CREDIT"))
    .OrderBy(a => a.CreatedAt)
    .ToListAsync();

// Compare timestamps: did migration happen mid-transaction?
```

**Prevention:**
- Run migration during low-traffic window (e.g., 2-4 AM)
- Pause incoming requests briefly during backfill if high volume detected

---

## Rollback Steps (If Critical Issue Found)

1. **Immediate:** Switch monolith back to in-process AccountsService
   ```csharp
   // services.AddHttpAccountsService(endpoint);
   services.AddAccountsModule(connectionString);  // Restore
   ```

2. **Inform Users:** "Brief service interruption, now restored"

3. **Investigate:** Compare data in both databases

4. **Retry:** Fix issue in Accounts MS, restart migration

---

## Success Criteria

✅ **Zero Downtime**
- No user-facing errors during migration
- No failed transfers during cutover
- No lost data

✅ **Data Integrity**
- Account counts match
- Balance sums match
- No accounts duplicated
- No accounts deleted

✅ **Reversibility**
- Can roll back to monolith-only within 30 days
- Old data preserved for audit

✅ **Observability**
- Logs show all migration steps
- Metrics show no spike in errors during cutover
- Tracing shows normal latency

---

## Implementation Timeline

| Phase | Duration | Action | Rollback? |
|-------|----------|--------|-----------|
| Prepare | 1 hour | Deploy Accounts MS, update routes | Easy (no production data yet) |
| Dual-Write | 1-24 hours | Monolith writes to both DBs | Easy (all data still in monolith) |
| Backfill | 10 mins - 2 hours | Copy historical data | Easy (retry backfill) |
| Read Switch | Immediate | Monolith reads from Accounts MS | Already done (HTTP) |
| Stabilize | 24+ hours | Monitor for issues | Easy (30-day window) |
| Cleanup | 30 days | Archive old Accounts DB | Hard (data deleted) |

---

## Monitoring During Migration

**Metrics to Watch:**
- HTTP error rate (Accounts MS health)
- Database replication lag (if used)
- Account creation latency (write path)
- Account read latency (read path)

**Alerts:**
- If error rate > 1%: automatic rollback
- If latency P99 > 2s: investigate
- If data mismatch detected: pause migration

---

## Documentation for Operations Team

**Migration Runbook:**
```markdown
# Accounts MS Migration Runbook

## Pre-Migration Checklist
- [ ] Accounts MS deployed and healthy
- [ ] Backfill job prepared
- [ ] Rollback procedure tested
- [ ] Team on-call ready

## Execution Steps
1. Deploy HttpAccountsService to monolith
2. Monitor error rates (should stay < 1%)
3. Run backfill job
4. Verify account counts match
5. Monitor for 24 hours
6. Schedule cleanup (30-day hold)

## Rollback (if needed)
1. Revert monolith to in-process AccountsService
2. Test transfer operation
3. Verify balance consistency
4. Notify stakeholders
```

---

## Related Decisions

**Depends On:**
- ADR-001 (Accounts extraction)
- ADR-005 (PostgreSQL for Accounts MS)

**Enables:**
- Phase 1 completion (Accounts MS live)
- Phase 2 readiness (Transfers depends on stable Accounts MS)

---

## References
- **Pattern:** Strangler Fig (Michael Feathers)
- **Pattern:** Blue-Green Deployment
- **Database Migration:** PostgreSQL pg_dump, pg_restore
- **Monitoring:** Prometheus metrics + Grafana dashboards

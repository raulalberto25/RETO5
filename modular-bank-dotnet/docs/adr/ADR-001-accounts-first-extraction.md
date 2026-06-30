# ADR-001: Selection of First Module for Microservices Extraction (Accounts)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

FinBank is transitioning from a monolithic architecture to microservices using the Strangler Fig pattern. This requires selecting which module to extract first. The choice will set the precedent for subsequent extractions and determine the learning curve and technical debt addressed early.

### Current State
- Monolithic modular architecture with 5 modules: Auth, Accounts, Transfers, Notifications, Audit
- All modules share a single PostgreSQL database with schema-per-module design
- In-process communication via .NET interfaces
- No distributed transaction handling or saga patterns
- No async messaging infrastructure

### Constraints
- Must maintain zero-downtime during extraction
- Must preserve exact HTTP contract compatibility
- Must avoid cascading failures across modules
- Must establish patterns reusable for subsequent extractions

---

## Decision

**Extract the Accounts module as the first microservice (MS1).**

### Rationale

| Criterion | Accounts | Transfers | Notifications | Audit |
|-----------|----------|-----------|----------------|-------|
| **Autonomy** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| **Dependencies** | None | High (Accounts) | High (Transfers) | High (Transfers) |
| **Traffic Volume** | Very High | High | Medium | Medium |
| **Data Isolation** | Clean | Mixed | Mixed | Mixed |
| **Domain Clarity** | Crystal clear | Well-defined | Clear | Clear |
| **Feasibility** | ✅ | Blocked by Accounts | Blocked by Transfers | Blocked by Transfers |

### Specific Advantages

1. **No Upstream Dependencies**
   - Accounts has no dependencies on other modules
   - Can be extracted independently without cascading changes
   - Reduces risk and complexity of first extraction

2. **High Autonomy**
   - Core banking domain: accounts, balances, account numbers
   - Clear, bounded bounded context
   - Well-defined interfaces (IAccountsService)
   - No cross-module transactions

3. **Foundation for Others**
   - Transfers depends on Accounts → must extract Accounts first
   - Notifications depends on Transfers → second extraction
   - Creates natural ordering for subsequent work

4. **Traffic & Data Volume**
   - Accounts is accessed by every transfer operation
   - High traffic justifies extraction (scalability benefits)
   - Schema is clean, manageable data migration

5. **Learning Opportunity**
   - Simplest extraction pattern (no Saga, no messaging yet)
   - Establishes HTTP client pattern for monolith-to-MS calls
   - Low complexity → higher success probability

---

## Alternative Options Considered

### Option A: Extract Transfers First ❌
- **Why Not:** Transfers depends on Accounts via IAccountsService
- Would require stubbing Accounts behavior → incorrect semantics
- Cannot extract dependency-first; violates clean architecture principles
- High risk of data inconsistency

### Option B: Extract Notifications First ❌
- **Why Not:** Has no clear domain independence
- Notifications is a side effect of transfers, not a bounded context
- Extraction would leave Transfers as monolith-only, defeating the purpose
- Harder to justify to stakeholders (lower business value visible immediately)

### Option C: Extract Audit First ❌
- **Why Not:** Pure infrastructure concern, not a domain boundary
- No tangible benefits to extracting audit separately
- Would still require cross-module calls from remaining monolith
- Does not establish reusable patterns

---

## Consequences

### Positive
✅ **Clear Migration Path**
- Monolith → calls Accounts MS via HTTP (HttpAccountsService adapter)
- Sets precedent for Transfers extraction (will depend on Accounts MS)

✅ **Reduced Complexity**
- No distributed transactions needed yet
- No messaging/event infrastructure required in Phase 1
- Synchronous HTTP calls are simpler to debug and monitor

✅ **Early ROI**
- Scalability: Accounts can be scaled independently (high-traffic module)
- Database per service: Accounts can have its own PostgreSQL instance
- Foundation for event-driven architecture (Transfers → Events → Notifications/Audit)

✅ **Risk Mitigation**
- Smallest blast radius if extraction fails
- Can roll back cleanly without affecting transfers
- Monolith remains fully functional during extraction

### Negative
❌ **Partial Architecture**
- Phase 1 will have hybrid monolith + 1 MS (not yet "microservices" in plural)
- Full benefits (async messaging, saga) deferred to Phase 2+

❌ **Dependency in Monolith**
- Monolith (Transfers) must depend on Accounts MS via HTTP
- Introduces network latency where previously in-process
- Requires resilience patterns (circuit breaker, retry)

❌ **Data Consistency Transition**
- During migration, must handle dual-writes (monolith DB + Accounts MS DB)
- Temporary complexity in deployment steps

---

## Implementation Checklist

- [ ] Create `services/accounts-service/` with Hexagonal architecture
- [ ] Implement `IAccountsRepository` port (hexagonal)
- [ ] Create `HttpAccountsService` in monolith (adapter pattern)
- [ ] Set up dual-write during migration
- [ ] Configure `docker-compose` with separate PostgreSQL instances
- [ ] Implement zero-downtime cutover (backfill → read switch)
- [ ] Add HTTP client resilience (circuit breaker, retry) in monolith

---

## References
- **Related ADRs:** ADR-002 (Transfers), ADR-003 (Gateway), ADR-011 (Zero-Downtime Migration)
- **Pattern:** Strangler Fig (Michael Feathers)
- **Architecture:** Hexagonal (Ports & Adapters)

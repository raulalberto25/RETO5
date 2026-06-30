# ADR-002: Selection of Second Module for Extraction (Transfers)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Following the successful extraction of Accounts as MS1, the next phase requires selecting the second microservice. This extraction must introduce event-driven architecture, saga pattern for distributed transactions, and async messaging—core requirements of the modernization challenge.

### State at Phase 2
- Accounts is now a standalone microservice
- Monolith still contains: Auth, Transfers, Notifications, Audit
- Monolith calls Accounts via HTTP (HttpAccountsService)
- No messaging infrastructure yet deployed

### Design Goals for Phase 2
1. Introduce event-driven communication (RabbitMQ, Kafka, etc.)
2. Implement saga pattern for distributed transactions
3. Establish resilience patterns (circuit breaker, outbox, retry)
4. Enable async data propagation to side-effect modules (Notifications, Audit)

---

## Decision

**Extract the Transfers module as the second microservice (MS2).**

### Rationale

| Criterion | Transfers | Notifications | Audit |
|-----------|-----------|----------------|-------|
| **Business Criticality** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| **Async Messaging Need** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| **Saga Pattern Fit** | Perfect | Not needed | Not needed |
| **Event Generation** | Primary | Secondary | Secondary |
| **Tech Debt** | Significant | Minimal | Minimal |
| **Readiness** | ✅ After Accounts | - | - |

### Specific Advantages

1. **Primary Event Source**
   - Transfers is the main business event generator in banking
   - Natural place to publish `TransferExecutedEvent` to event broker
   - Notifications and Audit become event consumers (decoupled)
   - Establishes event-driven patterns required by challenge

2. **Saga Pattern Justification**
   - Transfer operation spans multiple services:
     - Debit source account (Accounts MS)
     - Credit target account (Accounts MS)
     - Record transfer (Transfers MS)
     - Notify user (Notifications MS) — async via event
     - Audit operation (Audit MS) — async via event
   - Multi-step orchestration requires saga for eventual consistency
   - Choreography model fits: Transfers publishes, others consume

3. **Dependency Flow**
   - Transfers depends on Accounts MS (already extracted) ✅
   - Transfers publishes events → Notifications and Audit subscribe
   - Monolith becomes pure event consumer (side effects)
   - Clean dependency: extraction order = dependency order

4. **Measurable Business Value**
   - Transfers is the core banking operation
   - Scaling Transfers independently improves throughput
   - Resilience patterns protect most critical path
   - Observable business metrics (transfer latency, success rate)

5. **Architecture Milestone**
   - First implementation of event-driven architecture
   - Establishes messaging infrastructure (RabbitMQ) reusable for future modules
   - Tests saga choreography pattern at scale

---

## Alternative Options Considered

### Option A: Extract Notifications First ❌
- **Why Not:** Notifications is a side effect, not a primary aggregate
- Cannot be extracted independently (only triggered by Transfers)
- Extraction would leave Transfers in monolith → defeats the purpose
- No messaging pattern established if Notifications stays as consumer only
- Would require stubbing transfer events (unrealistic)

### Option B: Extract Audit First ❌
- **Why Not:** Audit is infrastructure/cross-cutting concern, not domain boundary
- Has no independent business logic
- Could be refactored in-process before even considering extraction
- Does not justify architectural complexity of async messaging
- Similar issue as Notifications: extraction isolated without value

### Option C: Remain Monolithic ❌
- **Why Not:** Violates challenge requirements
- Saga pattern and event-driven architecture cannot be demonstrated
- No basis for distributed transaction handling
- Observability across service boundaries unmeasurable

---

## Consequences

### Positive

✅ **Event-Driven Foundation**
- Establishes RabbitMQ (or similar) infrastructure
- CloudEvents contract for all events (scalable, standardized)
- Reusable messaging patterns for future modules

✅ **Distributed Transaction Pattern**
- Saga choreography demonstrated end-to-end
- Outbox pattern ensures at-least-once delivery
- Compensation logic for failure scenarios

✅ **Monolith Simplification**
- Transfers extracted → Monolith no longer orchestrates complex flow
- Monolith becomes pure event consumer (Notifications, Audit)
- Reduced cognitive load in remaining monolith

✅ **Resilience Foundation**
- Circuit breaker for HTTP calls to Accounts MS
- Retry policies with exponential backoff
- Outbox guarantees delivery even if broker temporarily unavailable

✅ **Observability Milestone**
- End-to-end tracing across 2+ services
- Distributed transaction visibility
- Business metrics (transfer success rate, latency P99)

### Negative

❌ **Network Latency**
- Accounts debit/credit → now network calls instead of in-process
- P99 latency will increase (typical HTTP: 50-200ms)
- Must set timeout policies to prevent cascading delays

❌ **Operational Complexity**
- Introduces RabbitMQ as new operational dependency
- Requires monitoring broker health and consumer lag
- Deployment complexity increases (3 services instead of 2)

❌ **Data Consistency Trade-off**
- Eventual consistency instead of strong consistency
- Notifications and Audit might lag transfers by seconds
- Requires compensating transactions for failure scenarios

❌ **Temporary Tech Debt**
- Monolith must still host Notifications and Audit as event consumers
- During Phase 2, monolith = event consumer (not ideal long-term)
- Full decoupling deferred to Phase 3+ (when Notifications/Audit extracted)

---

## Implementation Checklist

- [ ] Design Saga choreography flow (Transfers → Event → Accounts/Notifications/Audit)
- [ ] Create `services/transfers-service/` with hexagonal architecture
- [ ] Implement `IAccountsPort` (HTTP to Accounts MS)
- [ ] Implement `IEventPublisher` port with RabbitMQ adapter
- [ ] Create `OutboxEntry` + `OutboxWorker` for guaranteed delivery
- [ ] Define `TransferExecutedEvent` in CloudEvents format
- [ ] Add event consumers in monolith (Notifications, Audit)
- [ ] Implement Polly resilience policies (circuit breaker, retry)
- [ ] Configure RabbitMQ exchanges, queues, bindings in docker-compose
- [ ] Set up distributed tracing (W3C TraceContext propagation)
- [ ] Document saga happy path and failure paths

---

## Failure Scenarios & Mitigation

| Scenario | Risk | Mitigation |
|----------|------|-----------|
| Accounts MS unavailable | Transfer fails | Circuit breaker + fallback (HTTP 503) |
| RabbitMQ unavailable | Event loss | Outbox pattern + replay |
| Notifications consumer crashes | Notifications missing | Durable queue + manual replay capability |
| Network partition | Split-brain | Use heartbeat monitoring + declare data loss acceptable |

---

## Related Decisions

**Depends On:**
- ADR-001: Accounts extraction (Transfers depends on Accounts MS)
- ADR-007: Saga choreography pattern
- ADR-004: RabbitMQ message broker

**Enables:**
- ADR-009: Observability stack (distributed tracing)
- ADR-010: Event contract format (CloudEvents)
- Future extractions: Notifications, Audit (as event consumers)

---

## References
- **Pattern:** Saga (Choreography model) — Chris Richardson
- **Pattern:** Outbox — Vaughn Vernon, IDDD
- **Event Standard:** CloudEvents 1.0 (CNCF)
- **Related ADRs:** ADR-001, ADR-003, ADR-004, ADR-007, ADR-011

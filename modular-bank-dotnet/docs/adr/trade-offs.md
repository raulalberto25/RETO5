# Architectural Trade-Offs Analysis

**Date:** 2026-06-29

---

## Overview

This document analyzes key trade-offs made during the modernization of FinBank from monolithic to microservices architecture. Each decision sacrificed something to gain something else.

---

## 1. Eventual Consistency vs. Strong Consistency

### Decision
**Chosen:** Eventual Consistency (Saga Choreography pattern with event-driven architecture)

### Trade-Off Analysis

#### What We Sacrificed: Strong Consistency
- Account balances may lag transfers by 1-10 seconds
- User sees "transfer recorded" immediately, but balance update delayed
- Audit entry might lag notification

#### What We Gained: Resilience & Scalability
✅ **Resilience:**
- If Accounts MS temporarily down: transfer still recorded (via Outbox)
- Events retry via OutboxWorker (no manual intervention)
- No cascading failures across services

✅ **Scalability:**
- Services scale independently (no distributed lock contention)
- Transfers MS doesn't block waiting for Accounts MS response
- Notifications/Audit asynchronous (no slow path)

✅ **Operational Simplicity:**
- No distributed transaction coordinator needed
- No global lock management
- Simpler failure recovery

#### Why This Trade-Off Is Acceptable

**Banking Domain Reality:**
- Modern banking already accepts eventual consistency (SWIFT transfers take days)
- Real-world: notification delay 5-10 seconds is acceptable
- Regulatory: focus is on accuracy (eventual correctness), not speed

**Example Scenario:**
```
User transfers $1000 at 10:00:05
- T+0s: Transfer recorded (user sees "success")
- T+2s: Accounts balance updated (Accounts MS processed event)
- T+3s: Notification sent (user informed)
- T+4s: Audit recorded (compliance recorded)

Total: eventual consistency within 4 seconds
(vs monolith: ACID within 100ms, but much more complex architecture)
```

**If Strong Consistency Required:**
- Would need distributed transactions (2-phase commit)
- Kills scalability (all services coupled)
- Higher latency (coordination overhead)
- More failure scenarios

#### Risk Mitigation

| Risk | Mitigation |
|------|-----------|
| User sees "transfer OK" but balance doesn't update | Outbox pattern guarantees delivery within seconds |
| Accounts MS never processes event | Circuit breaker detects, alerts operator |
| User notification never arrives | Audit log shows "sent", manual notification possible |
| Multiple transfers double-debit same account | Outbox ensures idempotency (same ID = no double-debit) |

---

## 2. Distributed Complexity vs. Monolithic Simplicity

### Decision
**Chosen:** Distributed Microservices (multiple services, network calls, eventual consistency)

### Trade-Off Analysis

#### What We Sacrificed: Monolithic Simplicity
- Single deployable unit → 3+ services (Phase 2+)
- In-process calls → network calls (50-200ms latency)
- ACID transactions → Saga pattern (manual compensation)
- Single DB → Database per service (schema management)

#### What We Gained: Independent Scaling & Isolation
✅ **Scaling:**
- Accounts MS: handle 10x traffic (24/7 high volume)
- Transfers MS: handle peak loads independently
- Notifications/Audit: eventual, can lag without impact

✅ **Isolation:**
- Accounts MS failure ≠ transfers stop (stored in Outbox)
- Can deploy Accounts MS without touching Transfers
- Team autonomy (Accounts team owns their service)

✅ **Technology Choice:**
- Could use different database per service (e.g., Accounts: PostgreSQL, future service: MongoDB)
- Could optimize each service's tech stack

#### Why This Trade-Off Is Acceptable

**Monolith was reaching limits:**
- 5 modules sharing single database (schema contention)
- Transfers always waiting on Accounts (blocking calls)
- Scaling: can't scale just Accounts without deploying entire monolith
- Team growth: hard to parallelize development

**Example: Monolith Bottleneck**
```
Scenario: Peak traffic at 5 PM (10x normal)
  - Transfers surge (all waiting on Accounts for balance check)
  - Notifications backlog (blocking transfer flow)
  - Entire monolith gets slow (even Auth, Audit affected)
  - Only solution: scale entire monolith (expensive, slow)

With Microservices:
  - Transfers scales independently (add more instances)
  - Accounts scales independently
  - Notifications stay async (no blocking)
  - Cheaper, faster to respond to demand
```

#### Risks & Mitigation

| Risk | Mitigation |
|---|---|
| Network latency increased (50ms per HTTP call) | Circuit breaker + caching + prediction |
| Debugging becomes complex (traces across services) | OpenTelemetry (ADR-009) shows full call stack |
| Deployment complexity (3+ services to deploy) | Kubernetes/container orchestration (Phase 3+) |
| Data consistency is eventual (not immediate) | Outbox pattern guarantees delivery within seconds |

---

## 3. Sacrificing Availability for Consistency (Or Not)

### Decision
**Chosen:** Sacrifice Consistency Slightly, NOT Availability

### Analysis

#### What Some Architects Would Ask
"Should we prioritize consistency (all data synchronized immediately) over availability (system always responding)?"

**In financial systems:** Common misconception that consistency always wins

#### Our Choice: Availability + Eventual Consistency

**Example Scenario:**
```
Scenario: Accounts MS becomes unavailable

Option A: Strong Consistency (Sacrificing Availability)
  - Transfer request comes in
  - Tries to call Accounts MS (debit/credit)
  - Accounts MS down → call fails → return 503 Service Unavailable
  - User cannot transfer (system unavailable)

Option B: Eventual Consistency (Sacrificing Immediate Consistency)
  - Transfer request comes in
  - Records transfer in Transfers DB (Outbox pattern)
  - Returns "transfer accepted" immediately
  - OutboxWorker keeps retrying Accounts MS in background
  - When Accounts MS comes back: debit/credit processed
  - User could see temp inconsistency (balance lag) but system available
```

#### Why This Trade-Off Matters

**CAP Theorem Reality:**
- In distributed systems: can't have Consistency + Availability + Partition Tolerance (CAP)
- **Our choice:** Availability + Partition Tolerance (Accept eventual consistency)
- **Why not:** Strong Consistency + Availability would require all services always online (impossible at scale)

#### Banking Domain Acceptance

In real banking:
- **Real-time:** SWIFT, ACH transfers can take hours/days
- **Deposits:** ATM deposits don't show immediately (batch processing)
- **Consensus:** Industry accepts eventual consistency at high scale
- **Regulation:** Focuses on accuracy (eventual), not speed

**Regulatory Angle:**
- Audit trail must show what happened (✓ Audit module)
- Accounts must eventually balance (✓ Saga ensures it)
- Reconciliation processes are nightly/daily (✓ Can detect discrepancies)

---

## 4. Decision Reversibility

### Which Decisions Are Reversible? Which Are Permanent?

| Decision | Reversible? | Cost to Reverse | Timeline |
|----------|-----------|-----------------|----------|
| **RabbitMQ vs Kafka** | ❌ Hard | High (event schemas, consumers) | Weeks |
| **PostgreSQL DB** | ❌ Hard | High (data migration, schema) | Days/Weeks |
| **YARP Gateway** | ✅ Easy | Low (swap implementation) | Hours |
| **Hexagonal architecture** | ✅ Easy | Low (refactor layers) | Days |
| **Saga choreography** | ⚠️ Medium | Medium (rewrite to orchestration) | Weeks |
| **OpenTelemetry** | ✅ Easy | Low (swap exporter backend) | Hours |
| **CloudEvents format** | ❌ Hard | High (all events, all consumers) | Weeks |

### How This Affected Decisions

**Low Reversibility → More Conservative:**
- Message broker choice (RabbitMQ): researched thoroughly, industry standard
- Database choice (PostgreSQL): team expertise, proven at scale
- Event format (CloudEvents): standard, locks us in but for good reason

**High Reversibility → More Flexible:**
- Gateway implementation (YARP): easy to swap, implementation detail
- Internal architecture (Hexagonal): easy to refactor into monolith later
- Observability backend (Jaeger): can switch to Zipkin if needed

### Key Insight: Lock-In Points

**Locked-in decisions:**
1. RabbitMQ as broker (switching to Kafka requires redesigning all events)
2. CloudEvents format (switching format requires redeploying all producers/consumers)
3. PostgreSQL (schema deeply tied to entity structures)

**Why we're okay with lock-in:**
- Industry standards (RabbitMQ, CloudEvents, PostgreSQL)
- Long-term stability (these tech won't disappear)
- Cost to switch so high that lock-in is acceptable

---

## 5. Operational Complexity

### Decision
**Chosen:** Accept Additional Operational Burden (observability, distributed systems)

### Trade-Off

#### Added Operational Components
- RabbitMQ broker (self-hosted or managed)
- Jaeger (distributed tracing)
- Prometheus (metrics)
- Grafana (dashboards)
- Loki (log aggregation)
- OTLP Collector (optional, data pipeline)
- 3+ microservices (vs 1 monolith)

#### Benefits Worth the Complexity
✅ **Debuggability:** Can trace single request across 5 services in Jaeger  
✅ **Visibility:** Prometheus shows exactly where bottleneck is (which service)  
✅ **Automation:** Can set alerts (P99 latency > 1s, error rate > 1%)  
✅ **Proactive Response:** See problem before users report it  

#### Costs
- Learning curve (team must understand OpenTelemetry, PromQL, LogQL)
- Infrastructure overhead (more services to monitor, patch, update)
- Debugging difficulty (must switch between dashboards)
- Operator skill requirement (higher level)

### Is It Worth It?

**Phase 1-2 (Small Scale):**
- Monolith might have been simpler (fewer services = fewer problems)

**Phase 3+ (Production Scale):**
- Monolith would be impossible to debug (no visibility)
- Would spend more time firefighting (reactive)
- Observability investment pays off in reduced MTTR (mean time to recovery)

**ROI Example:**
```
Without observability:
  - Performance issue reported by user (already affected business)
  - 2 hours to identify root cause (guessing, logs)
  - Cost: $X in lost transactions

With observability:
  - Alert fires before user impact (P99 latency spiked)
  - 5 minutes to identify root cause (see in Jaeger)
  - Cost: $0, issue prevented

ROI: breaks even after 1-2 incidents
```

---

## 6. Team Skills vs. Technology Choices

### Decision
**Chosen:** Optimize for Team Expertise (.NET ecosystem)

### What This Meant

#### Favored Decisions
- ✅ **YARP Gateway:** Built in C#, team-native
- ✅ **PostgreSQL:** Team experience, Npgsql first-class
- ✅ **RabbitMQ.Client:** Native .NET library
- ✅ **Hexagonal in C#:** Natural DDD pattern in C#

#### Decisions NOT Made
- ❌ **Nginx:** Would require learning ops tool
- ❌ **Kafka:** More operational complexity, learning curve
- ❌ **NoSQL:** Team expertise in relational, not document databases
- ❌ **Istio:** Requires Kubernetes expertise (not available)

### Trade-Off

#### What We Sacrificed
- Potentially "better" tech for the job (Kafka more feature-rich than RabbitMQ)
- Broader multi-language ecosystem exposure

#### What We Gained
- **Velocity:** Team onboards faster (C# is home turf)
- **Quality:** Deeper expertise in chosen stack
- **Support:** Easy to find .NET expertise in job market
- **Integration:** Seamless with existing .NET codebase

### Why This Is The Right Call

In early-stage modernization:
- **Execution speed > Perfect tooling**
- **Team confidence > Best-in-class technology**
- **Time-to-market > Architectural purity**

Example:
```
Timeline A (Polyglot): Kafka (Go) + Nginx (C) + PostgreSQL
  - Learning curve: 4 weeks
  - Debugging: context switching between languages
  - Hiring: need Go + C + PostgreSQL expertise

Timeline B (Homogeneous): RabbitMQ (.NET) + YARP (C#) + PostgreSQL
  - Learning curve: 1 week
  - Debugging: single language, single IDE
  - Hiring: need .NET expertise only

Timeline B wins for Phase 1-2
```

---

## 7. Strangler Fig Approach (Gradual vs. Big Bang)

### Decision
**Chosen:** Gradual Extraction (Strangler Fig pattern)

### Trade-Off

#### What We Sacrificed: Immediate Benefits
- Didn't rewrite entire system in "ideal" architecture
- Technical debt remains in monolith (Auth, Notifications, Audit still monolithic)
- Temporary hybrid state (gateway routing to 2+ services)

#### What We Gained: Managed Risk
✅ **Controllable Rollout:**
- Deploy Accounts MS, test with real traffic
- If issue found: roll back (Outbox contains data)
- No need for "big bang" deployment day

✅ **Incremental ROI:**
- Accounts MS scales independently (week 1+)
- Transfers MS comes online (week 2+)
- Benefits accumulate incrementally

✅ **Team Learning:**
- Pattern established with Accounts (reusable for Transfers)
- Operators learn microservices patterns gradually
- Not all learning pressure at once

#### Risks of Gradual Approach
- Temporary technical debt (hybrid monolith + MS)
- Cognitive load (team must understand both old + new)
- Temporary complexity (gateway, dual-write, etc.)

**Accepted because:** These are temporary pains (6-12 months), strong consistency choice gives us safety net

---

## Summary: Net Positive Trade-Offs

| Trade-Off | What We Sacrificed | What We Gained | Verdict |
|-----------|-------------------|----------------|---------|
| Eventual consistency | Immediate balance updates | Resilience, scalability | ✅ Worth it |
| Distributed complexity | Monolith simplicity | Independent scaling, team autonomy | ✅ Worth it |
| Availability > Consistency | Can't guarantee immediate sync | System always responsive | ✅ Worth it (banking reality) |
| Operational burden | Simpler ops (1 service) | Full visibility, proactive alerts | ✅ Worth it (long-term) |
| .NET lock-in | Polyglot potential | Team velocity, expertise depth | ✅ Worth it (Phase 1-2) |
| Gradual rollout | Delayed full benefits | Managed risk, incremental learning | ✅ Worth it (controllable) |

---

## Decisions We Would Make Differently If...

### If We Had Unlimited Budget
- Kafka (more powerful streaming potential)
- Kubernetes (container orchestration from day 1)
- Dedicated DBA team (more complex database optimization)
- Multi-region deployment (higher availability)

### If We Had More Time
- Event sourcing (complete audit trail)
- CQRS (separate read/write optimization)
- Polyglot persistence (different DB per bounded context)

### If We Had More Team Expertise
- Istio service mesh (advanced traffic management)
- Cassandra (distributed database)
- Go/Rust services (performance-critical paths)

### If Regulatory Requirements Were Different
- Could choose strong consistency over availability
- Could skip audit logging (less overhead)
- Could use NoSQL (less strict ACID requirement)

---

## Conclusion

The chosen architecture prioritizes **practical pragmatism over theoretical purity:**
- ✅ Real teams execute faster with familiar tech
- ✅ Real systems need resilience, not just consistency
- ✅ Real banking accepts eventual consistency
- ✅ Real operations benefit from observability, even at cost of complexity

These trade-offs should be revisited in 12-24 months when:
- Bottlenecks become clear (scale issues)
- Team expertise grows (can handle complexity)
- Requirements change (regulatory, business)

**Review Date:** 2027-06-29 (1 year after implementation)

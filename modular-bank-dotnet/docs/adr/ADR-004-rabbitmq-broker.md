# ADR-004: Message Broker Selection (RabbitMQ)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Phase 2 introduces event-driven architecture with saga choreography pattern. Transfers MS publishes `TransferExecutedEvent` to a message broker; Notifications and Audit services consume and react. The broker must provide:
- Durable message storage (no event loss)
- Replay capability (reprocess events)
- Multiple consumer support (fan-out pattern)
- At-least-once delivery semantics
- Transactional guarantees (Outbox pattern)

### Requirements
- Must support CloudEvents format (JSON payloads)
- Must enable consumer lag monitoring (observability)
- Must support dead-letter queues for failed messages
- Must be horizontally scalable (Phase 4+)
- Should not introduce operational complexity beyond team capability

### Scale Assumptions (Phase 1-2)
- ~1,000 transfers/day initially
- Sustained: ~100 transfers/minute peak
- ~3 consumers (Notifications, Audit, Future)
- No extreme throughput requirements (< 100k msg/sec)

---

## Decision

**Use RabbitMQ as the message broker.**

RabbitMQ is a robust, mature message broker implementing AMQP 0.9.1 protocol with excellent durability guarantees and operational tooling.

### Rationale

| Criterion | RabbitMQ | Kafka | AWS SQS | Azure ServiceBus | Redis |
|-----------|----------|-------|---------|-----------------|-------|
| **Durability** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐ |
| **Replay** | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐ | ⭐⭐⭐ | ❌ |
| **Setup Complexity** | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐ |
| **Consumer Lag Monitoring** | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐ |
| **Multi-Consumer (Fan-out)** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| **Infrastructure Cost** | $0 | $0 | Paid | Paid | $0 |
| **Team Expertise** | ⭐⭐⭐ | ⭐⭐⭐ | Low | Low | ⭐⭐⭐⭐ |
| **Docker Deployment** | Easy | Complex | N/A | N/A | Easy |
| **Operational Tooling** | Excellent | Good | Limited | Good | Limited |

### Specific Advantages

1. **Perfect Fan-Out Pattern**
   - Multiple consumers subscribe to same topic (exchanges in AMQP)
   - Each consumer gets independent copy of message
   - Ideal for Notifications + Audit both reacting to transfer events
   - Kafka's consumer groups not needed for our use case

2. **Exceptional Durability & Reliability**
   - Message persisted to disk before acknowledgment
   - Prevents message loss even if broker crashes
   - Replication supported (RabbitMQ clustering)
   - Dead-letter queue for undeliverable messages
   - Exactly-once semantics with transactional channels

3. **Operational Excellence**
   - Management UI (RabbitMQ Management Console) on port 15672
   - Built-in consumer lag visibility
   - Queue depth monitoring (observable from dashboard)
   - No separate infrastructure tools required (unlike Kafka)

4. **Simplicity vs Kafka**
   - Kafka is optimized for event streaming (replay from beginning)
   - RabbitMQ optimized for message queuing (current + future)
   - Our use case: side-effect handling, not event sourcing
   - Onboarding faster, operations simpler (no ZooKeeper, no broker coordin

ation)
   - Learning curve: days vs weeks

5. **Outbox Pattern Integration**
   - Exactly fits requirement: "publish event when DB transaction commits"
   - Can be implemented with RabbitMQ channels + transactional publishes
   - Guarantees "write to DB + publish to broker" atomicity

6. **Cost-Effective**
   - Open source (Mozilla Public License 2.0)
   - Docker image available (official: `rabbitmq:3.13-management`)
   - No licensing costs
   - Runs on modest hardware (Phase 1: single instance)

---

## Alternative Options Considered

### Option A: Apache Kafka ❌
**Why Not Recommended:**
- Over-engineered for current use case (streaming platform, not messaging queue)
- Requires ZooKeeper for coordination (operational overhead)
- Consumer group model creates complexity for fan-out (need topics per consumer)
- Performance optimization: log-structured storage (overkill for side effects)
- Typical use case: analytics pipelines, high-volume event streaming

**When to use instead:**
- Extreme throughput requirements (1M+ msg/sec)
- Event sourcing architecture (need replay from beginning)
- Need for event stream analytics (Kafka Streams)
- Team expertise in streaming architectures

### Option B: AWS SQS ❌
**Why Not Recommended:**
- Vendor lock-in (AWS-only, not portable)
- Costs accumulate per message (not negligible at scale)
- Limited replay capability (messages deleted after retention period)
- Cannot fan-out to multiple consumers easily (need SNS for that)
- Difficult to run locally for development (AWS Local Stack complexity)

**When to use instead:**
- Pure AWS infrastructure (no other cloud option)
- Cost not primary concern (enterprise budget)
- Willing to accept AWS service constraints

### Option C: Redis Pub/Sub ❌
**Why Not Recommended:**
- **Critical flaw:** No message persistence (Redis is in-memory)
- If broker crashes mid-message, messages lost permanently
- No durable queue (only active subscribers receive)
- No replay capability (can't reprocess after consumer failure)
- Not acceptable for financial transactions (regulatory requirement)

**When to use instead:**
- Non-critical messaging (caching invalidation, real-time notifications)
- Acceptable message loss (user-facing notifications only)
- Team wants simplicity over durability

### Option D: Azure Service Bus ❌
**Why Not Recommended:**
- Vendor lock-in (Azure-only)
- Requires cloud account + networking setup
- Cannot run locally in docker-compose easily
- Costs accumulate per operation
- Team has no existing Azure expertise

**When to use instead:**
- Enterprise Azure ecosystem
- Need for advanced messaging patterns (dead-lettering, deferred messages)

---

## Consequences

### Positive

✅ **High Reliability**
- Disk-persisted messages: no data loss on broker restart
- Replication support for HA (Phase 3+)
- Dead-letter queue for poison messages

✅ **Perfect for Microservice Patterns**
- Fan-out pattern: multiple consumers get same message
- Ideal for side-effect handling (Notifications, Audit)
- Poison message handling (DLQ, alerting)

✅ **Operational Simplicity**
- Management UI built-in (no separate monitoring tool)
- Easy consumer lag observation
- Straightforward Docker deployment
- Single broker instance sufficient for Phase 1-2

✅ **Team Productivity**
- Faster onboarding (simpler than Kafka)
- Excellent .NET client library (RabbitMQ.Client)
- Well-documented patterns
- Stack Overflow community support

✅ **Cost-Effective**
- Open source
- No licensing fees
- Modest resource requirements

### Negative

❌ **Not Optimized for High Volume**
- Throughput ceiling ~100k msg/sec (vs Kafka's 1M+)
- Acceptable for banking application (peak <<100k req/sec)
- Potential bottleneck only at massive scale (Phase 4+)

❌ **Limited Stream Replay**
- Cannot efficiently replay entire message history (not designed for it)
- Kafka better if future need for event sourcing emerges
- Mitigation: implement manual event log if needed

❌ **Single-Broker Not Highly Available**
- Phase 1 deployment: single RabbitMQ instance (SPOF)
- Clustering/mirroring complexity for HA (deferred to Phase 3+)
- Acceptable risk in early phases

❌ **Kafka Ecosystem Advantage**
- Kafka has mature stream processing libraries (Kafka Streams, Flink)
- If future need for stream analytics: migration required
- Current roadmap doesn't include real-time analytics

---

## Implementation Details

### RabbitMQ Configuration

**Exchange:** `banking.events` (topic exchange)
- **Type:** Topic (allows routing key patterns)
- **Durable:** Yes (survives broker restart)

**Queues:**
- `notifications.transfer-executed` (durable) → Notifications consumer
- `audit.transfer-executed` (durable) → Audit consumer

**Bindings:**
- Exchange → Queue with routing key: `transfer.executed`

### Delivery Guarantees

**At-Least-Once Semantics:**
- Transfers MS publishes with confirmation (waits for broker acknowledgment)
- Consumer processes, then sends acknowledgment to broker
- If consumer crashes before ack: broker redelivers to next consumer

**Exactly-Once with Outbox:**
- Transfers MS: writes Transfer + OutboxEntry to same DB transaction
- OutboxWorker: reads unconfirmed entries, publishes, marks confirmed
- Even if OutboxWorker crashes mid-publish: entry stays unconfirmed, replayed

---

## Monitoring & Alerting

**Consumer Lag Monitoring:**
```bash
# Via RabbitMQ Management API
GET http://localhost:15672/api/consumers
```

**Metrics to Track:**
- Queue depth (unprocessed messages)
- Consumer ack rate (messages processed/sec)
- Dead-letter queue size (failed messages)
- Broker connection count

---

## Migration Path

**Phase 1:** No messaging (Accounts MS extraction complete)

**Phase 2:** Introduce RabbitMQ
- Transfers MS publishes events
- Monolith listens and processes

**Phase 3+:** Optional cluster/replication if HA needed

---

## Related Decisions

**Depends On:**
- ADR-002 (Transfers extraction as primary event source)
- ADR-007 (Saga choreography pattern)

**Enables:**
- ADR-010 (CloudEvents contract for events)
- ADR-009 (Observability: monitor consumer lag)

---

## References
- **Project:** RabbitMQ — https://www.rabbitmq.com/
- **Protocol:** AMQP 0.9.1
- **.NET Client:** https://www.rabbitmq.com/dotnet.html
- **Docker Image:** `rabbitmq:3.13-management`
- **Pattern:** Event-Driven Architecture (Sam Newman, Building Microservices)

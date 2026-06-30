# ADR-010: Event Contract Format and Versioning (CloudEvents + JSON Schema)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Services communicate via RabbitMQ events. The event format must:
- Be standardized (both producers and consumers understand structure)
- Support versioning (evolve without breaking consumers)
- Include necessary metadata (timestamp, source, correlation)
- Be extensible (custom properties)

### Current State
- No event contract defined
- Risk: producer and consumer disagree on schema → silent failures

---

## Decision

**Use CloudEvents 1.0 (CNCF standard) for all events, with JSON Schema for validation.**

### Example: TransferExecutedEvent

```json
{
  "specversion": "1.0",
  "type": "com.finbank.transfers.executed.v1",
  "source": "/services/transfers-service",
  "id": "transfer-123456",
  "time": "2026-06-29T10:30:00Z",
  "datacontenttype": "application/json",
  "dataschema": "urn:com.finbank:transfers:executed:v1",
  "subject": "transfer/123456",
  "correlationid": "user-456",
  "data": {
    "transferId": "123456",
    "sourceAccountId": "acc-001",
    "targetAccountId": "acc-002",
    "userId": "user-456",
    "amount": "1000.50",
    "reference": "Payment for invoice #INV-2026-001",
    "occurredAt": "2026-06-29T10:30:00Z"
  }
}
```

### CloudEvents Standard Fields

| Field | Purpose | Example |
|-------|---------|---------|
| `specversion` | CloudEvents spec version | "1.0" |
| `type` | Event type (include version) | "com.finbank.transfers.executed.v1" |
| `source` | Event origin | "/services/transfers-service" |
| `id` | Unique event ID (for deduplication) | UUID |
| `time` | Timestamp (RFC3339) | "2026-06-29T10:30:00Z" |
| `datacontenttype` | Payload format | "application/json" |
| `dataschema` | Schema location | "urn:com.finbank:transfers:executed:v1" |
| `subject` | Related resource | "transfer/123456" |
| `correlationid` | Request correlation ID | UUID |
| `data` | Event payload | Custom JSON object |

### Rationale

| Criterion | CloudEvents | Custom JSON | Avro | Protobuf |
|-----------|-----------|-----------|------|----------|
| **Standardization** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Tooling** | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ |
| **Human Readable** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐ |
| **Versioning** | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Governance** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| **Integration** | ⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |

**Chosen: CloudEvents** because:
1. Industry standard (CNCF, Linux Foundation)
2. Human-readable (JSON, debuggable)
3. Version in `type` field (clear versioning strategy)
4. Includes correlation/tracing fields (aligns with ADR-009)
5. Portable across different brokers (not tied to RabbitMQ)

---

## Alternative A: Custom JSON ❌

```json
{
  "transferId": "...",
  "sourceAccountId": "...",
  "amount": "...",
  "timestamp": "..."
}
```

**Why not:**
- No standard metadata (tracing, correlation)
- No clear versioning mechanism
- Every team invents different structure
- Difficult to build cross-org tooling

---

## Alternative B: Avro ❌

Binary serialization with schema registry

**Why not:**
- Binary format: not human-readable (hard to debug)
- Requires schema registry (additional operational component)
- .NET support okay but not first-class
- Overkill for JSON-friendly RabbitMQ

---

## Versioning Strategy

### Version in Type Field

**V1 of TransferExecuted:**
```
type: "com.finbank.transfers.executed.v1"
```

**V2 (future, with additional fields):**
```
type: "com.finbank.transfers.executed.v2"
```

### Backward Compatibility Rules

**Rules:**
1. Old versions: consumers MUST handle multiple versions (subscribe to `transfer.executed.*`)
2. New fields: added as optional (old consumer skips unknown fields)
3. Changed semantics: increment version (v1 → v2)
4. Removed fields: increment version (breaking change)

### RabbitMQ Routing

```
Exchange: banking.events (topic)
Routing key pattern: transfer.executed.v*

Binding:
  - Notifications: transfer.executed.v* (consumes v1 and v2)
  - Audit: transfer.executed.v* (consumes v1 and v2)
```

---

## JSON Schema Definition

**File:** `docs/events/transfer-executed-v1.schema.json`

```json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "urn:com.finbank:transfers:executed:v1",
  "title": "Transfer Executed Event (V1)",
  "type": "object",
  "properties": {
    "specversion": { "const": "1.0" },
    "type": { "const": "com.finbank.transfers.executed.v1" },
    "source": { "type": "string", "example": "/services/transfers-service" },
    "id": { "type": "string", "format": "uuid" },
    "time": { "type": "string", "format": "date-time" },
    "datacontenttype": { "const": "application/json" },
    "dataschema": { "const": "urn:com.finbank:transfers:executed:v1" },
    "subject": { "type": "string", "pattern": "^transfer/.*" },
    "correlationid": { "type": "string", "format": "uuid" },
    "data": {
      "type": "object",
      "properties": {
        "transferId": { "type": "string", "format": "uuid" },
        "sourceAccountId": { "type": "string", "format": "uuid" },
        "targetAccountId": { "type": "string", "format": "uuid" },
        "userId": { "type": "string", "format": "uuid" },
        "amount": { "type": "string", "pattern": "^[0-9]+\\.[0-9]{2}$" },
        "reference": { "type": ["string", "null"], "maxLength": 255 },
        "occurredAt": { "type": "string", "format": "date-time" }
      },
      "required": ["transferId", "sourceAccountId", "targetAccountId", "userId", "amount", "occurredAt"],
      "additionalProperties": false
    }
  },
  "required": ["specversion", "type", "source", "id", "time", "datacontenttype", "data"],
  "additionalProperties": true
}
```

---

## Implementation in C#

### Event Class

```csharp
public record TransferExecutedEvent(
    string SpecVersion = "1.0",
    string Type = "com.finbank.transfers.executed.v1",
    string Source = "/services/transfers-service",
    string Id = null!,
    DateTime Time = default,
    string DataContentType = "application/json",
    string DataSchema = "urn:com.finbank:transfers:executed:v1",
    string Subject = null!,
    string CorrelationId = null!,
    TransferExecutedData Data = null!
);

public record TransferExecutedData(
    Guid TransferId,
    Guid SourceAccountId,
    Guid TargetAccountId,
    Guid UserId,
    string Amount,  // "1000.50"
    string? Reference,
    DateTime OccurredAt
);
```

### Publishing

```csharp
var @event = new TransferExecutedEvent(
    Id: Guid.NewGuid().ToString(),
    Time: DateTime.UtcNow,
    Subject: $"transfer/{transfer.Id}",
    CorrelationId: userId.ToString(),
    Data: new(
        TransferId: transfer.Id,
        SourceAccountId: transfer.SourceAccountId,
        TargetAccountId: transfer.TargetAccountId,
        UserId: userId,
        Amount: transfer.Amount.ToString(),
        Reference: transfer.Reference,
        OccurredAt: transfer.CreatedAt
    )
);

var json = JsonSerializer.Serialize(@event);
await eventPublisher.PublishAsync("transfer.executed.v1", json);
```

### Consuming with Version Handling

```csharp
await consumer.Subscribe("transfer.executed.*", async (message) =>
{
    var @event = JsonSerializer.Deserialize<TransferExecutedEvent>(message.Body);
    
    if (@event.Type == "com.finbank.transfers.executed.v1")
    {
        // Handle v1
        await notifications.SendAsync(
            @event.Data.UserId,
            NotificationType.TransferSent,
            new() { 
                { "amount", @event.Data.Amount },
                { "targetAccountId", @event.Data.TargetAccountId.ToString() }
            });
    }
    else if (@event.Type == "com.finbank.transfers.executed.v2")
    {
        // Handle v2 (hypothetical future version)
        // ...
    }
    else
    {
        // Unknown version: log and skip
        logger.LogWarning("Unknown event type: {Type}", @event.Type);
    }
});
```

---

## Consequences

### Positive

✅ **Standardized Contract**
- All events follow same structure
- Teams understand what to expect
- Tooling can validate against schema

✅ **Versioning Clarity**
- Version in type field: no ambiguity
- Backward/forward compatibility explicit
- Evolution path clear

✅ **Traceability**
- `correlationid`: links event to original request
- `id`: deduplicates replayed events
- Aligns with distributed tracing (ADR-009)

✅ **Portability**
- CloudEvents spec works with any broker (RabbitMQ, Kafka, HTTP webhooks, etc.)
- If switching brokers in future: events still valid

### Negative

❌ **Verbosity**
- Envelope metadata makes events larger (CloudEvents standard fields)
- Acceptable overhead (metadata ~300 bytes, payload ~500 bytes)

❌ **Learning Curve**
- Team must understand CloudEvents spec
- Schema validation adds complexity
- New concept for some developers

---

## Implementation Checklist

- [ ] Create `docs/events/` directory
- [ ] Define transfer-executed-v1.schema.json (JSON Schema)
- [ ] Create C# event classes (TransferExecutedEvent, Data)
- [ ] Implement serialization/deserialization
- [ ] Add schema validation in consumers (optional, for robustness)
- [ ] Document versioning strategy in README
- [ ] Create examples in `docs/events/examples/`

---

## Evolution Example: V2 Support (Future)

If future requirement: "Add fee tracking to transfers"

**V2 Schema Addition:**
```json
{
  "type": "com.finbank.transfers.executed.v2",
  "data": {
    // ... existing fields ...
    "fee": "10.00",  // New in v2
    "feeType": "TRANSFER_FEE"  // New in v2
  }
}
```

**Backward Compat:**
- V1 subscribers: field marked optional, skip it
- V2 subscribers: new logic for fee tracking
- No downtime, no code changes to v1 consumers

---

## Related Decisions

**Depends On:**
- ADR-004 (RabbitMQ as broker)
- ADR-007 (Saga choreography)

**Enables:**
- Contract-driven consumer development
- Cross-team event agreements

---

## References
- **CloudEvents:** https://cloudevents.io/
- **Spec:** https://github.com/cloudevents/spec/blob/v1.0/cloudevents/spec.md
- **JSON Schema:** https://json-schema.org/
- **RabbitMQ Topic Exchange:** https://www.rabbitmq.com/tutorials/amqp-concepts.html#exchange-topic

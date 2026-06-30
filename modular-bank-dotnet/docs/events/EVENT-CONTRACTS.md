# Event Contracts & Versioning

## Overview

FinBank uses **CloudEvents 1.0** (CNCF standard) for event-driven communication between microservices.

- **Schema Format:** JSON Schema (draft-07)
- **Event Format:** CloudEvents 1.0 compliant JSON
- **Transport:** RabbitMQ (topic exchange with routing keys)
- **Versioning:** Type field includes version (v1, v2, etc.)

---

## CloudEvents 1.0 Specification

CloudEvents provides a standard envelope for describing events with consistent metadata.

### Standard Attributes (Required)

| Attribute | Type | Purpose | Example |
|---|---|---|---|
| `specversion` | String | CloudEvents spec version | `"1.0"` |
| `type` | String | Event type (namespaced + versioned) | `"com.finbank.transfers.executed.v1"` |
| `source` | String | Event origin/context | `"/services/transfers-service"` |
| `id` | String (UUID) | Unique event ID | `"550e8400-e29b-41d4-a716-446655440000"` |
| `time` | String (RFC3339) | Event timestamp (UTC) | `"2026-06-29T10:30:00Z"` |
| `datacontenttype` | String | Payload format | `"application/json"` |
| `data` | Object | Event payload | `{...}` |

### Optional Attributes

| Attribute | Purpose | Example |
|---|---|---|
| `dataschema` | Schema ID for validation | `"urn:com.finbank:transfers:executed:v1"` |
| `subject` | Related resource | `"transfer/550e8400-e29b-41d4-a716-446655440000"` |
| `correlationid` | Request tracing ID | `"user-123"` |
| `traceparent` | W3C Trace Context | `"00-4bf92f3577b34da6a3ce929d0e0e4736-..."` |

---

## Event Catalog

### transfer.executed (v1)

**File:** `transfer-executed-v1.schema.json`

**When Published:** Transfer successfully recorded in Transfers MS

**Consumers:**
- Accounts MS: Debit/credit accounts
- Notifications MS: Send user notification
- Audit MS: Record audit entry

**Payload Schema:**
```json
{
  "transferId": "uuid",
  "sourceAccountId": "uuid",
  "targetAccountId": "uuid",
  "userId": "uuid",
  "amount": 1000.50,
  "reference": "optional string",
  "occurredAt": "2026-06-29T10:30:00Z"
}
```

**Routing Key:** `transfer.executed.v*`

**Binding:** 
- Exchange: `banking.events` (topic)
- Queue: `notifications.transfer-executed` (Notifications)
- Queue: `audit.transfer-executed` (Audit)
- Queue: `accounts.transfer-executed` (Accounts - future)

---

## Versioning Strategy

### Version in Type Field

CloudEvents type field includes version to enable **graceful versioning**.

**Format:** `com.domain.entity.action.v<N>`

**Examples:**
- V1: `com.finbank.transfers.executed.v1`
- V2: `com.finbank.transfers.executed.v2` (breaking changes)
- V3: `com.finbank.transfers.executed.v3` (new fields)

### Routing Key Pattern

Consumers use wildcard routing to support multiple versions:

```
transfer.executed.v*    # Matches v1, v2, v3, etc.
```

### Backward Compatibility

**V1 → V2 (Compatible):**
- Add optional fields (consumers skip unknown fields)
- Rename old field → add new field (both present)
- Change field type if backward-compatible

**V1 → V2 (Incompatible):**
- Remove required field
- Change type (e.g., string → number)
- Change meaning of field

For incompatible changes:
1. Increment version in type
2. Consumers subscribe to new routing key
3. Keep V1 consumers running during transition
4. Migrate consumers gradually

### Example: Adding Fee Field

**V1 (current):**
```json
{
  "transferId": "...",
  "amount": 1000.50,
  "reference": "..."
}
```

**V2 (future - compatible):**
```json
{
  "transferId": "...",
  "amount": 1000.50,
  "reference": "...",
  "fee": 5.00,           // NEW: optional
  "feeType": "TRANSFER"  // NEW: optional
}
```

**Migration Path:**
1. Producer: Publish both V1 and V2 events for a period
2. Consumers: Subscribe to `transfer.executed.v*` (matches both)
3. Consumers: Handle V2 fee fields if present
4. After all consumers updated: Stop publishing V1

---

## Event Publishing

### In Transfers MS

```csharp
var @event = new TransferExecutedEvent(
    Id: transfer.Id.ToString(),
    Time: transfer.CreatedAt,
    Subject: $"transfer/{transfer.Id}",
    CorrelationId: userId.ToString(),
    Data: new(
        TransferId: transfer.Id,
        SourceAccountId: transfer.SourceAccountId,
        TargetAccountId: transfer.TargetAccountId,
        UserId: userId,
        Amount: transfer.Amount,
        Reference: transfer.Reference,
        OccurredAt: transfer.CreatedAt
    )
);

var eventJson = JsonSerializer.Serialize(@event);

// Saved to outbox (guaranteed delivery)
await _eventPublisher.PublishAsync("transfer.executed.v1", eventJson);
```

### In RabbitMQ

**Exchange:** `banking.events` (topic)

**Routing Key:** `transfer.executed.v1`

**Message Properties:**
- `Persistent: true` (survives broker restart)
- `DeliveryMode: 2` (persistent)
- `ContentType: application/json`

**Payload:** CloudEvents JSON (compressed optional)

---

## Event Consuming

### In Monolith Consumers

```csharp
// NotificationsConsumer.cs
consumer.Received += async (model, ea) =>
{
    var message = Encoding.UTF8.GetString(ea.Body.ToArray());
    
    // Parse CloudEvent
    var @event = JsonSerializer.Deserialize<TransferExecutedEvent>(message);
    
    // Check version
    if (@event.Type == "com.finbank.transfers.executed.v1")
    {
        // Handle V1
        await notificationsService.SendAsync(
            @event.Data.UserId,
            NotificationType.TransferSent,
            payload);
    }
    else if (@event.Type == "com.finbank.transfers.executed.v2")
    {
        // Handle V2 (if exists in future)
        // Handle fee field if present
    }
    
    // Acknowledge after success
    channel.BasicAck(ea.DeliveryTag, false);
};
```

---

## Validation

### JSON Schema Validation

For production, validate incoming events against schema:

```csharp
// Pseudo-code
var schema = JSchema.Parse(schemaJson);
var token = JToken.Parse(eventJson);
bool isValid = token.IsValid(schema);

if (!isValid)
{
    _logger.LogError("Invalid event: {Errors}", token.ValidationErrors);
    // Nack and move to DLQ
}
```

### Schema Registry (Optional)

In larger systems, centralize schema management:

- **Confluent Schema Registry** (if using Kafka)
- **Azure Schema Registry** (if using Azure)
- **Custom JSON endpoint** (DIY approach)

For this project: schemas in `docs/events/`

---

## Monitoring

### Event Flow Verification

**RabbitMQ Management UI:**
```
http://localhost:15672
  → Queues
    → notifications.transfer-executed
      → Should have "Ready" messages after transfer
    → audit.transfer-executed
      → Should have "Ready" messages after transfer
```

### Consumer Lag Monitoring

Track how behind consumers are:

```bash
# In consumer logs, watch for processing timestamps
docker-compose logs monolith | grep -i "processed.*transfer"

# Calculate lag = (published time - processed time)
```

### Schema Validation Monitoring

Track validation failures:

```bash
# Check logs for "Invalid event" errors
docker-compose logs monolith | grep -i "invalid event"
```

---

## Checklist: Adding New Event Type

To add a new event (e.g., `account.created`):

- [ ] Create schema file: `docs/events/account-created-v1.schema.json`
- [ ] Add to EVENT-CONTRACTS.md (this file)
- [ ] Create C# record: `AccountCreatedEvent.cs`
- [ ] Add publisher in relevant MS
- [ ] Add consumer in monolith (or future MS)
- [ ] Add routing key binding in RabbitMQ setup
- [ ] Document in architecture diagrams
- [ ] Add test scenarios

---

## Related Documentation

- **Transfers Testing:** `docs/PHASE-2-TESTING.md` (event verification)
- **RabbitMQ Setup:** `docker-compose.yml` (exchanges, queues, bindings)
- **Consumers:** `src/ModularBank/Modules/{Notifications,Audit}/Infrastructure/*Consumer.cs`
- **Publisher:** `services/transfers-service/Infrastructure/Messaging/RabbitMqPublisher.cs`

---

## References

- [CloudEvents 1.0 Spec](https://cloudevents.io/v1.0/)
- [JSON Schema Draft-07](https://json-schema.org/draft-07)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)
- [RabbitMQ Topic Exchange](https://www.rabbitmq.com/tutorials/amqp-concepts.html#exchange-topic)

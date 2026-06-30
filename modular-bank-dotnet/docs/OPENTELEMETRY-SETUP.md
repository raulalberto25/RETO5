# OpenTelemetry Setup Guide

## Overview

OpenTelemetry provides **vendor-neutral instrumentation** for:
- **Traces** → Jaeger (distributed tracing)
- **Metrics** → Prometheus (time-series metrics)
- **Logs** → Loki (log aggregation)

**Deployment:** All 4 services already have OpenTelemetry configured in `Program.cs`

---

## Architecture

```
Services (Gateway, Accounts, Transfers, Monolith)
    ↓ (OpenTelemetry SDK)
    ├─ Traces (W3C TraceContext)
    ├─ Metrics (Prometheus format)
    └─ Logs (JSON structured)
    ↓ (OTLP Exporter)
OpenTelemetry Collector (optional aggregation)
    ↓
    ├─ Jaeger (traces)        → http://localhost:16686
    ├─ Prometheus (metrics)   → http://localhost:9090
    └─ Loki (logs)            → Grafana @ http://localhost:3000
```

---

## Instrumentation Status

### Gateway
**Location:** `gateway/Program.cs`

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("api-gateway"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = 
            new Uri("http://localhost:4317")))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(...));
```

✅ **Configured:** Traces for HTTP, Metrics for ASP.NET Core + HTTP

---

### Accounts MS
**Location:** `services/accounts-service/Program.cs`

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("accounts-service"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter(...))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(...));
```

✅ **Configured:** Traces for HTTP + SQL, Metrics for ASP.NET Core + HTTP + Runtime

---

### Transfers MS
**Location:** `services/transfers-service/Program.cs`

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("transfers-service"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter(...))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(...));
```

✅ **Configured:** Traces for HTTP + SQL, Metrics for ASP.NET Core + HTTP + Runtime

---

### Monolith
**Location:** `src/ModularBank/Program.cs`

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("modular-bank-monolith"))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter(...))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(...));
```

✅ **Configured:** Traces for HTTP + SQL, Metrics for ASP.NET Core + HTTP + Runtime

---

## Trace Flow

### Single Request Example: POST /transfers

```
Client Request (http://localhost:5000/transfers)
    ↓
Gateway (root span: "POST /transfers")
    ├─ HTTP span: "HTTP GET /accounts/{id}"
    │   ↓ (Accounts MS)
    │   ├─ SQL span: "SELECT * FROM accounts WHERE id = @id"
    │   └─ Return
    │
    └─ HTTP span: "HTTP POST /accounts/{id}/debit"
        ↓ (Accounts MS)
        └─ SQL span: "UPDATE accounts SET balance = ..."

TraceId: 4bf92f3577b34da6a3ce929d0e0e4736 (propagated through all spans)
    ├─ Gateway span: a3ce929d0e0e4736
    ├─ Accounts MS span: 929d0e0e4736a3ce
    └─ SQL span: 0e0e4736a3ce929d
```

All spans linked via **W3C TraceContext** headers:
```
traceparent: 00-4bf92f3577b34da6a3ce929d0e0e4736-a3ce929d0e0e4736-01
tracestate: (vendor-specific, e.g., Jaeger baggage)
```

---

## Metrics

### Automatically Collected

| Metric | Source | Use Case |
|---|---|---|
| `http.server.request.duration` | ASP.NET Core | Endpoint latency (P50, P99) |
| `http.client.request.duration` | HTTP Client | Dependency latency |
| `process.runtime.dotnet.gc.collections.count` | Runtime | GC pressure |
| `process.runtime.dotnet.memory.heap.size` | Runtime | Memory usage |
| `process.cpu.usage` | Runtime | CPU usage |
| `process.working_set` | Runtime | Process memory (RSS) |

### Custom Metrics (Optional)

Can be added to track business metrics:

```csharp
var meter = new Meter("finbank.transfers");
var transferCounter = meter.CreateCounter<int>("transfers.executed");

// In TransferUseCase
transferCounter.Add(1, new KeyValuePair<string, object>("status", "success"));
```

---

## Logs

### Structured JSON Logging

Already configured in `Program.cs`:

```csharp
builder.Logging.AddConsole();
// Logs output as JSON with structured fields
```

**Output Format:**
```json
{
  "timestamp": "2026-06-29T10:30:00Z",
  "level": "Information",
  "message": "Transfer executed",
  "service": "transfers-service",
  "traceId": "4bf92f3577b34da6a3ce929d0e0e4736",
  "spanId": "a3ce929d0e0e4736",
  "userId": "user-123",
  "transferId": "transfer-456",
  "amount": 1000.50
}
```

### Log Context Propagation

Logs automatically include:
- `traceId`: Unique request ID
- `spanId`: Specific operation within trace
- `service`: Which microservice generated log
- Custom fields: via logging scope

```csharp
using (logger.BeginScope(new Dictionary<string, object>
{
    { "transferId", transfer.Id },
    { "userId", userId },
    { "amount", transfer.Amount }
}))
{
    logger.LogInformation("Transfer executed successfully");
}
// Output includes transferId, userId, amount fields
```

---

## OTLP Exporter Configuration

### Endpoint

Default: `http://localhost:4317` (OTLP/gRPC)

**Override via environment variable:**
```bash
OpenTelemetry__ExporterEndpoint=http://otel-collector:4317
```

**In docker-compose.yml:**
```yaml
environment:
  OpenTelemetry__ExporterEndpoint: "http://otel-collector:4317"
```

### Protocol

- **OTLP/gRPC** (default, efficient): port 4317
- **OTLP/HTTP** (fallback): port 4318

---

## Sampling

### Default: Sampling All Traces

Currently configured to **sample all traces** (AlwaysOnSampler):

```csharp
.AddOtlpExporter() // Default: sample 100%
```

### Production: Sampling Strategy

For high-traffic production, use probabilistic sampling:

```csharp
.SetSampler(new ProbabilisticSampler(0.1)) // 10% sample rate
```

### Head-Based vs Tail-Based

- **Head-based** (current): Sampler decides at span creation
- **Tail-based** (future): Collector samples based on attributes

---

## Jaeger Integration

### What Jaeger Shows

1. **Traces**: Full request flow across services
2. **Latency breakdown**: Time spent in each service/span
3. **Errors**: Failed operations with stack traces
4. **Dependency graph**: Services calling other services
5. **Service metrics**: Request/error rates per service

### Accessing Jaeger UI

```
http://localhost:16686

Search:
- By Service: "transfers-service"
- By Operation: "POST /transfers"
- By Tag: "http.status_code=201"
- By Trace ID: (from logs)
```

---

## Prometheus Integration

### What Prometheus Shows

1. **Request latency**: P50, P95, P99 percentiles
2. **Error rates**: HTTP 4xx/5xx per endpoint
3. **Throughput**: Requests per second
4. **Resource usage**: CPU, memory, GC
5. **Custom metrics**: Business KPIs

### Accessing Prometheus UI

```
http://localhost:9090

Queries:
- rate(http.server.request.duration_seconds_sum[5m])  # Throughput
- histogram_quantile(0.99, rate(http_request_duration_seconds_bucket[5m]))  # P99
- increase(transfers_executed_total[1h])  # Transfers/hour
```

---

## Loki Integration

### What Loki Shows

1. **Structured logs**: Filter by service, level, trace ID
2. **Log patterns**: Find similar errors across services
3. **Log volume**: Spike detection
4. **Correlation**: Logs for same trace ID

### Accessing Loki (via Grafana)

```
Grafana → Explore → Loki

Query:
{service="transfers-service"} | json
| level="error"
| traceid="4bf92f..."
```

---

## Verification Checklist

### 1. Services Starting with OpenTelemetry

```bash
docker-compose logs | grep -i "OpenTelemetry\|Adding.*instrumentation"
# Should see trace/metric initialization for each service
```

### 2. Traces Flowing

```bash
# Make a transfer request
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"sourceAccountId": "...", "targetAccountId": "...", "amount": 100}'

# Check Jaeger
open http://localhost:16686
# Should see trace for the transfer request
```

### 3. Metrics Being Collected

```bash
# Check Prometheus
open http://localhost:9090
# Query: up{job="transfers-service"}
# Should return 1 (service is up)
```

### 4. Logs with TraceId

```bash
# Check service logs
docker-compose logs transfers-service | head -20
# Each log should have "traceId" field
```

---

## Common Issues & Troubleshooting

### Issue: No traces in Jaeger

**Check:**
1. Jaeger collector running: `docker-compose ps jaeger`
2. OTLP endpoint reachable: curl http://localhost:4317
3. Service logs for exporter errors: grep -i "otlp\|exporter"

**Fix:**
```bash
# Restart services
docker-compose restart transfers-service accounts-service

# Check logs
docker-compose logs jaeger | grep -i "error"
```

### Issue: Metrics not showing in Prometheus

**Check:**
1. Prometheus scrape config (in Phase 4 task 12)
2. Service metrics endpoint: curl http://localhost:5002/metrics (if exposed)

**Note:** Current setup exports via OTLP, not Prometheus scrape endpoint.

### Issue: Logs not appearing in Loki

**Check:**
1. Loki running: `docker-compose ps loki`
2. Log format (should be JSON): `docker-compose logs transfers-service | head -1`
3. Loki datasource in Grafana

---

## Performance Impact

### Overhead

- **Traces**: ~1-2% latency overhead (sampling reduces this)
- **Metrics**: ~1% CPU overhead
- **Logs**: Already done (structured JSON)

### Optimization

For production:
1. Use sampling (10% default is reasonable)
2. Use OTLP compression (gRPC native)
3. Batch exporting (default 512 spans/batch)
4. Disable unneeded instrumentations

---

## Related Documentation

- **Phase 4 Task 12:** `docs/PHASE-4-OBSERVABILITY-SETUP.md` (Jaeger, Prometheus, Grafana, Loki)
- **Troubleshooting:** Check service logs for "error", "exporter", "telemetry"

---

## References

- [OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet)
- [OTLP Spec](https://opentelemetry.io/docs/specs/otel/protocol/)
- [W3C Trace Context](https://www.w3.org/TR/trace-context/)

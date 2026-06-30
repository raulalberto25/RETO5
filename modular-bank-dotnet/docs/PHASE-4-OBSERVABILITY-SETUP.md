# Phase 4 Observability Stack Setup & Testing

## Overview

Observability stack for distributed tracing, metrics collection, and log aggregation:

- **Jaeger** (traces) → visualizes request flow across services
- **Prometheus** (metrics) → collects performance and health metrics
- **Loki** (logs) → aggregates structured logs
- **Grafana** (dashboards) → unified visualization UI

**Deployment:** All services configured in `docker-compose.yml` with OpenTelemetry instrumentation ready.

---

## Quick Start

### 1. Start Full Stack with Observability

```bash
docker-compose down -v  # Clean slate (optional)
docker-compose up -d

# Wait for all services to be healthy
docker-compose ps
# All should show "healthy" or "up"
```

### 2. Verify Services Are Running

```bash
# Check services
docker-compose logs jaeger | grep -i "listening\|ready"
docker-compose logs prometheus | grep -i "listening"
docker-compose logs loki | grep -i "listening"
docker-compose logs grafana | grep -i "started"
```

### 3. Access UIs

```
🔷 Jaeger (Traces):     http://localhost:16686
📊 Prometheus (Metrics): http://localhost:9090
📈 Grafana (Dashboard):  http://localhost:3000 (admin/admin)
```

---

## Service Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Applications                          │
│  Gateway | Accounts MS | Transfers MS | Monolith        │
│        (OpenTelemetry SDK)                              │
└────────────┬──────────────────────────────────┬─────────┘
             │                                  │
             ▼                                  ▼
    ┌────────────────┐              ┌────────────────┐
    │  OTLP Exporter │              │   Log Output   │
    │  (gRPC 4317)   │              │   (JSON Stdout)│
    └────────┬───────┘              └────────┬───────┘
             │                               │
             ▼                               ▼
    ┌────────────────────────────────────────────────┐
    │           Jaeger (All-in-One)                  │
    │  - OTLP Receiver (4317)                        │
    │  - UI (16686) - view traces                    │
    └────────────────────────────────────────────────┘
             │
             └──→ Jaeger UI
                  ├─ Traces by service
                  ├─ Latency analysis
                  ├─ Error visualization
                  └─ Service dependency graph


    ┌────────────────────────────────────────────────┐
    │        Prometheus                              │
    │  - Scrapes metrics from services (:8080/metrics)
    │  - Time-series database                        │
    └────────────────────────────────────────────────┘
             │
             └──→ Prometheus UI
                  ├─ PromQL queries
                  ├─ Time-series graphing
                  └─ Alert evaluation


    ┌────────────────────────────────────────────────┐
    │        Loki                                    │
    │  - Log aggregation (receives from services)    │
    │  - LogQL query engine                          │
    └────────────────────────────────────────────────┘
             │
             └──→ Grafana Loki datasource
                  ├─ Full-text log search
                  ├─ Label filtering
                  └─ Log patterns


    ┌────────────────────────────────────────────────┐
    │        Grafana                                 │
    │  - Unified dashboard UI                        │
    │  - Datasource: Prometheus, Loki, Jaeger        │
    └────────────────────────────────────────────────┘
```

---

## Configuration Files

### Prometheus
**Location:** `observability/prometheus.yml`
- Scrape configs for each service
- Jaeger metrics collection
- RabbitMQ metrics collection
- Evaluation intervals: 15s

### Loki
**Location:** `observability/loki-config.yml`
- Log ingestion config
- Pipeline stages for JSON parsing
- Storage configuration (boltdb-shipper)
- Retention policies

### Grafana
**Datasources:** `observability/grafana/provisioning/datasources/datasources.yml`
- Prometheus (default)
- Jaeger
- Loki
- Derived fields for TraceID linking

**Dashboards:** `observability/grafana/provisioning/dashboards/dashboards.yml`
- Pre-provisioned dashboard provider
- Location: `/etc/grafana/provisioning/dashboards`

---

## Test Scenarios

### Test 1: Verify Services Healthy

```bash
# Check all containers
docker-compose ps

# Expected: All services "Up" or "healthy"
# Jaeger:       Up
# Prometheus:   Up
# Loki:         Up
# Grafana:      Up
# Plus: Gateway, Accounts, Transfers, Monolith (all healthy)
```

**Status:** ✅ Pass if all services running

---

### Test 2: Access Jaeger UI

```bash
# Open browser
open http://localhost:16686

# UI should show:
# - Service selector dropdown (currently no traces)
# - Operations list (empty)
# - Search interface
```

**Status:** ✅ Pass if UI loads and is interactive

---

### Test 3: Verify Prometheus Scraping

```bash
# Open browser
open http://localhost:9090

# Navigate to: Status → Targets
# Should show:
# - jaeger:14269 (Up)
# - gateway:8080 (Up or Down - OK if Down)
# - accounts-service:8080 (Up)
# - transfers-service:8080 (Up)
# - monolith:8080 (Up)
# - rabbitmq:15692 (Up)

# Try a query:
# Status → Graph
# Query: up{job="prometheus"}
# Should return 1 (Prometheus itself is up)
```

**Status:** ✅ Pass if at least Prometheus scrapes itself

---

### Test 4: Verify Loki Running

```bash
# Check Loki logs
docker-compose logs loki | head -20
# Should show: "Ready to start Loki" or "listening"

# Or curl health endpoint
curl http://localhost:3100/ready
# Expected: 200 OK
```

**Status:** ✅ Pass if Loki responds to health check

---

### Test 5: Access Grafana & Configure Datasources

```bash
# Open browser
open http://localhost:3000

# Login: admin / admin
# (or create new admin account if first run)

# Verify datasources (Configuration → Data Sources)
# Should see:
# ✅ Prometheus (green checkmark)
# ✅ Loki (green checkmark)
# ✅ Jaeger (green checkmark)
```

**Status:** ✅ Pass if all 3 datasources are green

---

### Test 6: Generate Traces (via API Call)

```bash
# Register user
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "trace-test@example.com",
    "password": "SecurePassword123",
    "name": "Trace Tester"
  }'

# Save token
TOKEN="<accessToken>"

# Create account (generates trace)
curl -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}'

# Wait 1-2 seconds for trace to reach Jaeger
sleep 2

# Check Jaeger for trace
open http://localhost:16686
# Service: api-gateway (or accounts-service)
# Operation: POST /accounts
# Should see trace with spans
```

**Verification:**
```bash
# Check if Jaeger received traces
docker-compose logs jaeger | grep -i "span\|trace" | head -5
```

**Status:** ✅ Pass if trace appears in Jaeger UI

---

### Test 7: View Trace Details

In Jaeger UI:
1. Select **Service**: "api-gateway"
2. Select **Operation**: "POST /accounts"
3. Click on any trace
4. Should show:
   - Trace timeline
   - Spans for:
     - api-gateway (root)
     - accounts-service (called via HTTP)
   - Latency breakdown per span
   - Tags (http.method, http.status_code, etc.)

**Status:** ✅ Pass if full trace visible with timing

---

### Test 8: Check Metrics in Prometheus

```bash
# Open Prometheus
open http://localhost:9090

# Try these queries:

# HTTP request count
query: rate(http_server_requests_total[5m])
# Should show requests per second for each service

# P99 latency for accounts-service
query: histogram_quantile(0.99, rate(http_server_request_duration_seconds_bucket{job="accounts-service"}[5m]))
# Should show latency in seconds

# Error rate
query: rate(http_server_requests_total{status=~"5.."}[5m])
# Should show 5xx errors (likely 0 for healthy flow)
```

**Status:** ✅ Pass if metrics queries return data

---

### Test 9: View Logs in Grafana

```bash
# Open Grafana
open http://localhost:3000

# Navigate to: Explore
# Select datasource: "Loki"
# Query: {service="accounts-service"}

# Should show:
# - JSON logs from accounts-service
# - Fields: timestamp, level, message, traceId, service
# - Labels: service, level, host, pod

# Filter by error:
# {service="accounts-service", level="Error"}
# Should show any error logs
```

**Status:** ✅ Pass if logs visible and searchable

---

### Test 10: Link Trace to Logs

In Grafana Loki explore:
1. Find a log entry with `traceId` field
2. Click the `traceId` value
3. Should open Jaeger trace for that ID
4. See full distributed trace context

**Status:** ✅ Pass if trace link works

---

### Test 11: Full End-to-End Observability Flow

Execute a complete transfer flow and verify observability:

```bash
# 1. Create two users
curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "alice@test.com",
    "password": "SecurePassword123",
    "name": "Alice"
  }'

ALICE_TOKEN="<accessToken>"

curl -X POST http://localhost:5000/auth/register \
  -H "Content-Type: application/json" \
  -d '{
    "email": "bob@test.com",
    "password": "SecurePassword123",
    "name": "Bob"
  }'

BOB_TOKEN="<accessToken>"

# 2. Create accounts
ALICE_ACCOUNT=$(curl -s -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' | jq -r '.id')

BOB_ACCOUNT=$(curl -s -X POST http://localhost:5000/accounts \
  -H "Authorization: Bearer $BOB_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{}' | jq -r '.id')

# 3. Attempt transfer (will fail: insufficient funds, but that's OK)
curl -X POST http://localhost:5000/transfers \
  -H "Authorization: Bearer $ALICE_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "sourceAccountId": "'$ALICE_ACCOUNT'",
    "targetAccountId": "'$BOB_ACCOUNT'",
    "amount": 100,
    "reference": "Observable transfer"
  }'

# 4. Wait for traces
sleep 2

# 5. Check Jaeger for full trace
open http://localhost:16686
# Filter: Service = api-gateway, Operation = POST /transfers
# Should see trace with:
#   - Gateway span
#   - accounts-service span (called twice)
#   - transfers-service span
#   - Full latency breakdown
```

**Verification in Jaeger:**
- Root span: "POST /transfers" (Gateway)
- Child span: "HTTP GET /accounts/{id}" (FindAccount)
- Child span: "HTTP POST /accounts/{id}/debit" (Debit - fails)
- TraceId propagated through all spans

**Status:** ✅ Pass if full distributed trace visible

---

## Observability Best Practices

### 1. Naming Conventions
- Services: lowercase-with-hyphens (accounts-service)
- Operations: METHOD /path (POST /transfers)
- Metrics: snake_case (http_request_duration_seconds)
- Labels: lowercase (job, service, environment)

### 2. Important Metrics to Monitor

| Metric | What it Tells | Alert Threshold |
|--------|---|---|
| `http_server_requests_total` | Request volume | N/A (informational) |
| `http_server_request_duration_seconds` (P99) | Latency | > 1 second |
| `http_server_requests_total{status=~"5.."}` | Errors | > 0.1% of requests |
| `rabbitmq_queue_messages_ready` | Queue depth | > 100 messages |
| `process_runtime_dotnet_gc_collections_count` | GC pressure | Increasing trend |

### 3. Log Levels

Use consistently:
- **DEBUG**: Detailed flow tracing (request received, processing step)
- **INFO**: Important business events (transfer executed, notification sent)
- **WARN**: Recoverable issues (retry attempt, circuit breaker state change)
- **ERROR**: Non-recoverable errors (failed database write, service unavailable)

### 4. Adding Custom Traces

To add custom spans in code:

```csharp
using var activity = Activity.StartActivity("CustomOperation");
activity?.SetTag("user.id", userId);
activity?.SetTag("transfer.amount", amount);

try
{
    // Do work
    activity?.SetStatus(ActivityStatusCode.Ok);
}
catch (Exception ex)
{
    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}
```

---

## Performance Impact

### Overhead

- **Traces**: ~0.5-1% latency (with full sampling, < 100ms extra per request)
- **Metrics**: ~0.5% CPU (Prometheus scraping minimal)
- **Logs**: Already JSON output, no extra overhead

### Optimization Tips

1. **Sampling**: Currently 100% (all traces). In production, reduce to 10%:
   ```csharp
   .SetSampler(new ProbabilisticSampler(0.1))
   ```

2. **Batch Exporting**: Default 512 spans/batch. Increase for high throughput:
   ```csharp
   .AddOtlpExporter(opts => opts.BatchExportProcessorOptions.MaxExportBatchSize = 1024)
   ```

3. **Storage**: Prometheus keeps ~15 days by default. For production, increase:
   ```yaml
   # In prometheus.yml
   command:
     - "--storage.tsdb.retention.time=30d"
   ```

---

## Troubleshooting

### Issue: No traces in Jaeger

**Check:**
1. Jaeger receiver enabled: `COLLECTOR_OTLP_ENABLED=true`
2. Services exporting to correct endpoint: `OpenTelemetry__ExporterEndpoint=http://jaeger:4317`
3. Jaeger logs for errors: `docker-compose logs jaeger | grep -i error`

**Fix:**
```bash
# Restart Jaeger
docker-compose restart jaeger

# Make test request
curl http://localhost:5000/accounts ...

# Wait 2s and check Jaeger
open http://localhost:16686
```

### Issue: Prometheus not scraping metrics

**Check:**
1. Services have `/metrics` endpoint exposed
2. Prometheus config points to correct targets (prometheus.yml)
3. Network connectivity: `docker-compose exec prometheus curl http://accounts-service:8080/metrics`

**Current Status:** Services export via OTLP, not Prometheus scrape. Metrics flow through Jaeger → then to Prometheus (future enhancement).

### Issue: Loki logs not showing

**Check:**
1. Loki running: `docker-compose ps loki`
2. Log format is JSON: `docker-compose logs accounts-service | head -1`
3. Grafana has Loki datasource configured

**Fix:**
```bash
# Restart Loki
docker-compose restart loki

# Make a request to generate logs
curl http://localhost:5000/accounts ...

# Check logs in Grafana Explore → Loki
```

### Issue: Grafana datasources red/down

**Check:**
1. Services running: `docker-compose ps`
2. Datasource URLs correct (prometheus:9090, loki:3100, jaeger:16686)
3. Network connectivity

**Fix:**
```bash
# Restart Grafana
docker-compose restart grafana

# Verify datasources in UI after 30s
open http://localhost:3000
# Configuration → Data Sources
```

---

## Next Steps (Phase 5)

- **E2E Testing Suite**: comprehensive integration tests covering all observability aspects
- **Alert Rules**: Define Prometheus alerts for production readiness
- **Dashboard Enhancements**: Create custom Grafana dashboards for business metrics
- **Log Retention**: Configure long-term log storage and archival

---

## Cleanup

```bash
# Stop everything
docker-compose down

# Remove observability volumes (fresh start)
docker-compose down -v
```

---

## Summary

**Observability Stack Successfully Deployed When:**
- ✅ Jaeger UI accessible (http://localhost:16686)
- ✅ Prometheus UI accessible (http://localhost:9090)
- ✅ Grafana UI accessible (http://localhost:3000)
- ✅ Traces visible in Jaeger after API calls
- ✅ Metrics queryable in Prometheus
- ✅ Logs searchable in Grafana/Loki
- ✅ TraceId linked between systems
- ✅ Full distributed traces visible (Gateway → MS1 → MS2)

**Production Ready When:**
- Custom alert rules defined
- Log retention policy configured
- Sampling rate optimized (10-20%)
- Custom dashboards built for team
- Runbooks written for common alerts

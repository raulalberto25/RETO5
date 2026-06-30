# ADR-009: Observability Stack (OpenTelemetry + Jaeger + Prometheus + Grafana + Loki)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Distributed microservices require end-to-end visibility across network boundaries. A single user request may span 3+ services; without observability, debugging is impossible.

### Three Pillars of Observability
1. **Traces:** Request flow across services (end-to-end request path)
2. **Metrics:** System health (latency, error rate, resource usage)
3. **Logs:** Detailed events with structured context

### Challenge Requirement
"Implement the three pillars of observability across all components, ensuring a single TraceId propagates end-to-end"

---

## Decision

**Use OpenTelemetry (OTEL) as the instrumentation framework**, with backends:
- **Traces:** Jaeger (distributed tracing UI)
- **Metrics:** Prometheus + Grafana (time-series metrics + dashboards)
- **Logs:** Loki (log aggregation + Grafana integration)

### Architecture

```
Services (Gateway, Accounts, Transfers, Monolith)
  ↓ (OpenTelemetry SDK)
OTLP Exporter (OTLP/gRPC protocol)
  ↓
OpenTelemetry Collector (optional aggregation point)
  ├─ → Jaeger (traces)
  ├─ → Prometheus (metrics)
  └─ → Loki (logs)
  
UI Layer:
  ├─ Jaeger UI (http://localhost:16686) - traces
  └─ Grafana (http://localhost:3000) - metrics + logs + dashboard
```

### Rationale

| Component | Choice | Alternative | Why |
|-----------|--------|------------|-----|
| **Instrumentation** | OpenTelemetry | Datadog, New Relic, Elastic | Vendor-neutral, open-source, language-agnostic |
| **Traces** | Jaeger | Zipkin, AWS X-Ray | Self-hosted, excellent UI, CNCF project |
| **Metrics** | Prometheus | InfluxDB, Datadog | Time-series DB for metrics, Grafana integrates natively |
| **Visualize Metrics** | Grafana | Kibana, Splunk | Best-in-class dashboarding, free & open-source |
| **Logs** | Loki | ELK (Elasticsearch), Splunk | Lightweight log aggregation, Grafana-native integration |

---

## Consequences

### Positive

✅ **Vendor Independence**
- OpenTelemetry is open standard (not locked to Datadog, Splunk, etc.)
- Can switch backends (Jaeger → Zipkin) without code changes
- Export to multiple backends simultaneously if needed

✅ **End-to-End Tracing**
- Single TraceId flows: Client → Gateway → Accounts → Database
- Distributed tracing shows which service is slow
- W3C TraceContext standard (HTTP headers + RabbitMQ message headers)

✅ **Integrated Observability**
- Grafana unifies metrics + logs + traces in one interface
- Single login, single dashboard
- Correlation across all three pillars

✅ **Cost-Effective**
- All open-source (zero licensing)
- Self-hosted (no SaaS costs)
- Modest resource requirements for Phase 1-2

✅ **Standardized Instrumentation**
- OpenTelemetry is industry standard
- Team skills transferable to other organizations
- Rich ecosystem of instrumentation libraries

### Negative

❌ **Operational Complexity**
- 4 additional services: Jaeger, Prometheus, Grafana, Loki
- Must monitor the monitors (what if Prometheus crashes?)
- Increased docker-compose complexity

❌ **Storage Concerns**
- Traces stored locally (Jaeger: BadgerDB by default)
- Metrics stored locally (Prometheus: local time-series)
- No built-in replication (Phase 3: add persistence layer)

❌ **Learning Curve**
- OpenTelemetry concepts: Spans, Traces, Baggage
- Prometheus PromQL query language
- Loki LogQL query language
- Team needs orientation

---

## Implementation Details

### OpenTelemetry Instrumentation

**Required Packages** (per service):
- `OpenTelemetry.Extensions.Hosting`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.Http`
- `OpenTelemetry.Instrumentation.SqlClient` (for Accounts/Transfers)
- `OpenTelemetry.Exporter.OpenTelemetryProtocol`

**Configuration** (Program.cs):
```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService("accounts-service")
        .AddAttributes(new[] { 
            KeyValuePair.Create("environment", "production"),
            KeyValuePair.Create("version", "1.0")
        }))
    .WithTracing(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSqlClientInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri("http://localhost:4317")))
    .WithMetrics(b => b
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(opts => opts.Endpoint = new Uri("http://localhost:4317")));
```

### W3C TraceContext Propagation

**HTTP Headers (automatic with OpenTelemetry):**
- `traceparent: 00-<trace-id>-<span-id>-<trace-flags>`
- `tracestate: vendor-specific`

**RabbitMQ Message Headers (manual implementation):**
```csharp
// When publishing
var tracingHeader = Activity.Current?.Id ?? "";
basicProperties.Headers["traceparent"] = Encoding.UTF8.GetBytes(tracingHeader);

// When consuming
if (basicProperties.Headers.TryGetValue("traceparent", out var tracingValue))
{
    var tracingHeader = Encoding.UTF8.GetString((byte[])tracingValue);
    // Extract TraceContext from header
}
```

### Structured Logging

**Configuration:**
```csharp
builder.Logging.AddConsole();
// Or for JSON output to Loki:
builder.Logging.AddJsonConsole(opts =>
{
    opts.IncludeScopes = true;
    opts.IncludeExceptionMessage = true;
});
```

**Usage (with correlation):**
```csharp
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

using (logger.BeginScope(new Dictionary<string, object>
{
    { "TraceId", Activity.Current?.Id },
    { "UserId", userId },
    { "TransferId", transferId }
}))
{
    logger.LogInformation("Transfer initiated: {@Transfer}", transfer);
}
```

### Metrics (Custom)

```csharp
var meter = new Meter("finbank.accounts");
var activeAccountsCounter = meter.CreateCounter<int>("accounts.active");

// Increment when account created
activeAccountsCounter.Add(1);
```

### Jaeger Configuration

**docker-compose:**
```yaml
jaeger:
  image: jaegertracing/all-in-one:latest
  ports:
    - "16686:16686"  # UI
    - "4317:4317"    # OTLP receiver (gRPC)
  environment:
    - COLLECTOR_OTLP_ENABLED=true
```

### Grafana Datasources

**Prometheus:**
- URL: `http://prometheus:9090`
- Type: Prometheus
- PromQL: `rate(http_request_duration_seconds_bucket[5m])`

**Loki:**
- URL: `http://loki:3100`
- Type: Loki
- LogQL: `{job="accounts-service"} | json | latency > 500ms`

### Key Metrics to Monitor

| Metric | Type | Alert Threshold |
|--------|------|-----------------|
| `http_request_duration_seconds` | Histogram | P99 > 1s |
| `http_request_total` | Counter | Errors > 1% |
| `db_query_duration_seconds` | Histogram | P99 > 500ms |
| `consumer_lag_bytes` | Gauge (RabbitMQ) | > 1MB |
| `process_resident_memory_bytes` | Gauge | > 500MB |

---

## Implementation Checklist

- [ ] Add OpenTelemetry packages to all 4 services
- [ ] Configure OTLP exporters (endpoint: localhost:4317)
- [ ] Implement W3C TraceContext propagation (HTTP + RabbitMQ)
- [ ] Add structured JSON logging
- [ ] Deploy Jaeger, Prometheus, Loki in docker-compose
- [ ] Create Grafana dashboard (latency, error rate, throughput)
- [ ] Verify trace propagation end-to-end (single TraceId across services)
- [ ] Set up alerts (P99 latency > 1s, error rate > 1%)
- [ ] Document observability queries (how to find slow transfers)

---

## Verification Steps

1. **Execute a transfer via API**
2. **Trace in Jaeger:**
   - Navigate to http://localhost:16686
   - Search for service="transfers-service"
   - Find the transfer request trace
   - Verify TraceId appears in all spans (gateway → transfers → accounts → database)

3. **Metrics in Grafana:**
   - Login: admin/admin
   - Select Prometheus datasource
   - Query: `rate(http_requests_total[5m])`
   - Should show throughput per service

4. **Logs in Grafana Explore:**
   - Select Loki datasource
   - Query: `{service_name="accounts-service"} | json | level=error`
   - Should show any errors with full context

---

## Related Decisions

**Depends On:**
- ADR-002 (Transfers MS with events)
- ADR-007 (Saga for multi-service flow)

**Enables:**
- Phase 4: Alerting on SLAs (latency, error rate)
- Phase 5: Performance optimization (identify bottlenecks)

---

## References
- **OpenTelemetry:** https://opentelemetry.io/
- **Jaeger:** https://www.jaegertracing.io/
- **Prometheus:** https://prometheus.io/
- **Grafana:** https://grafana.com/
- **Loki:** https://grafana.com/oss/loki/
- **W3C TraceContext:** https://w3c.github.io/trace-context/

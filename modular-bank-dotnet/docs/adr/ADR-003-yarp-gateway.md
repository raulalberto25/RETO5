# ADR-003: API Gateway Selection (YARP)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

As the monolith transitions to microservices (Accounts MS, Transfers MS, remaining monolith), client requests must be routed transparently to the appropriate backend service. An API Gateway provides:
- Single entry point for clients
- Request routing by path/method
- Request/response transformation
- Authentication/authorization enforcement
- Rate limiting and circuit breaking
- Request tracing and logging

### Requirements
- Route `/accounts/**` → Accounts MS
- Route `/transfers/**` → Transfers MS
- Route `/auth/**`, `/notifications/**`, `/audit/**` → Monolith
- Preserve JWT authorization headers
- Zero latency overhead (minimal)
- Should not introduce additional language/runtime (align with .NET ecosystem)
- Support distributed tracing (OpenTelemetry)

### Operational Constraints
- Single gateway instance initially (no HA required in Phase 1)
- Configuration must be code-based (not YAML/JSON files that require restart)
- Should support dynamic route updates (future-proofing)

---

## Decision

**Use YARP (Yet Another Reverse Proxy) as the API Gateway.**

YARP is a reverse proxy library built by Microsoft (part of ASP.NET Core ecosystem) designed for high-performance request routing in .NET applications.

### Rationale

| Criterion | YARP | Nginx | Envoy | Kong | AWS ALB |
|-----------|------|-------|-------|------|---------|
| **Language** | .NET (C#) | C | C | Go/Lua | N/A |
| **Learning Curve** | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ |
| **Setup Complexity** | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Team Expertise** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐ |
| **Extensibility** | ⭐⭐⭐⭐⭐ | ⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐ |
| **Deployment** | Docker (ASP.NET) | Docker/Linux | Docker/K8s | Docker/K8s | Managed |
| **Cost** | $0 | $0 | $0 | Free/Paid | Paid |
| **OpenTelemetry** | Native | Manual | Good | Partial | Good |

### Specific Advantages

1. **Technology Alignment**
   - Built in .NET/C# (same language as team expertise)
   - Integrates seamlessly with ASP.NET Core DI container
   - Can share infrastructure code (JWT validation, logging, middleware)
   - No language context switching for operations/debugging

2. **Minimal Operational Overhead**
   - Single service in `docker-compose` (no separate Nginx/Envoy binary)
   - Configuration as code in C# (strongly typed, refactorable)
   - Standard ASP.NET Core logging and monitoring
   - Built-in dependency injection for custom middleware

3. **Extensibility for Phase 2+**
   - Custom middleware for request correlation IDs
   - Custom routing logic if needed
   - Integration with Polly resilience policies
   - OpenTelemetry instrumentation is native

4. **Zero-Latency Architecture**
   - Reverse proxy implemented in-process (no fork/exec overhead)
   - Efficient connection pooling to backend services
   - Minimal memory footprint compared to separate gateway process

5. **Future Patterns**
   - Rate limiting per service/tenant
   - Request prioritization by endpoint criticality
   - Gradual traffic shifting (canary deployments)
   - Request/response modification middleware

---

## Alternative Options Considered

### Option A: Nginx ❌
**Why Not Recommended:**
- Requires separate binary (Linux/Docker image)
- Configuration in Nginx syntax (different from team's .NET expertise)
- Custom logic requires Lua scripting (learning curve, performance risk)
- Operational overhead: separate logging, monitoring, health checks
- Less seamless integration with ASP.NET Core observability (OpenTelemetry)

**When to use instead:**
- Ultra-high-throughput scenarios (1M+ requests/sec)
- Multi-language teams requiring vendor-neutral gateway
- Existing Nginx expertise in ops team

### Option B: Envoy ❌
**Why Not Recommended:**
- Designed for Kubernetes/microservices mesh (overkill for Phase 1)
- Operational complexity: requires control plane (Consul, Istio)
- YAML configuration not code-driven
- Learning curve steep for team without Envoy experience
- Minimal team expertise benefit

**When to use instead:**
- Kubernetes-first architecture
- Need for advanced observability/traffic management (Istio mesh)
- Polyglot microservices ecosystem

### Option C: Kong ❌
**Why Not Recommended:**
- Open-source version requires PostgreSQL for configuration
- Gateway runs in separate process (operational overhead)
- Team would need to learn Kong ecosystem and Lua
- Licensing complexity (open source vs enterprise)

**When to use instead:**
- Need for plugin marketplace (pre-built integrations)
- Multi-tenant SaaS gateway
- Team already invested in Kong

### Option D: AWS ALB (Application Load Balancer) ❌
**Why Not Recommended:**
- Locked to AWS (infrastructure vendor lock-in)
- Not applicable if deployed on-premise or other cloud
- Costs accumulate ($0.0225/hour per ALB)
- Limited customization without AWS Lambda integration

**When to use instead:**
- Pure AWS infrastructure
- High availability/redundancy required from cloud provider
- Team prefers managed services

---

## Consequences

### Positive

✅ **Development Velocity**
- Gateway implemented in familiar C#/.NET
- Developers can add custom middleware quickly
- Reuse existing ASP.NET Core patterns (DI, logging, middleware)

✅ **Operational Simplicity**
- Single Docker image (gateway + monolith can even share base image)
- Standard ASP.NET Core health checks
- Integrated logging to console/JSON (Loki/ELK compatible)
- No learning curve for ops team (already familiar with .NET)

✅ **Unified Observability**
- OpenTelemetry instrumentation identical to microservices
- W3C TraceContext propagation automatic
- Single monitoring dashboard for all services (gateway + backends)
- No separate Prometheus exporter required

✅ **Cost-Effective**
- Open source (MIT license)
- No additional infrastructure required
- Can scale horizontally using standard .NET load balancing

### Negative

❌ **Lower Throughput Than Nginx**
- YARP achieves ~100k req/sec (vs Nginx ~1M req/sec)
- Acceptable for Phase 1 (expected traffic << 100k req/sec)
- Becomes issue only if traffic scales beyond projections

❌ **Community Size**
- Smaller community than Nginx/Kong
- Fewer third-party middleware libraries
- Must implement custom middleware for non-standard features

❌ **Operational Monitoring**
- Cannot use Nginx-specific monitoring tools
- Must build custom metrics (Prometheus integration required for Phase 4)

---

## Configuration Example

```csharp
var routeConfig = new ReverseProxyConventionBuilder()
    .AddRoute()
        .WithClusterId("accounts-service")
        .WithPath("/accounts")
        .Build()
    .AddRoute()
        .WithClusterId("transfers-service")
        .WithPath("/transfers")
        .Build()
    .AddRoute()
        .WithClusterId("monolith")
        .WithPath("/{**catch-all}")
        .Build()
    .AddCluster()
        .WithClusterId("accounts-service")
        .WithDestination("accounts-service", "http://accounts-service:8080")
        .Build()
    .AddCluster()
        .WithClusterId("transfers-service")
        .WithDestination("transfers-service", "http://transfers-service:8080")
        .Build()
    .AddCluster()
        .WithClusterId("monolith")
        .WithDestination("monolith", "http://monolith:8080")
        .Build();

// Automatic JWT propagation via middleware
app.UseAuthentication();
app.UseAuthorization();

// YARP reverse proxy routing
app.MapReverseProxy();
```

---

## Implementation Checklist

- [ ] Create `gateway/Gateway.csproj` (ASP.NET Core 10 Web API)
- [ ] Add `Yarp.ReverseProxy` NuGet package
- [ ] Configure route/cluster configuration in Program.cs
- [ ] Implement JWT authorization middleware (propagate to backends)
- [ ] Add correlation ID middleware for request tracing
- [ ] Create Dockerfile for gateway
- [ ] Update docker-compose.yml with gateway service
- [ ] Test routing to all backend services
- [ ] Verify authorization headers propagated correctly

---

## Migration Strategy

**Phase 1:** Gateway routes to Accounts MS + Monolith
- `/accounts/**` → Accounts MS
- Everything else → Monolith

**Phase 2:** Gateway routes to Accounts MS + Transfers MS + Monolith
- `/accounts/**` → Accounts MS
- `/transfers/**` → Transfers MS
- Everything else → Monolith

**Phase 3+:** Additional microservices gradually added to routes

---

## Related Decisions

**Depends On:**
- ADR-001, ADR-002 (microservice extraction)
- Deployment infrastructure (Docker, networking)

**Enables:**
- Distributed tracing (gateway inserts trace headers)
- Rate limiting per service
- Request correlation across services

---

## References
- **Project:** YARP (Microsoft Reverse Proxy) — https://microsoft.github.io/reverse-proxy/
- **Source:** https://github.com/microsoft/reverse-proxy
- **Benchmarks:** https://codeload.github.com/microsoft/reverse-proxy/zip/main
- **Pattern:** Backend for Frontend (BFF)

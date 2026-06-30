# ADR-008: Internal Microservice Architecture (Hexagonal)

**Status:** Accepted

**Date:** 2026-06-29

---

## Context

Each microservice (Accounts MS, Transfers MS) must maintain clean separation between business logic and infrastructure concerns. The choice of internal architecture affects:
- Testability (domain logic independent of frameworks)
- Maintainability (clear boundaries)
- Evolution (switching databases, messaging brokers, protocols)

### Challenge Requirement
"Choose and justify an internal architecture (e.g., Hexagonal, CQRS, or Layered)"

---

## Decision

**Use Hexagonal Architecture (Ports & Adapters) for both MS1 (Accounts) and MS2 (Transfers).**

### Structure

```
services/accounts-service/
├── Domain/                           ← Pure business logic
│   ├── Account.cs
│   ├── Money.cs
│   └── AccountId.cs
│
├── Application/                      ← Business rules, ports
│   ├── Ports/
│   │   └── IAccountsRepository.cs   ← OUTPUT port
│   ├── AccountsUseCase.cs           ← Orchestrator
│   └── Dto/
│       └── AccountSummary.cs
│
├── Infrastructure/                   ← Concrete implementations
│   ├── AccountsRepository.cs        ← Adapter: Domain ↔ DB
│   ├── AccountsDbContext.cs         ← EF Core configuration
│   └── Resilience/
│       └── RepositoryRetryPolicy.cs ← Polly resilience
│
└── Api/                              ← INPUT port (HTTP)
    └── AccountsEndpoints.cs         ← Adapter: HTTP ↔ Domain
```

### Layers & Dependencies

```
Domain (innermost)
  ├─ No external dependencies (100% testable without frameworks)
  ├─ Contains: Account, Money, AccountId value objects
  └─ Business rules: validation, balance constraints

Application (orchestration)
  ├─ Depends on: Domain
  ├─ Contains: AccountsUseCase, Ports (interfaces)
  ├─ Does NOT depend on: Infrastructure, HTTP, Database
  └─ Business workflows: CreateAccount, Debit, Credit

Infrastructure (adapters)
  ├─ Depends on: Application + Domain
  ├─ Contains: Repository (DB adapter), DbContext, resilience policies
  └─ Implements ports defined in Application

Api (HTTP adapter)
  ├─ Depends on: Application + Domain
  ├─ Contains: Endpoints, DTOs, HTTP error mapping
  └─ Transforms HTTP requests/responses to/from domain
```

### Information Flow

```
HTTP Request
  ↓
Api/AccountsEndpoints (adapter)
  ↓
Application/AccountsUseCase (orchestrator)
  ├─ Validates using Domain objects
  ├─ Calls ports (IAccountsRepository)
  └─ Returns domain objects
  ↓
Infrastructure/AccountsRepository (adapter)
  ├─ Translates domain → Entity Framework entities
  ├─ Executes SQL
  └─ Translates results → domain objects
  ↓
Database

(Response follows reverse path)
```

### Rationale

| Criterion | Hexagonal | CQRS | Layered |
|-----------|-----------|------|---------|
| **Simplicity** | ⭐⭐⭐⭐ | ⭐⭐⭐ | ⭐⭐⭐⭐⭐ |
| **Testability** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐⭐ | ⭐⭐⭐ |
| **Flexibility** | ⭐⭐⭐⭐⭐ | ⭐⭐⭐⭐ | ⭐⭐⭐ |
| **Tech Debt** | Low | Medium | High |
| **Current Need** | Perfect fit | Over-engineered | Too loose |

**Chosen: Hexagonal** because:
1. Ports & Adapters explicitly separate domain from infrastructure
2. Easy to test: create test adapters (in-memory repositories)
3. Framework-independent domain (no EntityFramework imports in Account.cs)
4. Scales to larger team (clear boundaries prevent merge conflicts)
5. Matches challenge requirement (explicit port/adapter pattern)

---

## Alternative A: CQRS ❌

Command Query Responsibility Segregation (separate read/write paths):
- Write model: Account command handlers (CreateAccount, Debit, Credit)
- Read model: Optimized read tables (AccountSummary views)

**Why not:**
- Overkill for current domain (no complex read requirements)
- Adds event sourcing complexity (eventual consistency)
- Maintenance burden for future team
- Acceptable once query complexity justifies it (Phase 4+)

**When to use instead:**
- Complex read requirements (analytics, reporting)
- High-traffic read paths (separate scaling)
- Event sourcing architecture (already have events)

---

## Alternative B: Layered (N-tier) ❌

Typical three-layer: Presentation → Business → Data

**Why not:**
- Boundary between layers is procedural, not domain-driven
- Business logic leaks into all layers
- Hard to test domain independent of frameworks
- Dependencies point downward (bottom tightly coupled)
- Over time: mixing of concerns

**When to use instead:**
- Small CRUD applications (monolith acceptable)
- No complex business rules
- Team unfamiliar with DDD/Hexagonal

---

## Consequences

### Positive

✅ **Framework Independence**
- Domain logic has zero dependencies on EF Core, ASP.NET, RabbitMQ
- Can test Account.cs without any frameworks (plain C# unit tests)
- Can swap database: write new RepositoryAdapter, domain unchanged

✅ **Testability**
```csharp
// Test domain without frameworks
[Fact]
public void Account_Debit_InsufficientFunds_ThrowsException()
{
    var account = new Account(UserId.Of(Guid.NewGuid()), Money.Of(100));
    var insufficientFunds = Money.Of(150);
    
    Assert.Throws<InvalidOperationException>(() => account.Debit(insufficientFunds));
}

// Test use case with mock repository (no database)
[Fact]
public async Task CreateAccount_InsertsAndReturns()
{
    var mockRepository = new InMemoryAccountsRepository();
    var useCase = new AccountsUseCase(mockRepository);
    
    var result = await useCase.CreateAccountAsync(userId);
    
    Assert.NotNull(result);
    Assert.Single(mockRepository.Accounts);
}
```

✅ **Clear Contracts**
- Ports (IAccountsRepository) are explicit contracts
- Easy for team to understand dependencies
- Future: easy to add caching layer (CachedAccountsRepository)

✅ **Evolution Path**
- Start with single database implementation
- Add caching: CachedAccountsRepository wraps repository
- Add event publishing: EventPublishingRepository wraps everything
- No domain changes needed

### Negative

❌ **Directory Structure Complexity**
- More folders than flat Layered approach
- Requires discipline (developers must respect boundaries)
- Learning curve for team unfamiliar with hexagonal

❌ **Mapping Boilerplate**
- Must map: Domain objects ↔ EF entities ↔ DTOs ↔ HTTP
- Four representations of "Account" (domain, entity, dto, response)
- Some repetition (though: intention-revealing, explicit)

❌ **Indirection**
- Request flows through multiple layers (longer stack trace)
- Debugging requires understanding each layer's responsibility
- New team members need architecture orientation

---

## Implementation Guidelines

### Domain Layer Rules

✅ **Allowed:**
- Plain C# classes (no base classes from frameworks)
- Business logic and validation
- Value objects and aggregates
- Exceptions for domain errors

❌ **NOT Allowed:**
- Entity Framework `DbSet<Account>`
- Async/await (make synchronous)
- Dependencies on `Microsoft.AspNetCore.*`
- References to repositories (depend on abstractions in Application)

### Application Layer Rules

✅ **Allowed:**
- UseCase/orchestrator classes
- Port interfaces (abstractions)
- DTO definitions
- Async/await (for I/O)

❌ **NOT Allowed:**
- Direct database access
- HTTP details
- Concrete repository implementations
- Framework-specific attributes

### Infrastructure Layer Rules

✅ **Allowed:**
- Concrete implementations of ports (repositories)
- EF Core DbContext, migrations, queries
- Resilience policies (Polly)
- Third-party library integrations

❌ **NOT Allowed:**
- Business logic (belongs in Domain/Application)
- HTTP details
- Multiple responsibilities in single adapter

### Api Layer Rules

✅ **Allowed:**
- Endpoint definitions
- HTTP status code mapping
- Request/response validation
- Authentication/authorization

❌ **NOT Allowed:**
- Business logic (use Application/UseCase)
- Direct database access

---

## Related Decisions

**Depends On:**
- ADR-001, ADR-002 (microservice extraction)

**Enables:**
- Clean testing (Phase 4: comprehensive test suite)
- Evolution flexibility (swap implementations easily)

---

## References
- **Pattern:** Hexagonal Architecture (Alistair Cockburn, 2005)
- **Book:** Domain-Driven Design (Eric Evans)
- **Package:** C# no external dependencies for Domain layer

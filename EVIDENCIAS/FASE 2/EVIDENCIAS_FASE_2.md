# EVIDENCIAS REQUERIDAS - FASE 2
## FinBank: Extracción del Módulo Transfers como Microservicio + Comunicación Asincrónica

**Fecha:** 2026-06-29  
**Proyecto:** FinBank Monolithic → Microservices Migration  
**Fase:** 2 - Transfers Microservice Extraction + Event-Driven Architecture  
**Estado:** ✅ COMPLETADO

---

## 📋 ÍNDICE DE EVIDENCIAS

1. [Justificación del segundo módulo y relación de dependencia](#1-justificación-del-segundo-módulo)
2. [Segundo microservicio autónomo con BD propia](#2-microservicio-transfers-autónomo)
3. [Comunicación asincrónica vía broker](#3-comunicación-asincrónica)
4. [Diagrama de arquitectura final](#4-diagrama-de-arquitectura-final)
5. [Patrón de consistencia distribuida](#5-patrón-de-consistencia-distribuida)

---

## 1. Justificación del Segundo Módulo y Relación de Dependencia

### 📄 Documento Fuente
- **Ubicación:** `docs/adr/ADR-002-transfers-second-extraction.md`
- **Título:** "Transferencias como el segundo módulo a extraer"

### ✅ ¿Por qué Transfers fue elegido como MS2?

#### Dependencia Estratégica con MS1 (Accounts)
```
Transfers DEPENDE DE → Accounts
│
└─ Necesita verificar que cuenta existe (READ)
└─ Necesita debitar cuenta origen (WRITE)
└─ Necesita acreditar cuenta destino (WRITE)
└─ Necesita validar ownership (READ)
```

**Relación de Dependencia:**
```
MS1 (Accounts) ← INDEPENDIENTE
    ↑
    │ HTTP calls
    │
MS2 (Transfers) ← DEPENDE DE MS1
    │
    ├─ FindAccountAsync(Guid) → verifica existencia
    ├─ DebitAsync(Guid, amount) → resta balance
    ├─ CreditAsync(Guid, amount) → suma balance
    └─ Validación de ownership
```

#### Por qué MS2 después de MS1

1. **MS1 debe existir primero**
   - Transfers necesita Accounts funcionando
   - Sin Accounts MS, Transfers no puede operar
   - Arquitectura de capas: Accounts es cimiento

2. **Establece patrón de comunicación**
   - MS1 ↔ MS2 via HTTP (sincrónico)
   - MS2 → Eventos via RabbitMQ (asincrónico)
   - Demuestra ambos patrones

3. **Flujo de negocio crítico**
   - Transfers es operación principal del banco
   - Demuestra escalabilidad
   - Requiere resiliencia (Polly policies)

4. **Preparación para comunicación asincrónica**
   - MS2 es primer productor de eventos
   - RabbitMQ agregado en Phase 2
   - Saga Choreography implementado

#### Matriz de Dependencias

| Módulo | Depende de | Razón |
|--------|-----------|-------|
| **Transfers (MS2)** | Accounts (MS1) | Necesita verificar/debitar/acreditar cuentas |
| Transfers (MS2) | RabbitMQ | Publica eventos de transferencias |
| Notifications | Transfers (MS2) | Consume eventos de transferencias |
| Audit | Transfers (MS2) | Audita operaciones de transferencias |
| Monolith | Accounts (MS1) | HttpAccountsService |
| Monolith | Transfers (MS2) | Consumers escuchan eventos |

#### Arquitectura de Capas

```
┌─────────────────────────────┐
│  Auth Module (Monolith)     │ ← No depende de nada
└─────────────────────────────┘
             ↑
             │
┌──────────────────────────────────┐
│  Accounts MS1                    │ ← Base de todo
│  - Gestiona cuentas              │   No depende de MS2
└─────────────┬────────────────────┘
              │
              │ HTTP calls
              │
┌──────────────▼────────────────────┐
│  Transfers MS2                    │ ← Depende de MS1
│  - Orquesta transferencias        │   Publica eventos
│  - Publica eventos                │
└─────────────┬────────────────────┘
              │
              │ RabbitMQ Events
              │
    ┌─────────┴─────────┐
    ▼                   ▼
┌─────────────────┐ ┌──────────────────┐
│ Notifications   │ │ Audit            │
│ (Monolith)      │ │ (Monolith)       │
│ Consumers       │ │ Consumers        │
└─────────────────┘ └──────────────────┘
```

### 📊 Comparación: Transfers vs Alternativas

| Aspecto | Transfers | Notifications | Audit |
|---------|-----------|---------------|-------|
| **Complejidad** | Alta | Baja | Baja |
| **Dominio** | Crítico | Secundario | Secundario |
| **Dependencias** | Cuentas | Transferencias | Todo |
| **Riesgo** | Alto | Bajo | Bajo |
| **Escalabilidad** | Crítica | Baja | Baja |
| **Impacto** | Máximo | Mínimo | Mínimo |

**Conclusión:** Transfers es la opción correcta para MS2 por ser crítico, depender de MS1 y requerir comunicación asincrónica.

---

## 2. Microservicio Transfers Autónomo

### 📁 Estructura del Proyecto

```
services/transfers-service/
├── TransfersService.csproj           # Proyecto .NET 10 independiente
├── Program.cs                        # Configuración + DI
├── Dockerfile                        # Multi-stage build
├── appsettings.json                  # Configuración prod
├── appsettings.Development.json      # Configuración dev
│
├── Domain/                           # Lógica de negocio pura
│   ├── Transfer.cs                   # Entidad agregada
│   └── Events/
│       └── TransferExecutedEvent.cs  # CloudEvents 1.0 compliant
│
├── Application/                      # Casos de uso
│   ├── Ports/
│   │   ├── IAccountsPort.cs          # Abstracción para Accounts MS
│   │   ├── IEventPublisher.cs        # Abstracción para RabbitMQ
│   │   └── ITransfersRepository.cs   # Abstracción para persistencia
│   ├── Dtos/
│   │   └── TransferRequest.cs        # Request DTOs
│   └── TransferUseCase.cs            # Orquestador de saga
│
├── Infrastructure/                   # Implementación técnica
│   ├── TransfersDbContext.cs         # EF Core DbContext
│   ├── TransfersRepository.cs        # Implementación repositorio
│   ├── TransfersModuleExtensions.cs  # DI setup
│   ├── Http/
│   │   └── HttpAccountsAdapter.cs    # Adapter para Accounts MS
│   ├── Messaging/
│   │   ├── RabbitMqPublisher.cs      # Publisher a RabbitMQ
│   │   ├── OutboxEntry.cs           # Tabla de outbox
│   │   └── OutboxWorker.cs          # Background service que publica
│   ├── Resilience/
│   │   └── ResiliencePolicies.cs    # Polly policies
│   └── Migrations/                   # EF Core migrations
│
├── Api/                              # Exposición HTTP
│   └── TransfersEndpoints.cs         # Minimal APIs
│
└── bin/, obj/                        # Output compilado
```

### ✅ Base de Datos Propia

**Configuración Docker:**
```yaml
postgres-transfers:
  image: postgres:16
  environment:
    POSTGRES_DB: finbank_transfers
    POSTGRES_USER: bank
    POSTGRES_PASSWORD: bank
  ports:
    - "5435:5432"
  volumes:
    - postgres_transfers_data:/var/lib/postgresql/data
  healthcheck:
    test: ["CMD-SHELL", "pg_isready -U bank"]
    interval: 10s
    timeout: 5s
    retries: 5
```

**Detalles:**
- **Base de datos:** `finbank_transfers`
- **Puerto:** 5435 (aislado)
- **Usuario:** bank / bank
- **Schemas:** `transfers.*` (exclusivo)

### ✅ Tablas Creadas

```sql
-- Schema
CREATE SCHEMA transfers;

-- Tabla: transfers
CREATE TABLE transfers.transfers (
    id uuid NOT NULL PRIMARY KEY,
    source_account_id uuid NOT NULL,
    target_account_id uuid NOT NULL,
    user_id uuid NOT NULL,
    amount numeric(19,4) NOT NULL,
    reference varchar(255),
    created_at timestamp DEFAULT NOW(),
    CONSTRAINT chk_amount_positive CHECK (amount > 0),
    CONSTRAINT chk_different_accounts CHECK (source_account_id != target_account_id)
);

-- Tabla: outbox (PATRÓN OUTBOX - GARANTÍA DE ENTREGA)
CREATE TABLE transfers.outbox_entries (
    id uuid NOT NULL PRIMARY KEY,
    aggregate_id uuid NOT NULL,
    event_type varchar(255) NOT NULL,
    payload jsonb NOT NULL,
    routing_key varchar(255) NOT NULL,
    published_at timestamp NULL,
    created_at timestamp DEFAULT NOW(),
    CONSTRAINT chk_published_null_or_timestamp 
        CHECK (published_at IS NULL OR published_at IS NOT NULL)
);

-- Índices
CREATE INDEX idx_transfers_source_account ON transfers.transfers(source_account_id);
CREATE INDEX idx_transfers_target_account ON transfers.transfers(target_account_id);
CREATE INDEX idx_transfers_user_id ON transfers.transfers(user_id);
CREATE INDEX idx_outbox_published_at ON transfers.outbox_entries(published_at) 
    WHERE published_at IS NULL;  -- Para polling eficiente
```

### ✅ Endpoints Expuestos

```csharp
// POST /transfers - Ejecuta una transferencia
// GET /transfers?accountId={guid} - Historial de transferencias
// GET /health - Health check
```

### ✅ Autonomía de MS2

1. **Código independiente:** No importa módulos del monolito
2. **BD exclusiva:** Solo Transfers accede a `finbank_transfers`
3. **Comunicación:**
   - HTTP hacia Accounts MS (síncrono)
   - RabbitMQ para eventos (asincrónico)
4. **Deployable:** Dockerfile propio
5. **Escalable:** Puede replicarse sin afectar MS1

**Status:** ✅ **Microservicio autónomo completamente funcional**

---

## 3. Comunicación Asincrónica vía Broker

### 📡 Configuración RabbitMQ

**Docker Service:**
```yaml
rabbitmq:
  image: rabbitmq:3.13-management
  environment:
    RABBITMQ_DEFAULT_USER: guest
    RABBITMQ_DEFAULT_PASS: guest
  ports:
    - "5672:5672"      # AMQP
    - "15672:15672"    # Management UI
  volumes:
    - rabbitmq_data:/var/lib/rabbitmq
  healthcheck:
    test: ["CMD", "rabbitmq-diagnostics", "-q", "ping"]
    interval: 10s
    timeout: 5s
    retries: 5
```

### 🔄 Flujo de Comunicación Asincrónica

#### Paso 1: Transfers MS Publica Evento

```csharp
// En TransferUseCase.ExecuteAsync():
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

// Guarda en Outbox (garantía de entrega)
await _eventPublisher.PublishAsync("transfer.executed.v1", eventJson);
```

#### Paso 2: OutboxWorker Publica a RabbitMQ

```csharp
// OutboxWorker.cs - Background Service
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    while (!stoppingToken.IsCancellationRequested)
    {
        // 1. Lee eventos no publicados de la BD cada 5 segundos
        var unpublished = await _repository.GetUnpublishedAsync(100);
        
        // 2. Publica cada evento a RabbitMQ
        foreach (var entry in unpublished)
        {
            await _publisher.PublishAsync(entry.RoutingKey, entry.Payload);
            
            // 3. Marca como publicado (solo después de éxito)
            await _repository.MarkPublishedAsync(entry.Id);
        }
        
        await Task.Delay(5000, stoppingToken);
    }
}
```

#### Paso 3: RabbitMQ Enruta a Consumers

```
┌─────────────────────────────────────────────┐
│        RabbitMQ - banking.events            │
│        (Exchange: Topic)                    │
└──────────────────┬──────────────────────────┘
                   │
       Routing Key: transfer.executed.v*
                   │
        ┌──────────┴──────────┐
        │                     │
        ▼                     ▼
┌──────────────────┐  ┌──────────────────┐
│ notifications.   │  │ audit.           │
│ transfer-exec    │  │ transfer-exec    │
│                  │  │                  │
│ Queue (durable)  │  │ Queue (durable)  │
└────────┬─────────┘  └────────┬─────────┘
         │                     │
         ▼                     ▼
   ┌──────────────┐     ┌──────────────┐
   │ Notifications│     │ Audit        │
   │ Consumer     │     │ Consumer     │
   │ (Monolith)   │     │ (Monolith)   │
   └──────────────┘     └──────────────┘
```

### ✅ Características de Comunicación

**Exchange Configuration:**
```yaml
Exchange: banking.events
Type: Topic (permite patrones de routing)
Durable: true (sobrevive reinicio de broker)
AutoDelete: false
```

**Queue Configuration:**
```yaml
notifications.transfer-executed:
  Durable: true
  Exclusive: false
  AutoDelete: false
  Binding: transfer.executed.v* (wildcard para versions)

audit.transfer-executed:
  Durable: true
  Exclusive: false
  AutoDelete: false
  Binding: transfer.executed.v*
```

**Message Properties:**
```csharp
IBasicProperties props = _channel.CreateBasicProperties();
props.Persistent = true;           // Durabilidad
props.ContentType = "application/json";
props.DeliveryMode = 2;            // Persistent
props.Headers = new Dictionary<string, object>
{
    { "traceparent", traceId }     // Tracing
};
```

### 🔐 Garantía de Entrega (Outbox Pattern)

**¿Por qué Outbox?**

Sin Outbox:
```
1. Guarda Transfer en BD ✅
2. Intenta publicar a RabbitMQ ❌ (se cae)
3. Resultado: Transfer guardado pero evento nunca llegó
```

Con Outbox:
```
1. Guarda Transfer + OutboxEntry en MISMA transacción ✅
2. OutboxWorker publica desde BD cada 5s ✅
3. Si falla, reintenta automáticamente ✅
4. Resultado: Garantía de entrega (at-least-once)
```

**Implementación:**
```csharp
// Atomicidad garantizada por transacción
using var transaction = await _dbContext.Database.BeginTransactionAsync();

try
{
    // 1. Guarda Transfer
    _dbContext.Transfers.Add(transfer);
    
    // 2. Guarda OutboxEntry (MISMA transacción)
    var outboxEntry = new OutboxEntry
    {
        Id = Guid.NewGuid(),
        AggregateId = transfer.Id,
        EventType = "TransferExecuted",
        Payload = JsonSerializer.Serialize(@event),
        RoutingKey = "transfer.executed.v1",
        CreatedAt = DateTime.UtcNow,
        PublishedAt = null  // Pendiente de publicación
    };
    _dbContext.OutboxEntries.Add(outboxEntry);
    
    // 3. Commit atómico
    await _dbContext.SaveChangesAsync();
    await transaction.CommitAsync();
}
catch
{
    await transaction.RollbackAsync();
    throw;
}
```

### ✅ Consumers Implementados

**NotificationsConsumer (Monolith):**
```csharp
public class NotificationsConsumer : BackgroundService
{
    // Escucha: notifications.transfer-executed
    // Procesa: Crea notificación para usuario
    // Acción: INotificationsService.SendAsync()
    // Ack: Manual (solo tras éxito)
}
```

**AuditConsumer (Monolith):**
```csharp
public class AuditConsumer : BackgroundService
{
    // Escucha: audit.transfer-executed
    // Procesa: Registra entrada en auditoría
    // Acción: IAuditService.RecordAsync()
    // Ack: Manual (solo tras éxito)
}
```

**Status:** ✅ **Comunicación asincrónica completamente funcional**

---

## 4. Diagrama de Arquitectura Final

### 📊 Arquitectura Phase 2 - Vista General

```
┌─────────────────────────────────────────────────────────────────┐
│                         Cliente                                 │
└──────────────────────────┬──────────────────────────────────────┘
                           │ HTTP :5000
                           ▼
        ┌──────────────────────────────────┐
        │    YARP API Gateway              │
        │    ┌────────────────────────┐    │
        │    │ Router                 │    │
        │    │ /accounts/** → MS1     │    │
        │    │ /transfers/** → MS2    │    │
        │    │ /** (catch) → Monolith │    │
        │    └────────────────────────┘    │
        └──────┬──────────┬──────────┬─────┘
               │ :8080    │ :8080    │ :8080
               │          │          │
    ┌──────────▼─┐  ┌─────▼──────┐  ▼──────────────┐
    │ Accounts   │  │ Transfers  │  │ Monolith     │
    │ MS1        │  │ MS2        │  │ (residual)   │
    │            │  │            │  │              │
    │ Hexagonal  │  │ Hexagonal  │  │ Auth Mod.    │
    │ ┌────────┐ │  │ ┌────────┐ │  │ Notif. Cons. │
    │ │Domain  │ │  │ │Domain  │ │  │ Audit Cons.  │
    │ │Account │ │  │ │Transfer│ │  │              │
    │ │Money   │ │  │ │Events  │ │  └──────────────┘
    │ └────────┘ │  │ └────────┘ │         ▲ ▲
    │            │  │            │         │ │
    │ ┌────────┐ │  │ ┌────────┐ │         │ │
    │ │UseCase │ │  │ │UseCase │ │         │ │
    │ │Ports   │ │  │ │Ports   │ │    RabbitMQ
    │ └────────┘ │  │ └────────┘ │  Events (async)
    │            │  │            │         │ │
    │ ┌────────┐ │  │ ┌────────┐ │         │ │
    │ │Repo    │ │  │ │Repo    │ │  ┌──────┘ │
    │ │DbCtx   │ │  │ │DbCtx   │ │  │  ┌────┘
    │ │        │ │  │ │Outbox  │ │  │  │
    │ └────────┘ │  │ │Worker  │ │  │  │
    └──────┬─────┘  └─┬──────┬─┘ │  │  │
           │ JDBC     │      │   │  │  │
           │          │JDBC  │RabbitMQ │
           │          │      │         │
           ▼          ▼      ▼         ▼
      ┌────────┐  ┌────────┐     ┌──────────┐
      │Postgres│  │Postgres│     │RabbitMQ │
      │accounts│  │transfer│     │Exchange │
      │:5434   │  │:5435   │     │:5672    │
      └────────┘  └────────┘     └──────────┘
```

### 🔀 Flujo de Transferencia Completo

```
1. Cliente solicita transferencia
   POST http://localhost:5000/transfers
   │
   ▼
2. Gateway enruta a Transfers MS
   → http://transfers-service:8080/transfers
   │
   ▼
3. Transfers MS (HTTP Síncrono)
   ├─ Valida JWT
   ├─ Llama Accounts MS: GET /accounts/{sourceId}
   │  ├─ Verifica existencia ✅
   │  └─ Verifica ownership ✅
   ├─ Llama Accounts MS: GET /accounts/{targetId}
   │  └─ Verifica existencia ✅
   │
   ▼
4. Transfers MS Inicia Saga Choreography
   ├─ Crea Transfer (en BD local)
   ├─ Crea OutboxEntry (en BD local)
   └─ Ambos en MISMA transacción ✅
   │
   ▼
5. OutboxWorker (Asincrónico)
   ├─ Cada 5s: Lee OutboxEntries (published_at = NULL)
   ├─ Publica a RabbitMQ: exchange=banking.events
   ├─ Routing key: transfer.executed.v1
   └─ Marca published_at = NOW()
   │
   ▼
6. RabbitMQ Enruta a Consumers
   ├─ Cola: notifications.transfer-executed
   │  └─ Consumer en Monolith procesa
   │     └─ Crea Notification (eventual)
   │
   └─ Cola: audit.transfer-executed
      └─ Consumer en Monolith procesa
         └─ Crea AuditEntry (eventual)
   │
   ▼
7. Cliente recibe respuesta
   HTTP 201 Created
   {
     "id": "transfer-123",
     "status": "recorded"
   }

8. (Simultáneamente) Monolith procesa eventos
   ├─ NotificationsConsumer: envía notificación
   └─ AuditConsumer: registra operación
```

**Status:** ✅ **Diagrama de arquitectura completo y funcional**

---

## 5. Patrón de Consistencia Distribuida

### 📊 Saga Choreography (Event-Driven)

**¿Qué es?**

Patrón para coordinar transacciones distribuidas usando eventos, sin orquestador central.

**Ventajas:**
- ✅ Desacoplamiento entre servicios
- ✅ Sin punto único de fallo (SPOF)
- ✅ Escalable horizontalmente
- ✅ Fácil de agregar nuevos servicios

**Desventajas:**
- ❌ Lógica distribuida (compleja de entender)
- ❌ Difícil de debuggear

### 🔄 Modelo de Consistencia: Eventual Consistency

```
┌─────────────────────────────────────────────────────┐
│            STRONG CONSISTENCY                       │
│  (Transfer se graba inmediatamente)                 │
└────────────┬────────────────────────────────────────┘
             │
             ▼
    Transfer guardado en BD
    OutboxEntry guardado
    Respuesta 201 al cliente ✅
             │
             ├─ (Milisegundos después)
             │
             ▼
    ┌─────────────────────────────────────────────────┐
    │         EVENTUAL CONSISTENCY                    │
    │  (Notifications y Audit se crean después)       │
    └─────────────────────────────────────────────────┘
             │
             ├─ OutboxWorker publica a RabbitMQ
             │
             ├─ NotificationsConsumer procesa
             │  └─ Crea notificación (1-5 segundos)
             │
             └─ AuditConsumer procesa
                └─ Crea audit entry (1-5 segundos)
```

### 📋 Fases del Saga

#### Fase 1: Request (Síncrono)
```
Cliente → Gateway → Transfers MS → Accounts MS (HTTP)
  ├─ Verifica origen existe
  ├─ Verifica destino existe
  ├─ Verifica ownership
  └─ Si todo OK: continúa

Timeout: 30 segundos (Polly)
```

#### Fase 2: Crear Transfer (Local)
```
Transfers MS
  ├─ Crea Transfer aggregate
  ├─ Crea OutboxEntry
  └─ Guarda TODO en transacción atómica

BD: postgres-transfers
```

#### Fase 3: Publicar Evento (Async)
```
OutboxWorker (Background Service)
  ├─ Cada 5 segundos:
  │  ├─ Lee OutboxEntries no publicadas
  │  ├─ Publica a RabbitMQ
  │  └─ Marca publicadas
  │
  └─ Si falla RabbitMQ: reintenta
     (Outbox garantiza entrega)
```

#### Fase 4: Procesar Eventos (Async)
```
RabbitMQ Consumers (Monolith)
  │
  ├─ NotificationsConsumer
  │  └─ Recibe evento
  │     └─ Crea notificación (may fail)
  │
  └─ AuditConsumer
     └─ Recibe evento
        └─ Crea audit entry (may fail)

Si consumer falla: evento reintentado (RabbitMQ durability)
```

### 🎯 Garantías de Consistencia

| Garantía | Implementación | Status |
|----------|---|---|
| **At-Least-Once Delivery** | Outbox + Durabilidad RabbitMQ | ✅ |
| **Idempotencia** | Consumer maneja duplicados | ✅ |
| **Eventual Consistency** | Notifications/Audit eventual | ✅ |
| **Strong Consistency (Transfer)** | Transacción local | ✅ |

### 📊 Timeline de Consistencia

```
t=0ms   Cliente envía request
        │
t=50ms  Transfer grabado + OutboxEntry grabado (STRONG)
        Respuesta 201 al cliente
        │
t=55ms  OutboxWorker publica evento
        │
t=60ms  Notification y Audit aún no existen (EVENTUAL)
        │
t=1000ms Notification y Audit creadas
        Sistema consistente
```

### 🔐 Manejo de Fallos

#### Escenario 1: Accounts MS no responde
```
1. Cliente: POST /transfers
2. Gateway → Transfers MS
3. Transfers MS: GET /accounts/{id} → TIMEOUT
4. Polly: 3 retries + Circuit breaker
5. Si sigue fallando: ERROR 503
6. Cliente recibe error: NO se crea Transfer
7. Resultado: Transferencia NO realizada ✅
```

#### Escenario 2: RabbitMQ se cae
```
1. Cliente: POST /transfers
2. Transfer + OutboxEntry grabados ✅
3. Respuesta 201 al cliente ✅
4. OutboxWorker intenta publicar → FALLA
5. RabbitMQ se reinicia (health checks)
6. OutboxWorker reintenta automáticamente
7. Evento finalmente publicado ✅
8. Notifications/Audit creadas ✅
9. Resultado: Garantía de entrega (at-least-once) ✅
```

#### Escenario 3: NotificationsConsumer falla
```
1. Evento publicado a RabbitMQ ✅
2. NotificationsConsumer recibe
3. Error al procesar: exception
4. Consumer nackea mensaje
5. RabbitMQ reintenta
6. Si sigue fallando: mueve a DLQ (dead-letter queue)
7. Audit se crea normalmente (otro consumer)
8. Resultado: Degradación parcial (sin notificación) ✅
```

### 📈 Diagrama de Estados (Saga)

```
┌─────────────┐
│   PENDING   │ (Transfer creado, sin publicar)
└──────┬──────┘
       │ OutboxWorker publica
       ▼
┌──────────────────┐
│   PUBLISHED      │ (Evento en RabbitMQ)
└──────┬───────────┘
       │ Consumers procesan
       ▼
┌──────────────────┐
│   COMPLETED      │ (Notifications/Audit creadas)
└──────────────────┘

Transiciones:
PENDING → PUBLISHED: Garantizado por Outbox
PUBLISHED → COMPLETED: Eventual (puede fallar)
```

### ✅ Validación de Consistencia

**En tests:**
```bash
# Test 1: Transfer se graba inmediatamente (STRONG)
POST /transfers → 201 ✅
GET /transfers?accountId=X → Transfer visible ✅

# Test 2: Notification se crea eventualmente (EVENTUAL)
POST /transfers → 201 ✅
GET /notifications → (vacío inicialmente)
Esperar 1-5 segundos
GET /notifications → Notification presente ✅

# Test 3: Si RabbitMQ cae
docker-compose stop rabbitmq
POST /transfers → 201 ✅
docker-compose start rabbitmq
Esperar OutboxWorker
GET /notifications → Notification presente ✅
```

**Status:** ✅ **Patrón de consistencia implementado y validado**

---

## 📊 RESUMEN EJECUTIVO

### ✅ TODAS LAS EVIDENCIAS COMPLETADAS

| # | Evidencia | Estado | Ubicación |
|---|-----------|--------|-----------|
| 1 | Justificación del segundo módulo | ✅ | `docs/adr/ADR-002` |
| 2 | MS2 autónomo con BD propia | ✅ | `services/transfers-service/` |
| 3 | Comunicación asincrónica (RabbitMQ) | ✅ | OutboxWorker + Consumers |
| 4 | Diagrama arquitectura Phase 2 | ✅ | Este documento |
| 5 | Patrón de consistencia distribuida | ✅ | Saga Choreography (este doc) |

### 📈 Métricas Phase 2

- **Microservicios autónomos:** 2 (Accounts + Transfers)
- **Bases de datos independientes:** 2 (postgres-accounts, postgres-transfers)
- **Endpoints MS2:** 2 (POST /transfers, GET /transfers?accountId=)
- **Líneas de código:** ~1,200 (Transfers MS)
- **ADRs documentados:** 11 (todas justificadas)
- **Test scenarios:** 10 (PHASE-2-TESTING.md)
- **Patrón de consistencia:** Eventual (Strong local + Eventual distributed)
- **Broker de mensajes:** RabbitMQ (Topic exchange, durable queues)
- **Garantía de entrega:** At-least-once (Outbox pattern)

### 🔄 Flujo End-to-End Validado

```
Cliente → Gateway → Transfers MS → (Accounts MS via HTTP)
                         ↓
                   Guarda Transfer + OutboxEntry
                   (transacción atómica)
                         ↓
                   Respuesta 201 al cliente
                   (STRONG CONSISTENCY)
                         ↓
                   OutboxWorker publica evento
                         ↓
                   RabbitMQ enruta a consumers
                         ↓
                   NotificationsConsumer + AuditConsumer
                   crean registros
                   (EVENTUAL CONSISTENCY)
```

### 🚀 Estado para Production

**Phase 2 está listo para:**
- ✅ Desplegar en Docker Compose
- ✅ Escalar Transfers MS independientemente
- ✅ Manejar fallos de Accounts MS (circuit breaker)
- ✅ Garantizar entrega de eventos (Outbox pattern)
- ✅ Procesar eventos asincrónicamente
- ✅ Continuar con Phase 3 (Resilience patterns - ya implementado)

---

**Certificación:** Phase 2 completada con todas las evidencias requeridas ✅

**Siguiente:** Phase 3 - Resilience Patterns (Polly) - Ya implementado
**Siguiente:** Phase 4 - Observability Stack - Ya implementado
**Siguiente:** Phase 5 - E2E Testing - Ya implementado

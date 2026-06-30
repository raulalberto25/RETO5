# Diagrama de Arquitectura - Phase 1

## Visión General (Antes vs Después)

### ANTES: Monolito Monolítico
```
┌─────────────────────────────┐
│      Cliente                │
└──────────────┬──────────────┘
               │
               ▼
┌─────────────────────────────┐
│    ModularBank Monolith     │
│  ┌─────────────────────┐    │
│  │ Auth Module         │    │
│  │ Accounts Module     │    │
│  │ Transfers Module    │    │
│  │ Notifications Mod.  │    │
│  │ Audit Module        │    │
│  └─────────────────────┘    │
│          ↓                  │
│  ┌─────────────────────┐    │
│  │  PostgreSQL (1)     │    │
│  │  (schemas: all)     │    │
│  └─────────────────────┘    │
└─────────────────────────────┘

Problemas:
❌ Acoplamiento fuerte entre módulos
❌ Una BD para todo
❌ Escala monolítica
❌ Deployments entrelazados
```

---

### DESPUÉS: Strangler Fig Pattern (Phase 1)
```
┌──────────────────────────────────────────────────────────────┐
│                        Cliente                               │
└───────────────────┬──────────────────────────────────────────┘
                    │
                    ▼
         ┌──────────────────────┐
         │  YARP API Gateway    │
         │  (puerto 5000)       │
         │  ┌────────────────┐  │
         │  │ Router         │  │
         │  │ /accounts/** ──┼──┼──────────────┐
         │  │ /transfers/**─┐│  │              │
         │  │ /auth/**  ────┼┼┬─┼──┐           │
         │  │ /** (catch)   ││││  │           │
         │  └────────────────┘││  │           │
         └──────────────────────┼──┼───────────┼──────┐
                                │  │           │      │
                                │  │           │      │
                    ┌───────────┘  │           │      │
                    │              │           │      │
                    ▼              ▼           ▼      ▼
         ┌──────────────────┐  ┌─────────────────┐  ┌──────────────┐
         │ Accounts MS (MS1)│  │ Monolith        │  │   (Future)   │
         │ (puerto 5001)    │  │ (puerto 5010)   │  │   Transfers  │
         │                  │  │                 │  │   MS2        │
         │ ┌──────────────┐ │  │ ┌─────────────┐│  │              │
         │ │ Domain       │ │  │ │ Auth Mod.   ││  │ (En Phase 2) │
         │ │ Account      │ │  │ │ Transfer M. ││  │              │
         │ │ Money        │ │  │ │ Notif. Mod. ││  │              │
         │ └──────────────┘ │  │ │ Audit Mod.  ││  │              │
         │                  │  │ └─────────────┘│  │              │
         │ ┌──────────────┐ │  │                 │  │              │
         │ │ Application  │ │  │ (Residual)      │  │              │
         │ │ UseCase      │ │  │                 │  │              │
         │ │ Ports        │ │  └─────────────────┘  └──────────────┘
         │ └──────────────┘ │          ▲
         │                  │          │
         │ ┌──────────────┐ │          │
         │ │ Infrastructure
         │ │ Repository   │ │          │
         │ │ DbContext    │ │          │
         │ └──────────────┘ │          │
         └──────────────────┘          │
                    │                  │
                    ▼                  ▼
        ┌──────────────────┐ ┌─────────────────┐
        │postgres-accounts │ │postgres-monolith│
        │(BD: finbank_     │ │(BD: modular_    │
        │ accounts)        │ │ bank_dotnet)    │
        │                  │ │                 │
        │Schema:           │ │Schemas:         │
        │ accounts.*       │ │ auth.*          │
        │ (Exclusive)      │ │ transfers.*     │
        │                  │ │ notifications.* │
        │                  │ │ audit.*         │
        └──────────────────┘ └─────────────────┘
```

---

## Flujo de Tráfico - Phase 1

### Solicitud: `POST /accounts` (nuevo endpoint)
```
1. Cliente
   └─ POST http://localhost:5000/accounts

2. Gateway (YARP)
   └─ Ruta coincide: /accounts/** → Accounts MS
   └─ Propaga headers (Authorization, Content-Type, etc.)

3. Accounts MS
   ├─ Valida JWT (Bearer token)
   ├─ Llama a AccountsUseCase
   ├─ Persiste en postgres-accounts
   └─ Responde al Gateway

4. Gateway
   └─ Propaga respuesta al cliente

5. Cliente
   └─ Recibe respuesta (201 Created)
```

### Solicitud: `POST /transfers` (aún en monolito)
```
1. Cliente
   └─ POST http://localhost:5000/transfers

2. Gateway (YARP)
   └─ Ruta coincide: /transfers/** → monolith (catch-all)

3. Monolith
   ├─ Valida JWT
   ├─ Necesita verificar cuenta (pero Accounts está en MS1)
   ├─ Llama HttpAccountsService
   │  └─ GET http://accounts-service:8080/accounts/{id}
   │
   ├─ HttpAccountsService mapea respuesta
   ├─ Ejecuta transfer localmente
   └─ Responde

4. Gateway
   └─ Propaga respuesta
```

---

## Componentes Phase 1

### 1. YARP Gateway
```yaml
Ubicación: gateway/
Propósito: Punto único de entrada
Configuración: appsettings.json
  Routes:
    - /accounts/** → accounts-service:8080
    - /transfers/** → monolith:8080 (aún)
    - /auth/** → monolith:8080
    - /** (catch-all) → monolith:8080
Puertos:
  - 5000 (entrada externa)
```

### 2. Accounts Microservice (MS1)
```yaml
Ubicación: services/accounts-service/
Arquitectura: Hexagonal
  - Domain/: Account, Money (value object)
  - Application/: AccountsUseCase, Ports
  - Infrastructure/: Repository, DbContext
Puertos:
  - 8080 (interno, via Gateway)
Base de datos:
  - postgres-accounts (puerto 5434)
  - Database: finbank_accounts
  - Schema: accounts.*
```

### 3. Monolith (Residual - Phase 1)
```yaml
Ubicación: src/ModularBank/
Módulos restantes:
  - Auth (sin cambios)
  - Transfers (usa HttpAccountsService)
  - Notifications (sin cambios)
  - Audit (sin cambios)
Puertos:
  - 8080 (interno, via Gateway)
Base de datos:
  - postgres-monolith (puerto 5433)
  - Database: modular_bank_dotnet
  - Schemas: auth.*, transfers.*, notifications.*, audit.*
Adaptación:
  - HttpAccountsService: llama a Accounts MS via HTTP
  - Feature flag: Features:UseAccountsMS=true (dev), false (fallback)
```

---

## Estrategia de Migración de Datos - Phase 1

### Fase 1: Dual-Write (Estado Actual)
```
Monolith
  ├─ Escribe en postgres-monolith/accounts.*
  └─ Escribe en postgres-accounts (vía HttpAccountsService)

Resultado:
  ✓ Ambas BDs en sincronía
  ✗ Mayor latencia (2 escrituras)
  ✓ Reversible (puede volver a monolito)
```

### Fase 2: Backfill (Futuro)
```
1. Copiar datos existentes:
   accounts.* → postgres-accounts
   
2. Verificar integridad:
   SELECT COUNT(*) FROM accounts (ambas BDs)
   
3. Validar transferencias históricas
```

### Fase 3: Read Switchover (Futuro)
```
Monolith
  ├─ Deja de leer de postgres-monolith/accounts.*
  └─ Lee de Accounts MS (vía HttpAccountsService)

Resultado:
  ✓ Accounts MS es la única fuente de verdad
  ✓ postgres-monolith/accounts.* puede deleterse
```

### Fase 4: Stop Dual-Write (Futuro)
```
Monolith
  └─ Deja de escribir en postgres-accounts
  
Resultado:
  ✓ Accounts MS es autónomo
  ✓ Monolith ya no toca datos de cuentas
```

---

## Comunicación Inter-Servicios - Phase 1

### Sincrónica (HTTP)
```
Monolith → Accounts MS
  └─ HttpAccountsService
     ├─ FindByOwnerAsync()
     ├─ GetBalanceAsync()
     ├─ DebitAsync()
     └─ CreditAsync()

Latencia: ~100-200ms
Protocolo: HTTP + JWT Bearer Token
Error Handling:
  ├─ 404 → KeyNotFoundException
  ├─ 422 → InvalidOperationException
  └─ Network errors → InvalidOperationException
```

### Asincrónica (Futuro - Phase 2)
```
(No implementado en Phase 1)
RabbitMQ será agregado en Phase 2
```

---

## Seguridad - Phase 1

### Autenticación
```
JWT (JSON Web Tokens)
  ├─ Secreto compartido entre servicios
  ├─ Bearer token en headers Authorization
  └─ Validación en cada servicio
```

### Red
```
Docker Network (finbank-network)
  ├─ Comunicación intra-servicio segura
  ├─ Aislamiento del exterior
  └─ Gateway es único punto de entrada público
```

---

## Deployabilidad - Phase 1

### Docker Compose
```yaml
Servicios:
  1. postgres-monolith (puerto 5433)
  2. postgres-accounts (puerto 5434)
  3. accounts-service (puerto 5001)
  4. gateway (puerto 5000)
  5. monolith (puerto 5010)

Orquestación:
  ├─ Health checks en cada servicio
  ├─ Depends_on: garantiza orden de startup
  └─ Volumes: persistencia de datos

Comando:
  docker-compose up -d
```

---

## Ventajas de Phase 1

✅ **Accounts MS Autónomo**
- BD exclusiva
- Escalable independientemente
- Puede deployer sin afectar monolito

✅ **Gateway Transparente**
- Clientes no notan cambio
- Enrutamiento inteligente
- Headers propagados correctamente

✅ **Monolito sin Cambios de Negocio**
- Transfers sigue funcionando
- HttpAccountsService maneja detalles HTTP
- Feature flag permite rollback

✅ **Zero Downtime Possible**
- Dual-write permite migración gradual
- Reversible en cualquier momento
- Sin interrupción de servicio

✅ **Observabilidad**
- OpenTelemetry listo en ambos servicios
- Trazas vinculadas por TraceId
- Logs estructurados (JSON)

---

## Métricas Phase 1

| Métrica | Valor |
|---------|-------|
| Servicios Autónomos | 1 (Accounts MS) |
| Bases de Datos | 2 (monolith, accounts) |
| Puntos de entrada | 1 (Gateway) |
| Líneas de código | ~1,100 (Accounts + Gateway) |
| ADRs | 11 (todas documentadas) |
| Test scenarios | 10 (PHASE-1-TESTING.md) |

---

## Próximos Pasos (Phase 2)

- Extraer Transfers como MS2
- Agregar RabbitMQ para comunicación asincrónica
- Implementar Saga Choreography
- Agregar Outbox Pattern para garantizar entrega

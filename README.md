# RETO5 - FinBank Microservices Migration

## Descripción
Proyecto de modernización arquitectónica: Migración de monolito modular a arquitectura de microservicios.

### Estado
- ✅ Phase 1: Extracción Accounts MS
- ✅ Phase 2: Extracción Transfers MS + RabbitMQ
- ✅ Phase 3: Resilience Patterns (Polly)
- ✅ Phase 4: Observability Stack (Jaeger, Prometheus, Loki, Grafana)
- ✅ Phase 5: E2E Testing

## Estructura del Proyecto

```
RETO5/
├── services/
│   ├── accounts-service/      # Microservicio de Cuentas (MS1)
│   └── transfers-service/     # Microservicio de Transferencias (MS2)
├── src/
│   └── ModularBank/           # Monolito residual
├── gateway/                   # YARP API Gateway
├── docs/                      # Documentación y ADRs
├── observability/             # Configuración de observabilidad
├── EVIDENCIAS/                # Evidencias de proyecto
├── docker-compose.yml         # Orquestación de servicios
└── .gitignore                 # Exclusiones de Git
```

## Quick Start

```bash
# Clonar repositorio
git clone https://github.com/raulalberto25/RETO5.git
cd RETO5

# Iniciar stack completo
docker-compose up -d

# Acceder a servicios
# Gateway: http://localhost:5000
# Jaeger: http://localhost:16686
# Prometheus: http://localhost:9090
# Grafana: http://localhost:3000 (admin/admin)
```

## Documentación

- `docs/adr/` - Architectural Decision Records (11 ADRs)
- `docs/PHASE-*-TESTING.md` - Guías de testing
- `docs/PROJECT-STATUS.md` - Estado del proyecto
- `EVIDENCIAS/` - Evidencias de cada fase

## Tecnologías

- **.NET 10** - Microservicios
- **PostgreSQL** - Base de datos (Database-per-Service)
- **RabbitMQ** - Message Broker
- **YARP** - API Gateway
- **Docker** - Containerización
- **OpenTelemetry** - Observabilidad
- **Polly** - Resilience
- **EF Core** - ORM

## Contacto

raulalbertoalvaradoramos@gmail.com


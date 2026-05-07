# Base tecnica inicial

Fecha: 2026-04-23

## Que se genero

Solucion:

- `SifenInvoicing.sln`

Proyectos:

- `src/SifenInvoicing.Domain`
- `src/SifenInvoicing.Application`
- `src/SifenInvoicing.Infrastructure`
- `src/SifenInvoicing.Api`
- `tests/SifenInvoicing.Tests`

## Responsabilidad por capa

### Domain

Reglas puras del negocio. No depende de base de datos, API, SIFEN, certificados ni frameworks externos.

Incluye por ahora:

- Base `Entity`.
- `DomainException`.

### Application

Contratos y casos de uso. Aun no contiene casos SIFEN, pero ya define la auditoria transversal.

Incluye:

- `IAuditTrail`.
- `AuditEvent`.
- `AuditCategory`.
- `AuditSeverity`.
- `ISystemClock`.

### Infrastructure

Implementaciones tecnicas reemplazables.

Incluye:

- `LoggerAuditTrail`, que envia eventos de auditoria a logs estructurados.
- `SystemClock`.
- Registro DI con `AddInfrastructure()`.

### API

Entrada HTTP del sistema.

Incluye:

- Serilog.
- `CorrelationIdMiddleware`.
- `RequestAuditMiddleware`.
- `/ops/status`.
- `/health/live`.
- `/health/ready`.

## Auditoria inicial

Cada request HTTP registra:

- `CorrelationId`, desde `X-Correlation-Id` o generado automaticamente.
- `TenantId`, desde `X-Tenant-Id`.
- `ClientId`, desde `X-Client-Id`.
- Metodo HTTP.
- Ruta.
- Estado HTTP.
- Duracion.
- IP remota.
- Resultado.

Esto prepara el sistema para operar con multiples clientes y diagnosticar reclamos como:

- "A varios clientes les falla al mismo tiempo": posible SIFEN/API/dependencia global.
- "Solo falla un cliente": posible certificado, timbrado, RUC, datos o configuracion del tenant.
- "Falla solo una operacion": posible XML, CDC, evento, lote o regla de negocio.

## Endpoints

### Estado operativo

`GET /ops/status`

Devuelve:

- Servicio.
- Ambiente ASP.NET.
- Ambiente SIFEN activo.
- Si hay base URL SIFEN configurada.
- Headers usados para auditoria.

### Vida del proceso

`GET /health/live`

Indica si el proceso HTTP responde.

### Preparacion

`GET /health/ready`

Por ahora valida que existan URLs SIFEN test y produccion en configuracion. Todavia no llama a SIFEN ni valida certificado; eso se agregara cuando implementemos mTLS/SOAP.

## Como probar

Compilar:

```bash
dotnet build SifenInvoicing.sln
```

Ejecutar tests:

```bash
dotnet test SifenInvoicing.sln --no-build
```

Levantar API local:

```bash
dotnet run --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj --urls http://localhost:5088
```

Probar:

```bash
curl http://localhost:5088/ops/status
curl http://localhost:5088/health/live
curl http://localhost:5088/health/ready
```

Logs:

```text
src/SifenInvoicing.Api/logs/sifen-invoicing-YYYYMMDD.log
```

## Resultado verificado

- `dotnet build SifenInvoicing.sln`: correcto, 0 errores, 0 advertencias.
- `dotnet test SifenInvoicing.sln --no-build`: correcto, 1 test superado.
- `/ops/status`: HTTP 200.
- `/health/live`: Healthy.
- `/health/ready`: Healthy.


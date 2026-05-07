# Operacion multi-cliente y diagnostico inicial

Fecha: 2026-04-23

## Objetivo

Preparar el sistema para operar como producto profesional, incluso cuando haya muchos clientes emitiendo documentos al mismo tiempo. Este incremento no implementa todavia reglas SIFEN; crea la base para diagnosticar incidentes.

## Que se agrego

### Contexto de tenant

Headers soportados:

- `X-Tenant-Id`
- `X-Client-Id`
- `X-Taxpayer-Ruc`

Clases:

- `TenantContext`
- `ITenantContextAccessor`
- `AsyncLocalTenantContextAccessor`
- `TenantResolutionMiddleware`

Uso previsto:

- Cada request queda asociado a un tenant/cliente si se proveen headers.
- Luego JWT/API keys resolveran estos datos sin confiar en headers publicos.
- El RUC se trata como dato operativo, no como regla validada todavia.

### Diagnostico de dependencias

Endpoint:

```text
GET /ops/dependencies
```

Devuelve un snapshot por dependencia:

- API interna.
- Configuracion de endpoints SIFEN.
- Configuracion de certificado SIFEN.
- SQL Server.
- Background workers.

Estados posibles:

- `Healthy`
- `Degraded`
- `Unavailable`
- `NotConfigured`
- `Unknown`

## Como ayuda ante reclamos

Caso: "30 clientes reportan que no pueden emitir".

Primera lectura:

- Si `internal-api` esta healthy, el proceso API responde.
- Si `sifen-endpoints` esta healthy pero luego el futuro check mTLS falla, posible SIFEN/red/certificado.
- Si `sql-server` falla, el problema es persistencia.
- Si `background-workers` falla, el problema puede estar en lotes/reintentos/polling.

Caso: "solo un cliente falla".

Primera lectura:

- Revisar auditoria por `TenantId`.
- Revisar certificado por tenant.
- Revisar timbrado/CSC por tenant.
- Revisar rechazos SIFEN asociados al CDC/RUC.

## Importante

Por ahora el sistema no valida RUC ni certificado. Solo prepara trazabilidad y diagnostico. La validacion real debe entrar cuando implementemos:

- Persistencia de tenants.
- Almacenamiento seguro de certificados.
- Integracion mTLS/SOAP.
- Validaciones SIFEN por XML/XSD.

## Como probar

Levantar API:

```bash
dotnet run --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj --urls http://localhost:5088
```

Consultar estado normal:

```bash
curl http://localhost:5088/ops/status
curl http://localhost:5088/ops/dependencies
```

Consultar simulando cliente:

```bash
curl -H "X-Tenant-Id: tenant-001" -H "X-Client-Id: client-001" -H "X-Taxpayer-Ruc: 80000000-0" http://localhost:5088/ops/status
curl -H "X-Tenant-Id: tenant-001" -H "X-Client-Id: client-001" http://localhost:5088/ops/dependencies
```


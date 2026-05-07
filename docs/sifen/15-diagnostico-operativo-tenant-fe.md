<!-- scope: sifen-fe | relevant for: readiness, tenant, certificado, csc, diagnostico -->

# Diagnostico operativo FE por tenant

## Purpose

Exponer un diagnostico minimo para saber si un tenant esta listo para emitir FE.

## Data model

- `TenantFeOperationalDiagnostic`
- `TenantFeOperationalCheck`
- `TenantFeLastSubmission`
- `TenantFeLastIssue`

## API / endpoints

- `GET /ops/fe-diagnostic`
- `GET /api/fe/diagnostic/{tenantId}`

## Business rules

- Requiere `TenantId` resuelto en el request.
- El endpoint `GET /api/fe/diagnostic/{tenantId}` permite consultar readiness FE por empresa sin depender del tenant resuelto en headers.
- Evalua:
  - tenant activo
  - ambiente SIFEN configurado
  - certificado XML cargado
  - certificado XML vigente
  - CSC configurado
  - establecimiento operativo
  - punto de expedicion operativo
  - numeracion operativa
- Expone ultimo envio y ultimo error a partir de documentos FE persistidos.

## Constraints

- El sistema actual no persiste establecimiento, punto y numeracion como configuracion propia del tenant.
- Mientras eso no exista, esos tres checks se derivan de la ultima FE persistida.
- Si el tenant no tiene FE previa, el diagnostico marca esos checks como no listos con mensaje `TODO` explicito.
- La respuesta publica minima del endpoint nuevo es:
  - `ready`
  - `missing[]`

## Edge cases

- tenant sin contexto resuelto.
- tenant inexistente o inactivo.
- certificado faltante.
- certificado vencido.
- tenant sin FE previa.
- tenant sin errores o sin envios previos.

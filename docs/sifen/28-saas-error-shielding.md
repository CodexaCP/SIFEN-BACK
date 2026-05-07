<!-- scope: sifen-fe | relevant for: errors, retry, diagnostics, plan-limit, tenant-status, api-payload -->

# SaaS Error Shielding

## Purpose

Definir el blindaje funcional de errores para emision FE, con mensajes claros, persistencia de incidentes por documento y una respuesta API uniforme para frontend SaaS.

## Data model

- `SifenDocumentError`
  - `TenantId`
  - `InvoiceId`
  - `Cdc`
  - `ErrorCode`
  - `ErrorCategory`
  - `TechnicalMessage`
  - `UserMessage`
  - `SuggestedAction`
  - `IsRetryable`
  - `CorrelationId`
  - `RawResponse`
  - `CreatedAt`
- `SifenDocumentStatus`
  - `Draft`
  - `DraftGenerated`
  - `ReadyForSifenTest`
  - `Signed`
  - `PendingSubmission`
  - `Submitted`
  - `Approved`
  - `Accepted`
  - `Rejected`
  - `RetryableError`
  - `Failed`
  - `InternalValidation`
  - `BlockedByConfiguration`
  - `InternalValidationFailed`
  - `DraftValidatedWithoutSignature`
  - `BlockedByPlan`
  - `SystemError`

## API / endpoints

- `POST /api/fe/invoices`
- `GET /api/fe/invoices/status/{cdc}`
- `GET /invoice/`
- `GET /invoice/{id}`
- `POST /invoice/{id}/retry`
- `GET /api/fe/diagnostic/{tenantId}`

## Business rules

- La API no debe devolver errores tecnicos crudos al usuario final.
- Toda excepcion funcional relevante debe salir con este formato:
  - `errorCode`
  - `category`
  - `userMessage`
  - `suggestedAction`
  - `isRetryable`
  - `correlationId`
- `Tenant inactive`, `plan monthly limit`, `certificate not ready` y `SIFEN configuration incomplete` salen como errores guiados.
- Cuando una factura termina en rechazo, error de transporte o validacion degradada, se registra un `SifenDocumentError`.
- El listado y el detalle FE deben mostrar `Motivo`, `Accion sugerida` y `CorrelationId` cuando existan.
- Solo se habilita `Reintentar` para errores transitorios marcados como retryables.
- `DraftValidatedWithoutSignature` conserva el mensaje:
  - `XML generated locally. Certificate/XSD/CSC are missing for full validation.`

## Constraints

- No exponer `technicalMessage` ni `rawResponse` en la UI.
- No romper endpoints FE existentes ni KuDE.
- Mantener aislamiento por tenant al consultar errores.
- No cambiar el flujo real de aprobacion SIFEN.

## Edge cases

- tenant inactivo
- limite mensual alcanzado
- certificado no listo
- diagnostico incompleto
- timeout SOAP
- rechazo SIFEN
- validacion local degradada en `Development + Diagnostic`

<!-- scope: sifen-fe | relevant for: test-internal, traceability, events, logs, diagnostic, prepare-test -->

# Base TEST de trazabilidad FE

## Purpose

Definir la capa minima de trazabilidad y diagnostico interno para FE `TipoDoc 01` antes de usar certificados reales, CSC real o envio SOAP real.

## Data model

- `Documents`
  - `CorrelationId`
  - `InternalStatus`
  - `RetryCount`
  - `IsRetryable`
  - `LastErrorCode`
  - `LastErrorMessage`
- `FeInvoiceEvents`
  - historial de cambios de estado interno por factura
- `FeTenantLogs`
  - bitacora tecnica por tenant y opcionalmente por factura

## API / endpoints

- `GET /api/fe/tenants/{tenantId}/diagnostic`
- `GET /api/fe/invoices/{invoiceId}/events`
- `GET /api/fe/tenants/{tenantId}/logs`
- `POST /api/fe/invoices/{invoiceId}/prepare-test`

## Business rules

- Esta fase trabaja en `TEST_INTERNAL`.
- `prepare-test` no firma XML real.
- `prepare-test` no envia SOAP.
- `prepare-test` no llama a SIFEN.
- El `InternalStatus` permitido en esta fase es:
  - `DRAFT`
  - `GENERATED`
  - `VALIDATED_TEST`
  - `TEST_ERROR`
  - `READY_FOR_REAL`
  - `BLOCKED_BY_CONFIG`
- El endpoint de diagnostico no devuelve secretos.
- `CSC`, password y certificado solo se informan como presencia o ausencia.
- El tenant debe estar activo y dentro del plan para pasar a `VALIDATED_TEST`.
- Si falta configuracion critica, la factura pasa a `BLOCKED_BY_CONFIG`.

## Constraints

- No altera el flujo real de `Status` FE existente.
- No reemplaza el diagnostico operativo pre-homologacion ya existente.
- No cambia endpoints FE actuales; agrega superficie nueva y mantiene compatibilidad.
- No se expone `TechnicalDetail` sensible al usuario final.

## Edge cases

- tenant inexistente
- tenant suspendido
- limite mensual alcanzado
- factura de otro tenant
- factura sin `XmlPayload`
- tenant con RUC faltante
- tenant sin establecimiento, punto o numeracion
- tenant sin CSC o metadata de certificado: queda `PARTIAL`, no necesariamente bloqueado para TEST interno

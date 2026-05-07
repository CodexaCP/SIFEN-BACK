<!-- scope: sifen-fe | relevant for: retry, reenvio, error-tecnico, auditoria -->

# Retry operativo FE minimo

## Purpose

Permitir reintento controlado de FE TipoDoc 01 solo para fallas tecnicas o transitorias.

## Data model

- `InvoiceRetryResult` devuelve el resultado del reintento.
- `SifenDocumentLog` registra:
  - `sifen.retry.attempted`
  - `sifen.retry.result`
- `IAuditTrail` registra el evento `invoice.retry`.

## API / endpoints

- `POST /invoice/{id}/retry`

## Business rules

- No permite retry para `Accepted`.
- No permite retry normal para `Rejected`.
- Solo permite retry cuando el estado actual es `Failed` tecnico y existe `SignedXmlPayload`.
- El retry reutiliza el XML ya firmado y el mismo CDC.
- Antes del envio, el documento pasa a `PendingSubmission` para reducir doble envio accidental.
- Cada intento registra usuario operativo desde `TenantContext.ClientId`, fecha y resultado.

## Constraints

- No reprocesa rechazos definitivos.
- No regenera XML ni CDC.
- No toca otros tipos de documento FE.

## Edge cases

- documento inexistente.
- retry sin tenant activo.
- XML firmado inexistente.
- timeout o error de transporte durante el retry.
- actor operativo ausente: se registra `anonymous`.

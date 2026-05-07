<!-- scope: sifen-fe | relevant for: parser, rRetEnviDe, rProtDe, estados, soporte -->

# Parser de respuesta SIFEN

## Purpose

Centralizar el mapeo de `rRetEnviDe/rProtDe` a estados internos del modulo FE.

## Data model

- `ParsedSifenResponse` ahora incluye:
  - `Outcome`
  - `StatusHint`
  - `Cdc`
  - `TrackingId`
  - `StatusCode`
  - `StatusMessage`
  - `TechnicalMessage`

## API / endpoints

- Impacta indirectamente `POST /invoice/`.
- No agrega cambios de UI.

## Business rules

- `Approved` -> `Accepted`
- `Rejected` -> `Rejected`
- `Observed` -> `Submitted`
- `TechnicalError` -> `Failed`
- `EmptyResponse` -> `Failed`
- `InvalidXml` -> `Failed`
- `Unknown` -> `Submitted`

`StatusMessage` queda como mensaje limpio para usuario.

`TechnicalMessage` queda para soporte y logs.

## Constraints

- Se guarda `raw response` sin perder la respuesta original.
- Si la semantica exacta de un estado real no esta confirmada, se evita marcarlo como `Accepted`.

## Edge cases

- XML vacio.
- XML malformado.
- `rProtDe` ausente.
- codigo o mensaje no reconocido.
- respuesta tecnica de transporte.

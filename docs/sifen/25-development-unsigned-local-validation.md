<!-- scope: sifen-fe | relevant for: development, diagnostic, unsigned, xml, xsd, certificate, csc -->

# Validacion local FE sin firma en Development

## Purpose

Documentar la excepcion controlada para `TipoDoc 01` cuando la instancia corre en desarrollo y se necesita generar XML local aunque falten `CSC`, certificado o `XSD`.

## Data model

- `Sifen:Development:AllowUnsignedInternalValidation`
- `Sifen:Transport:Mode`
- `SifenDocumentStatus.DraftValidatedWithoutSignature`
- `SifenDocumentStatus.InternalValidationFailed`

## API / endpoints

- Impacta `POST /invoice/`
- Impacta `POST /api/fe/invoices`
- No cambia contratos de descarga de XML ni consulta de estado

## Business rules

- El bypass solo aplica cuando se cumplen las tres condiciones:
  - `EnvironmentName = Development`
  - `TransportMode = Diagnostic`
  - `AllowUnsignedInternalValidation = true`
- En ese modo el sistema puede:
  - generar y persistir el XML local
  - no firmar si falta certificado
  - no llamar SIFEN
  - no marcar `Approved`
- Si faltan `CSC`, certificado o `XSD` para completar la validacion total, el documento queda en `DraftValidatedWithoutSignature`.
- Si ocurre una falla real de validacion interna distinta al faltante esperado, el documento queda en `InternalValidationFailed`.
- El mensaje operativo esperado es:
  - `XML generated locally. Certificate/XSD/CSC are missing for full validation.`

## Constraints

- No habilitar este flujo en `Live`.
- No habilitar este flujo en `Production`.
- No usar este bypass para homologacion ni emision real.
- No tocar `TramiYa`.

## Edge cases

- `XSD` no configurado o archivo inexistente
- metadata de certificado inexistente
- secreto `CSC` no resoluble
- transporte `Diagnostic` con tenant no listo para emision real

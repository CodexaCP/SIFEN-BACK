<!-- scope: sifen-fe | relevant for: kude, preview, pdf, codexa-standard, tenant-template -->

# KuDE Codexa Standard

## Purpose

Definir la plantilla `Codexa Standard` para KuDE con preview HTML, PDF demo y generacion PDF real desde la factura FE del tenant.

## Data model

- `TenantKudeTemplateSettings`
  - `TenantId`
  - `TemplateCode`
  - `LogoUrl`
  - `PrimaryColor`
  - `SecondaryColor`
  - `FooterText`
  - `ShowPhone`
  - `ShowEmail`
- `SifenDocument`
  - reutiliza `XmlPayload`, `Cdc`, `ExternalDocumentNumber`, `IssuedAt`, `Status`
- `TaxpayerProfile`
  - reutiliza `RucNumber`, `RucCheckDigit`, `LegalName`

## API / endpoints

- `GET /api/platform/companies/{tenantId}/kude-template`
- `PUT /api/platform/companies/{tenantId}/kude-template`
- `POST /api/fe/invoices/kude/preview`
- `POST /api/fe/invoices/kude/preview/pdf`
- `GET /api/fe/invoices/{id}/kude`
- `GET /api/fe/invoices/{id}/xml`

## Business rules

- Solo existe una plantilla soportada en esta fase:
  - `codexa-standard`
- El preview siempre usa HTML real y datos demo.
- El PDF demo reutiliza la misma plantilla y el mismo set de parametros del preview.
- El KuDE real se genera desde la factura FE persistida y su XML.
- El tenant puede parametrizar:
  - `logoUrl`
  - `primaryColor`
  - `secondaryColor`
  - `footerText`
  - `showPhone`
  - `showEmail`
- Si no existe configuracion del tenant, se aplican valores fallback.
- Siempre deben quedar visibles:
  - `CDC`
  - `QR`
  - `RUC emisor`
  - `Razon social`
  - `Numero de documento`
  - `Fecha`
  - `Totales`
- El preview debe mostrar el aviso:
  - `Vista previa con datos de prueba. No es comprobante válido.`

## Constraints

- No usar imagen estatica como KuDE final.
- No romper los endpoints existentes de XML ni KuDE.
- Mantener compatibilidad con el flujo FE actual.
- No alterar la estructura legal minima visible del comprobante.

## Edge cases

- tenant sin configuracion KuDE
- XML sin items
- XML sin totales
- logo remoto no disponible
- factura inexistente al descargar KuDE

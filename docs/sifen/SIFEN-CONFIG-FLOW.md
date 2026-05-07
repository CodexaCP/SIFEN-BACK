<!-- scope: sifen-config | relevant for: tenant, config, taxpayer-profile, diagnostics, security -->

# SIFEN Config Flow

## Purpose

Documentar como se carga y guarda la configuracion SIFEN por tenant desde `Codexa-WEB`, que datos se persisten y que restricciones de seguridad aplica el backend.

## Data model

- `Tenant`
  - `Id`
  - `Status`
- `TaxpayerProfile`
  - `TenantId`
  - `RucNumber`
  - `RucCheckDigit`
  - `LegalName`
- `TenantSifenSettings`
  - `TenantId`
  - `Environment`
  - `CscIdentifier`
  - `CscSecretReference`
  - `CertificateSecretReference`
  - `CertificatePasswordSecretReference`
  - `CertificateAlias`
  - `EstablishmentCode`
  - `ExpeditionPointCode`
  - `CurrentDocumentNumber`
  - `StampingNumber`
- `TenantCertificateMetadata`
  - se usa solo para diagnostico de certificado activo

## API / endpoints

- `GET /api/platform/companies/{tenantId}/sifen-config`
- `PUT /api/platform/companies/{tenantId}/sifen-config`
- `GET /api/fe/diagnostic/{tenantId}`

## Business rules

- `SuperAdmin` puede configurar cualquier tenant.
- `TenantAdmin` solo puede configurar su propio tenant.
- El tenant debe existir y estar `Active`.
- El frontend carga la configuracion actual con `GET`.
- Si todavia no existe configuracion, `GET` devuelve el tenant con campos `null` y `readySummary`.
- `PUT` hace upsert real sobre:
  - `TaxpayerProfile`
  - `TenantSifenSettings`
- Los campos operativos ya no dependen de facturas previas:
  - establecimiento
  - punto de expedicion
  - numeracion actual
  - timbrado
- El diagnostico FE usa esa configuracion persistida para sus checks operativos.

## Campos obligatorios

- `ruc`
- `rucCheckDigit`
- `legalName`
- `environment`
  - `Test` o `Production`
- `cscSecretReference`
- `establishment`
  - 3 digitos, se normaliza con padding izquierdo
- `expeditionPoint`
  - 3 digitos, se normaliza con padding izquierdo
- `currentNumber`
  - 7 digitos, se normaliza con padding izquierdo
- `stampingNumber`

## Que se guarda

- identidad fiscal del tenant
- ambiente operativo
- identificador CSC
- referencias seguras a secretos
- alias de certificado
- establecimiento
- punto de expedicion
- numeracion actual
- timbrado

## Que NO se guarda por seguridad

- password real del certificado
- contenido binario del certificado
- secreto CSC en texto plano

## Constraints

- `PUT` solo acepta referencias de secretos, no archivos ni passwords reales.
- `GET` puede devolver referencias como `certificateSecretReference`, pero nunca contenido sensible.
- El readiness de certificado sigue dependiendo de `TenantCertificateMetadata` y de que las referencias resuelvan correctamente.
- Si el tenant configura `Production` pero la instancia corre con `Sifen:ActiveEnvironment=Test`, el diagnostico sigue evaluando el ambiente activo de la instancia.

## Edge cases

- `TenantAdmin` intentando otro tenant por URL
- tenant inactivo
- RUC o razon social vacios
- establecimiento con longitud invalida
- punto con longitud invalida
- numeracion invalida
- timbrado vacio
- referencia de secreto no resoluble
- metadata de certificado inexistente

## Flujo operativo

1. Login como `SuperAdmin` o `TenantAdmin`.
2. Abrir `/sifen/admin/companies/{tenantId}/sifen-config`.
3. `GET` carga configuracion actual o formulario vacio controlado.
4. Completar identidad fiscal y configuracion operativa.
5. Guardar con `PUT`.
6. Reconsultar `GET` para verificar normalizacion y persistencia.
7. Abrir diagnostico del tenant.
8. Confirmar que establecimiento, punto, numeracion y timbrado aparecen como configurados.

## Validacion de diagnostico

- Debe marcar como configurados:
  - establecimiento
  - punto de expedicion
  - numeracion actual
  - timbrado
- Puede seguir quedando `not ready` si faltan:
  - metadata de certificado
  - secreto CSC resoluble
  - password de certificado resoluble

<!-- scope: sifen-overview | relevant for: module, explanation, flow, missing-pieces, real-test -->

# SIFEN Module Overview

## Purpose

Explicar que hace el modulo `SIFEN`, donde esta hoy, como deberia funcionar y que falta para pasar de base tecnica a operacion real.

## Data model

- `Tenant`
- `TaxpayerProfile`
- `TenantSifenSettings`
- `TenantCertificateMetadata`
- `PlatformUser`
- `SifenDocument`
- `SifenDocumentLog`

## API / endpoints

- autenticacion: `/api/auth/login`
- companias: `/api/platform/companies`
- FE simple: `/api/fe/invoices`
- estado: `/api/fe/invoices/status/{cdc}`
- XML/KuDE: `/api/fe/invoices/{id}/xml`, `/api/fe/invoices/{id}/kude`
- diagnostico: `/api/fe/diagnostic/{tenantId}`

## Business rules

- `SIFEN` es un modulo SaaS para vender FE a multiples companias.
- Cada compania es un `Tenant`.
- Cada tenant necesita plan + configuracion fiscal + certificados + CSC.
- El motor FE ya sabe:
  - generar CDC
  - generar XML
  - firmar XML
  - preparar SOAP
  - parsear respuesta
  - persistir estado
- El sistema no debe considerarse listo para vender FE real sin una respuesta real de SIFEN test.

## Constraints

- UI multiempresa todavia incompleta.
- Homologacion real SIFEN test aun no cerrada.

## Edge cases

- login OK pero sin tenant seleccionado
- tenant creado sin readiness
- limite de plan alcanzado
- tenant inactivo
- falta de CSC/certificados/XSD

## Como funciona hoy

### Capa 1. Plataforma

- `SuperAdmin` global entra al sistema
- ve companias
- crea companias
- define planes
- crea admins de compania

### Capa 2. Tenant FE

Cada compania que use `SIFEN` necesita:

- RUC y razon social
- CSC
- certificado de firma
- ambiente
- establecimiento
- punto
- numeracion

### Capa 3. Operacion FE

Una vez listo el tenant:

1. se crea factura
2. se genera CDC
3. se arma XML
4. se valida
5. se firma
6. se envia a SIFEN
7. se parsea respuesta
8. se guarda estado
9. se descarga XML / KuDE

## Lo que necesitas para probar con un RUC real o test

### Para test

- tenant configurado
- CSC test
- certificado valido
- XSD oficial configurado
- transporte real
- numeracion valida

### Para real

Todo lo anterior, pero con credenciales y habilitacion real.

## Lo que falta hoy

- tenant context automatico desde frontend
- CRUD de companias por UI
- configuracion SIFEN por UI
- estilo final SIFEN azul cielo
- prueba homologada contra SIFEN test

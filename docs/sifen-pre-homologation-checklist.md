<!-- scope: sifen-pre-homologation | relevant for: diagnostic, readiness, internal-validation, sifen-test -->

# SIFEN Pre-Homologation Checklist

## Purpose

Definir que valida el backend FE antes de tener credenciales reales y como interpretar el estado del tenant para avanzar a la primera prueba real en `SIFEN test`.

## Data model

- `Tenant`
- `TaxpayerProfile`
- `TenantSifenSettings`
- `TenantCertificateMetadata`
- `SifenDocument`

## API / endpoints

- `GET /api/fe/diagnostic/{tenantId}`
- `POST /api/fe/invoices`
- `GET /api/fe/invoices/status/{cdc}`
- `GET /api/fe/invoices/{id}/xml`
- `GET /api/fe/invoices/{id}/kude`

## Business rules

- El diagnostico clasifica el tenant en tres estados:
  - incompleto
  - listo para validacion interna
  - listo para intentar primera FE real en `SIFEN test`
- `Transport.Mode=Diagnostic` permite generar `TipoDoc 01`, crear `CDC`, validar estructura basica, firmar XML si el certificado esta listo y persistir el documento como `InternalValidation`, pero no debe considerarse intento real.
- Si la validacion local XSD o la firma fallan en `Diagnostic`, el documento debe persistirse como `InternalValidationFailed` con detalle tecnico util para soporte.
- Si `Transport.Mode` no es `Diagnostic` y falta configuracion critica, la emision real debe bloquearse con:
  - `Tenant SIFEN configuration is incomplete. Run diagnostic first.`
- Ningun flujo mock o diagnostic puede persistirse como `Approved`.

## Que puede validarse sin RUC real

- existencia y estado del tenant
- estado comercial y limite mensual
- presencia de `TaxpayerProfile`
- presencia de RUC y razon social cargados
- configuracion por ambiente `Test/Production`
- referencia de `CSC`
- referencia de certificado y password
- establecimiento, punto de expedicion y numeracion
- path de XSD
- `Transport.Mode`
- endpoint configurado
- generacion local de XML `TipoDoc 01`
- firma local si el certificado esta disponible en el entorno

## Que NO puede validarse sin datos reales

- aprobacion real de un DE por SIFEN
- acceso mTLS real al endpoint DNIT si no hay certificado/transporte valido
- validez tributaria real del RUC contra entorno oficial
- evidencia de homologacion real

## Que falta para intentar SIFEN test

- RUC de test valido
- CSC real de test
- certificado de firma valido y su password
- certificado/configuracion de transporte real si el modo sera `Live`
- XSD oficial disponible en disco
- numeracion, establecimiento y punto operativos
- `Transport.Mode=Live`

## Constraints

- alcance solo Factura Electronica `TipoDoc 01`
- sin notas, eventos, lotes ni recepcion de terceros
- no imprimir secretos ni referencias sensibles en respuestas

## Edge cases

- tenant suspendido
- limite mensual alcanzado
- `TaxpayerProfile` ausente
- XSD configurado pero inexistente en disco
- certificado referenciado pero no cargable
- `Transport.Mode` desconocido
- endpoint configurado en `Diagnostic`, pero no habilitado para intento real

## Pasos cuando ya existan datos reales

1. cargar `TaxpayerProfile` real o de test
2. cargar `TenantSifenSettings` del ambiente objetivo
3. cargar metadata y secretos del certificado de firma
4. configurar XSD oficial en `Sifen:XmlSchemas:Invoice01RootPath`
5. configurar endpoint y transporte real
6. ejecutar `GET /api/fe/diagnostic/{tenantId}`
7. confirmar `readyForInternalValidation=true`
8. cambiar a `Transport.Mode=Live`
9. confirmar `readyForSifenTestAttempt=true`
10. ejecutar la primera factura real contra `SIFEN test`

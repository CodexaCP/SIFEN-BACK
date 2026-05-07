<!-- scope: sifen-fe | relevant for: tenant, homologacion, test-real, configuracion -->

# Checklist por tenant para primera emision real SIFEN test

## Purpose

Definir exactamente que debe existir por tenant antes de ejecutar la primera FE real TipoDoc 01 contra SIFEN test.

## Data model

- `Tenant`
- `TaxpayerProfile`
- `TenantSifenSettings`
- `TenantCertificateMetadata`
- `SifenDocument`

## API / endpoints

- `GET /api/fe/diagnostic/{tenantId}`
- `GET /api/fe/plan/{tenantId}`
- `POST /api/fe/invoices`
- `GET /api/fe/invoices/status/{cdc}`
- `GET /api/fe/invoices/{id}/xml`
- `GET /api/fe/invoices/{id}/kude`

## Business rules

- Sin respuesta real de SIFEN test, el sistema no debe marcarse como listo para vender FE real.
- La primera emision real debe usar un tenant activo, datos fiscales reales y certificados reales.
- Si falta cualquier punto critico, la emision real debe frenarse.

## Constraints

- Solo FE `TipoDoc 01`.
- No cubre NC, ND, remision, exportacion ni lotes.

## Edge cases

- tenant activo pero sin CSC
- certificado vigente pero sin clave accesible
- modo transporte en diagnostico
- numeracion ya usada
- XSD no configurado

## Checklist exacto

### 1. Tenant

- `Tenant.Id` real definido
- `Tenant.Status = Active`
- `Tenant.MaxInvoicesPerMonth` compatible con la prueba
- `GET /api/fe/diagnostic/{tenantId}` debe responder sin faltantes criticos

### 2. Perfil tributario

- `TaxpayerProfile` activo para el tenant
- RUC numero real
- digito verificador real
- razon social real

### 3. CSC

- `TenantSifenSettings` activo para `Environment = Test`
- `CscIdentifier` real
- `CscSecretReference` resolviendo a valor real

### 4. Certificado de firma XML

- registro `TenantCertificateMetadata` activo para:
  - `Environment = Test`
  - `Purpose = XmlSignature`
- `CertificateSecretReference` resolviendo al archivo real
- `CertificatePasswordSecretReference` resolviendo al password real
- certificado vigente
- certificado correspondiente al RUC del emisor

### 5. Certificado de transporte mTLS

- `Sifen:Transport:Mode = Live`
- `Sifen:Transport:ClientCertificatePath` con archivo real
- variable de entorno `SIFEN_TRANSPORT_CERTIFICATE_PASSWORD` con password real

### 6. XSD

- `Sifen:XmlSchemas:Invoice01RootPath` apuntando al XSD oficial real
- validacion XSD activa antes de firmar/enviar

### 7. Parametros fiscales reales

- timbrado vigente
- establecimiento valido
- punto de expedicion valido
- numeracion valida y no usada
- moneda valida

### 8. Configuracion global minima

- `Sifen:ActiveEnvironment = Test`
- `Sifen:Environments:Test:BaseUrl` correcto
- `Sifen:Environments:Test:Wsdl:Receive` correcto
- `Sifen:Timeouts:SoapRequestSeconds` definido

### 9. Payload de prueba

- receptor real o de prueba valido para ambiente test
- items validos
- total consistente
- RUC/documento receptor valido

### 10. Evidencia obligatoria a guardar

- `CDC`
- XML generado
- XML firmado
- request SOAP
- response SOAP
- `statusCode`
- `statusMessage`
- estado final persistido

## Estado actual del proyecto

- `Sifen:Transport:Mode` hoy esta en `Diagnostic`
- `Sifen:Transport:ClientCertificatePath` hoy esta vacio
- `Sifen:Certificate:Path` hoy esta vacio
- `Sifen:XmlSchemas:Invoice01RootPath` hoy esta vacio
- por eso hoy el entorno **no esta listo** para una primera emision real

## Criterio de listo para ejecutar

Se puede lanzar la primera FE real solo cuando:

- todos los puntos 1 a 9 esten completos
- el diagnostico del tenant no tenga faltantes criticos
- el transporte este en `Live`
- exista una numeracion valida disponible


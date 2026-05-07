# Onboarding interno de tenants

Fecha: 2026-04-23

## Objetivo

Crear el flujo interno inicial para registrar un cliente SaaS y verificar si tiene los prerequisitos minimos para avanzar hacia SIFEN test.

Este bloque no envia documentos a SIFEN y no carga certificados reales. Solo registra metadatos y referencias seguras.

## SQL Server 2022

La configuracion de desarrollo apunta a:

```text
Server=localhost\SQLEXPRESS;Database=SifenInvoicing;Trusted_Connection=True;TrustServerCertificate=True
```

Se detectaron instancias locales:

- `SQLEXPRESS`
- `SQLEXPRESS01`

Si quieres usar la segunda, cambia:

```json
"DefaultConnection": "Server=localhost\\SQLEXPRESS01;Database=SifenInvoicing;Trusted_Connection=True;TrustServerCertificate=True"
```

Tambien puedes usar variable de entorno para migraciones:

```text
SIFEN_CONNECTION_STRING
```

## Endpoints internos

Base:

```text
/internal/onboarding
```

### Crear tenant

```http
POST /internal/onboarding/tenants
```

Body:

```json
{
  "slug": "cliente-demo",
  "displayName": "Cliente Demo"
}
```

### Registrar perfil de contribuyente

```http
POST /internal/onboarding/tenants/{tenantId}/taxpayer-profile
```

Body:

```json
{
  "rucNumber": "80000000",
  "rucCheckDigit": "0",
  "legalName": "Cliente Demo SA",
  "tradeName": "Cliente Demo"
}
```

Nota:

- Todavia no se valida RUC contra DNIT/SIFEN.
- Esta validacion debe llegar con `siConsRUC` o proceso documental confirmado.

### Registrar settings SIFEN

```http
POST /internal/onboarding/tenants/{tenantId}/sifen-settings
```

Body:

```json
{
  "environment": "Test",
  "cscIdentifier": "0001",
  "cscSecretReference": "secret://tenant/{tenantId}/sifen/test/csc"
}
```

Nota:

- No se guarda el CSC real.
- Solo se guarda una referencia.

### Registrar metadatos de certificado

```http
POST /internal/onboarding/tenants/{tenantId}/certificates
```

Body:

```json
{
  "environment": "Test",
  "purpose": "XmlSignature",
  "alias": "test-signing",
  "subject": "CN=Example",
  "fingerprintSha256": "ABC123",
  "serialNumber": "SERIAL123",
  "certificateSecretReference": "config:Sifen:LocalSecrets:TenantA:Test:CertificatePath",
  "certificatePasswordSecretReference": "config:Sifen:LocalSecrets:TenantA:Test:CertificatePassword",
  "validFrom": "2026-01-01T00:00:00Z",
  "validTo": "2027-01-01T00:00:00Z"
}
```

Propositos soportados:

- `XmlSignature`
- `MutualTls`

Nota:

- No se guarda PFX/P12.
- No se guarda clave privada.
- No se valida aun la cadena, RUC, vencimiento real contra certificado cargado ni EKU.

### Readiness

```http
GET /internal/onboarding/tenants/{tenantId}/readiness?environment=Test
```

Checks:

- `tenant.exists`
- `tenant.active`
- `taxpayer_profile.active`
- `sifen_settings.active`
- `csc.reference`
- `certificate.xml_signature`
- `certificate.mutual_tls`
Checks de secretos/certificados:

- `csc.secret`
- `certificate.xml_signature`
- `certificate.mutual_tls`

Importante:

- El readiness valida referencias de secretos y carga de certificado en modo local.
- En produccion se debe reemplazar el provider local por Vault/HSM/secret manager.

## Manejo de errores

Se agrego middleware de errores:

- `DomainException` -> HTTP 400.
- `InvalidOperationException` -> HTTP 409.
- Errores no controlados -> HTTP 500.

## Verificacion ejecutada

Build:

```bash
dotnet build SifenInvoicing.sln
```

Tests:

```bash
dotnet test SifenInvoicing.sln --no-build
```

Resultado:

- Build correcto.
- Tests correctos: 6/6.
- Migracion aplicada en SQL Server 2022 `localhost\SQLEXPRESS`.
- `GET /health/ready`: Healthy.
- `POST /internal/onboarding/tenants`: creado correctamente.
- `GET /internal/onboarding/tenants/{tenantId}/readiness?environment=Test`: devuelve no listo, como corresponde.

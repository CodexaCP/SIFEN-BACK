# Secretos y certificados por tenant

Fecha: 2026-04-23

## Decision aplicada

Se implemento el patron recomendado:

- SQL Server guarda solo metadatos y referencias.
- PFX/P12, password y CSC no se guardan como columnas de negocio.
- Application usa abstracciones:
  - `ITenantSecretProvider`
  - `ITenantCertificateValidator`
- Infrastructure implementa un provider local intercambiable por Vault/HSM despues.

## Migracion

Se separaron referencias de certificado:

- `CertificateSecretReference`
- `CertificatePasswordSecretReference`

Migracion:

```text
src/SifenInvoicing.Infrastructure/Persistence/Migrations/20260423114543_SplitCertificateSecretReferences.cs
```

Aplicada en SQL Server 2022:

```text
localhost\SQLEXPRESS
```

## Referencias soportadas en desarrollo

### `config:`

Lee un valor desde configuracion .NET.

Ejemplo para CSC:

```text
config:Sifen:LocalSecrets:TenantA:Test:Csc
```

Ejemplo para password:

```text
config:Sifen:LocalSecrets:TenantA:Test:CertificatePassword
```

Ejemplo para PFX:

```text
config:Sifen:LocalSecrets:TenantA:Test:CertificatePath
```

En el caso del PFX, el valor de config debe ser una ruta local fuera del repo.

### `env:`

Lee una variable de entorno. Solo para secretos string.

Ejemplo:

```text
env:SIFEN_TENANT_A_CSC
```

### `file:`

Lee archivo local directamente.

Ejemplo:

```text
file:C:\SecureLocal\SifenCerts\tenant-a-test.pfx
```

Uso recomendado:

- Solo desarrollo local.
- No commitear archivos `.pfx` / `.p12`.
- Mantenerlos fuera del repo.

## User secrets recomendado

Desde el proyecto API:

```bash
dotnet user-secrets init --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj
dotnet user-secrets set "Sifen:LocalSecrets:TenantA:Test:CertificatePath" "C:\SecureLocal\SifenCerts\tenant-a-test.pfx" --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj
dotnet user-secrets set "Sifen:LocalSecrets:TenantA:Test:CertificatePassword" "<password>" --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj
dotnet user-secrets set "Sifen:LocalSecrets:TenantA:Test:Csc" "<csc>" --project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj
```

No pongas esos valores en `appsettings.json`.

## Validaciones implementadas

Para CSC:

- La referencia debe resolverse.
- El valor no debe estar vacio.

Para certificado:

- PFX/P12 debe resolverse.
- Password debe resolverse.
- El certificado debe cargar en memoria con `EphemeralKeySet`.
- Debe tener private key.
- SHA-256 fingerprint debe coincidir con metadata.
- Debe estar vigente al momento de validacion.

## Readiness

`GET /internal/onboarding/tenants/{tenantId}/readiness?environment=Test`

Checks actuales:

- `tenant.exists`
- `tenant.active`
- `taxpayer_profile.active`
- `sifen_settings.active`
- `csc.secret`
- `certificate.xml_signature`
- `certificate.mutual_tls`

El tenant solo queda listo si todos pasan.

## Pendiente para produccion

Reemplazar `LocalConfigurationTenantSecretProvider` por una implementacion productiva:

- Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, HSM o equivalente.
- RBAC estricto.
- Auditoria de lectura/uso de secretos.
- Rotacion de certificado y CSC.
- No exponer secretos a soporte.

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
- Tests correctos: 9/9.
- Migracion aplicada en SQL Server 2022.
- `/health/ready`: Healthy.
- Readiness usa checks reales de secretos/certificados.


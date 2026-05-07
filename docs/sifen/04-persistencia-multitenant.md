# Persistencia multi-tenant inicial

Fecha: 2026-04-23

## Decision aplicada

Se adopto la recomendacion recibida:

- SaaS multi-tenant con base SQL Server compartida.
- `TenantId` obligatorio para datos de negocio.
- Certificados y secretos aislados fuera de tablas normales.
- SQL guarda solo metadatos y referencias a secretos.
- Diseno preparado para migrar tenants grandes a base dedicada en el futuro.

## Tablas iniciales

### Tenants

Representa un cliente/tenant del SaaS.

Campos clave:

- `Id`
- `Slug`
- `DisplayName`
- `Status`
- `IsolationMode`
- `CreatedAt`
- `UpdatedAt`

`IsolationMode` permite preparar el camino:

- `SharedDatabase`
- `DedicatedDatabase`

### TaxpayerProfiles

Representa el contribuyente/RUC asociado al tenant.

Campos clave:

- `TenantId`
- `RucNumber`
- `RucCheckDigit`
- `LegalName`
- `TradeName`
- `IsActive`

Indice unico:

- `TenantId + RucNumber + RucCheckDigit`

### TenantSifenSettings

Configuracion SIFEN por tenant y ambiente.

Campos clave:

- `TenantId`
- `Environment`
- `CscIdentifier`
- `CscSecretReference`
- `IsActive`

Importante:

- No se guarda el CSC como valor secreto.
- Se guarda una referencia para resolverlo luego mediante capa de secretos.

### TenantCertificateMetadata

Metadatos de certificados por tenant.

Campos clave:

- `TenantId`
- `Environment`
- `Purpose`
- `Alias`
- `Subject`
- `FingerprintSha256`
- `SerialNumber`
- `SecretReference`
- `ValidFrom`
- `ValidTo`
- `IsActive`

Importante:

- No se guarda clave privada.
- No se guarda PFX/P12 como blob.
- `SecretReference` apunta a la futura capa segura de secretos/firma.

## Migracion

Migracion generada:

```text
src/SifenInvoicing.Infrastructure/Persistence/Migrations/20260423045833_InitialTenantPersistence.cs
```

Herramienta local:

```text
.config/dotnet-tools.json
```

Comando usado para generar:

```bash
dotnet tool run dotnet-ef migrations add InitialTenantPersistence --project src/SifenInvoicing.Infrastructure/SifenInvoicing.Infrastructure.csproj --startup-project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj --output-dir Persistence/Migrations
```

Comando usado para aplicar:

```bash
dotnet tool run dotnet-ef database update --project src/SifenInvoicing.Infrastructure/SifenInvoicing.Infrastructure.csproj --startup-project src/SifenInvoicing.Api/SifenInvoicing.Api.csproj
```

Resultado:

- Base de desarrollo `SifenInvoicing` creada/actualizada en `(localdb)\MSSQLLocalDB`.

## Protecciones implementadas

### Auditoria automatica

`SifenDbContext` marca:

- `CreatedAt` en inserts.
- `UpdatedAt` en updates.

### Proteccion de tenant

Si el request/job tiene `ResolvedTenantId`, `SaveChanges` bloquea modificaciones de entidades con otro `TenantId`.

Esto evita que un endpoint o worker con contexto tenant A modifique datos tenant B.

### Query filters

Las entidades tenant-scoped tienen filtro por `TenantId` cuando existe `ResolvedTenantId`.

Nota:

- La resolucion actual parsea `X-Tenant-Id` como GUID si es posible.
- En el futuro el header/API key/JWT debe resolverse contra `Tenants` y cargar `ResolvedTenantId`.

## Health checks

`GET /health/ready` ahora valida:

- Configuracion SIFEN.
- Conectividad SQL Server.

`GET /ops/dependencies` ahora reporta SQL Server como dependencia operacional real.

## Verificacion

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
- `/health/ready`: Healthy.
- `/ops/dependencies`: `sql-server = Healthy`.


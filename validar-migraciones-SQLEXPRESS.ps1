# Validación desde cero SOLO en .\SQLEXPRESS, BD SIFEN_FASE3_SQLSERVER_TEST. No toca otras BDs.
# Ejecutar desde la raíz del repo SIFEN-BACK (PowerShell). No modifica migraciones, snapshot ni código.
$ErrorActionPreference = 'Stop'
$srv = '.\SQLEXPRESS'; $db = 'SIFEN_FASE3_SQLSERVER_TEST'
$cn = "Server=$srv;Database=$db;Trusted_Connection=True;TrustServerCertificate=True"
$out = Join-Path $PSScriptRoot 'resultado-sqlexpress.txt'

# 1) Abortar si ya existe (no se borra ni se modifica)
$exists = sqlcmd -S $srv -E -C -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name='$db'"
if ([int]($exists | Select-Object -First 1) -ne 0) { Write-Host "YA EXISTE $db en $srv. Detenido, no se toca."; exit 1 }

# 2) Crear vacía
sqlcmd -S $srv -E -C -Q "CREATE DATABASE [$db]"

# 3) Aplicar las migraciones ACTUALES, sin modificarlas
dotnet tool restore
$env:SIFEN_CONNECTION_STRING = $cn
dotnet ef database update -p src/SifenInvoicing.Infrastructure -s src/SifenInvoicing.Api 2>&1 | Tee-Object -FilePath $out

# 4) Extraer esquema (solo lectura)
"`n===== SCHEMA =====" | Add-Content $out
sqlcmd -S $srv -d $db -E -C -W -h -1 -w 500 -i (Join-Path $PSScriptRoot 'extraer-esquema-solo-lectura.sql') | Sort-Object | Add-Content $out
"`n===== HISTORY =====" | Add-Content $out
sqlcmd -S $srv -d $db -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT MigrationId FROM __EFMigrationsHistory ORDER BY 1" | Add-Content $out
"`n===== VERSION =====" | Add-Content $out
sqlcmd -S $srv -d $db -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT @@VERSION" | Add-Content $out
Write-Host "Listo: $out"

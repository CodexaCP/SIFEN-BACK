using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Completa la cadena de migraciones para que una BD vacia quede identica al modelo EF actual sin scripts manuales:
    /// limites de plan de Tenants, columnas de Documents, DocumentLines, FeInvoiceEvents y FeTenantLogs.
    /// Sustituye a la clase huerfana AddTenantPlanLimits (nunca formo parte de la cadena) y a los scripts
    /// 20260505_* / 20260506_* (historicos). Sin guardas IF NOT EXISTS: falla de forma explicita si el esquema previo
    /// no es el que producen las migraciones anteriores.
    /// </summary>
    public partial class ReconcileSchemaWithEfModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tenants: limites de plan
            migrationBuilder.AddColumn<int>(name: "MaxInvoicesPerMonth", table: "Tenants", type: "int", nullable: true);
            migrationBuilder.AddColumn<int>(name: "MaxUsers", table: "Tenants", type: "int", nullable: true);

            // Documents: anchos segun configuracion (el indice unico que los incluye lo reconstruye EF)
            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode", table: "Documents", type: "nvarchar(10)", maxLength: 10, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(3)", oldMaxLength: 3);
            migrationBuilder.AlterColumn<string>(
                name: "EstablishmentCode", table: "Documents", type: "nvarchar(10)", maxLength: 10, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(3)", oldMaxLength: 3);
            migrationBuilder.AlterColumn<string>(
                name: "ExpeditionPointCode", table: "Documents", type: "nvarchar(10)", maxLength: 10, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(3)", oldMaxLength: 3);
            migrationBuilder.AlterColumn<string>(
                name: "ExternalDocumentNumber", table: "Documents", type: "nvarchar(20)", maxLength: 20, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(7)", oldMaxLength: 7);

            // Documents: trazabilidad
            migrationBuilder.AddColumn<string>(name: "CorrelationId", table: "Documents", type: "nvarchar(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "InternalStatus", table: "Documents", type: "nvarchar(40)", maxLength: 40, nullable: false, defaultValue: "DRAFT");
            migrationBuilder.AddColumn<int>(name: "RetryCount", table: "Documents", type: "int", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<bool>(name: "IsRetryable", table: "Documents", type: "bit", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<string>(name: "LastErrorCode", table: "Documents", type: "nvarchar(80)", maxLength: 80, nullable: true);
            migrationBuilder.AddColumn<string>(name: "LastErrorMessage", table: "Documents", type: "nvarchar(500)", maxLength: 500, nullable: true);

            // Documents: datos del documento y receptor. DocumentType y SaleCondition son NOT NULL sin default en el
            // modelo: el default "" solo existe para poder agregarlas y se elimina a continuacion.
            migrationBuilder.AddColumn<string>(name: "DocumentType", table: "Documents", type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "SaleCondition", table: "Documents", type: "nvarchar(80)", maxLength: 80, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(name: "Notes", table: "Documents", type: "nvarchar(1000)", maxLength: 1000, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ReceiverAddress", table: "Documents", type: "nvarchar(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ReceiverEmail", table: "Documents", type: "nvarchar(250)", maxLength: 250, nullable: true);
            migrationBuilder.AddColumn<string>(name: "ReceiverPhone", table: "Documents", type: "nvarchar(60)", maxLength: 60, nullable: true);

            // Documents: montos y vista previa fiscal
            migrationBuilder.AddColumn<decimal>(name: "SubtotalAmount", table: "Documents", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "Vat5Amount", table: "Documents", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "Vat10Amount", table: "Documents", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "ExemptAmount", table: "Documents", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "TotalVatAmount", table: "Documents", type: "decimal(18,2)", nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<string>(name: "TestCdc", table: "Documents", type: "nvarchar(120)", maxLength: 120, nullable: true);
            migrationBuilder.AddColumn<string>(name: "TestQrText", table: "Documents", type: "nvarchar(500)", maxLength: 500, nullable: true);
            migrationBuilder.AddColumn<bool>(name: "IsFiscalPreviewValid", table: "Documents", type: "bit", nullable: false, defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DocumentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VatRate = table.Column<int>(type: "int", nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExemptAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SubtotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentLines_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeInvoiceEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TechnicalDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeInvoiceEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeTenantLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Level = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TechnicalDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, defaultValueSql: "sysutcdatetime()"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeTenantLogs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentLines_DocumentId_LineNumber",
                table: "DocumentLines",
                columns: new[] { "DocumentId", "LineNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeInvoiceEvents_TenantId_InvoiceId_CreatedAt",
                table: "FeInvoiceEvents",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeTenantLogs_TenantId_CreatedAt",
                table: "FeTenantLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FeTenantLogs_TenantId_InvoiceId_CreatedAt",
                table: "FeTenantLogs",
                columns: new[] { "TenantId", "InvoiceId", "CreatedAt" });

            // Defaults que el modelo no declara (restos de defaultValue "" en migraciones anteriores, o auxiliares de
            // esta migracion). Los nombres de las restricciones son autogenerados, por eso se buscan por tabla/columna.
            // Si el default no existe, la migracion falla: el esquema previo no es el esperado.
            DropDefault(migrationBuilder, "Documents", "DocumentType");
            DropDefault(migrationBuilder, "Documents", "SaleCondition");
            DropDefault(migrationBuilder, "PlatformUsers", "FullName");
            DropDefault(migrationBuilder, "TenantCertificateMetadata", "CertificatePasswordSecretReference");
            DropDefault(migrationBuilder, "TenantSifenSettings", "CurrentDocumentNumber");
            DropDefault(migrationBuilder, "TenantSifenSettings", "EstablishmentCode");
            DropDefault(migrationBuilder, "TenantSifenSettings", "ExpeditionPointCode");
            DropDefault(migrationBuilder, "Tenants", "PlanName");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restituye los defaults "" previos (sin los cuales las columnas NOT NULL no aceptarian filas antiguas).
            migrationBuilder.Sql("ALTER TABLE [PlatformUsers] ADD DEFAULT N'' FOR [FullName];");
            migrationBuilder.Sql("ALTER TABLE [TenantCertificateMetadata] ADD DEFAULT N'' FOR [CertificatePasswordSecretReference];");
            migrationBuilder.Sql("ALTER TABLE [TenantSifenSettings] ADD DEFAULT N'' FOR [CurrentDocumentNumber];");
            migrationBuilder.Sql("ALTER TABLE [TenantSifenSettings] ADD DEFAULT N'' FOR [EstablishmentCode];");
            migrationBuilder.Sql("ALTER TABLE [TenantSifenSettings] ADD DEFAULT N'' FOR [ExpeditionPointCode];");
            migrationBuilder.Sql("ALTER TABLE [Tenants] ADD DEFAULT N'' FOR [PlanName];");

            migrationBuilder.DropTable(name: "DocumentLines");
            migrationBuilder.DropTable(name: "FeInvoiceEvents");
            migrationBuilder.DropTable(name: "FeTenantLogs");

            // Quita defaults propios de las columnas antes de borrarlas.
            DropDefault(migrationBuilder, "Documents", "InternalStatus");
            DropDefault(migrationBuilder, "Documents", "RetryCount");
            DropDefault(migrationBuilder, "Documents", "IsRetryable");
            DropDefault(migrationBuilder, "Documents", "SubtotalAmount");
            DropDefault(migrationBuilder, "Documents", "Vat5Amount");
            DropDefault(migrationBuilder, "Documents", "Vat10Amount");
            DropDefault(migrationBuilder, "Documents", "ExemptAmount");
            DropDefault(migrationBuilder, "Documents", "TotalVatAmount");
            DropDefault(migrationBuilder, "Documents", "IsFiscalPreviewValid");

            foreach (var column in new[]
            {
                "CorrelationId", "InternalStatus", "RetryCount", "IsRetryable", "LastErrorCode", "LastErrorMessage",
                "DocumentType", "SaleCondition", "Notes", "ReceiverAddress", "ReceiverEmail", "ReceiverPhone",
                "SubtotalAmount", "Vat5Amount", "Vat10Amount", "ExemptAmount", "TotalVatAmount",
                "TestCdc", "TestQrText", "IsFiscalPreviewValid"
            })
            {
                migrationBuilder.DropColumn(name: column, table: "Documents");
            }

            // Reduce anchos al estado anterior (falla si hay datos que no caben).
            migrationBuilder.AlterColumn<string>(
                name: "ExternalDocumentNumber", table: "Documents", type: "nvarchar(7)", maxLength: 7, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(20)", oldMaxLength: 20);
            migrationBuilder.AlterColumn<string>(
                name: "ExpeditionPointCode", table: "Documents", type: "nvarchar(3)", maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(10)", oldMaxLength: 10);
            migrationBuilder.AlterColumn<string>(
                name: "EstablishmentCode", table: "Documents", type: "nvarchar(3)", maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(10)", oldMaxLength: 10);
            migrationBuilder.AlterColumn<string>(
                name: "CurrencyCode", table: "Documents", type: "nvarchar(3)", maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "nvarchar(10)", oldMaxLength: 10);

            migrationBuilder.DropColumn(name: "MaxUsers", table: "Tenants");
            migrationBuilder.DropColumn(name: "MaxInvoicesPerMonth", table: "Tenants");
        }

        private static void DropDefault(MigrationBuilder migrationBuilder, string table, string column)
        {
            migrationBuilder.Sql($@"
DECLARE @constraint sysname;
DECLARE @sql nvarchar(max);
SELECT @constraint = dc.name
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE dc.parent_object_id = OBJECT_ID(N'[dbo].[{table}]') AND c.name = N'{column}';
IF @constraint IS NULL THROW 50000, N'No existe el default esperado en {table}.{column}.', 1;
SET @sql = N'ALTER TABLE [dbo].[{table}] DROP CONSTRAINT ' + QUOTENAME(@constraint);
EXEC(@sql);");
        }
    }
}

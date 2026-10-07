using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SifenInvoicing.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFiscalNumberingAndIdempotency : Migration
    {
        /// <summary>
        /// Solo cambios de Fase 3 (configuracion fiscal, numeracion, idempotencia). El snapshot de EF estaba por detras
        /// de los scripts SQL manuales (DocumentLines, FeInvoiceEvents, FeTenantLogs, columnas de Documents): esas
        /// diferencias NO se incluyen aqui para no duplicar objetos ya creados por scripts.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_Environment_Kind_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber",
                table: "Documents");

            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "TaxpayerProfiles",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityCode",
                table: "TaxpayerProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CityDescription",
                table: "TaxpayerProfiles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentCode",
                table: "TaxpayerProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DepartmentDescription",
                table: "TaxpayerProfiles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistrictCode",
                table: "TaxpayerProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DistrictDescription",
                table: "TaxpayerProfiles",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "TaxpayerProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HouseNumber",
                table: "TaxpayerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "TaxpayerProfiles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TaxpayerType",
                table: "TaxpayerProfiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NumberingSequenceId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StampingNumber",
                table: "Documents",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FiscalStamps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    StampingNumber = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiscalStamps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FiscalStamps_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NumberingSequences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Environment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    FiscalStampId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentTypeCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    EstablishmentCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ExpeditionPointCode = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Series = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, defaultValue: ""),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NumberingSequences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NumberingSequences_FiscalStamps_FiscalStampId",
                        column: x => x.FiscalStampId,
                        principalTable: "FiscalStamps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NumberingSequences_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Environment_Kind_StampingNumber_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber",
                table: "Documents",
                columns: new[] { "TenantId", "Environment", "Kind", "StampingNumber", "EstablishmentCode", "ExpeditionPointCode", "ExternalDocumentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FiscalStamps_TenantId_Environment_StampingNumber",
                table: "FiscalStamps",
                columns: new[] { "TenantId", "Environment", "StampingNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyRecords_TenantId_Key",
                table: "IdempotencyRecords",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSequences_FiscalStampId",
                table: "NumberingSequences",
                column: "FiscalStampId");

            migrationBuilder.CreateIndex(
                name: "IX_NumberingSequences_TenantId_Environment_FiscalStampId_DocumentTypeCode_EstablishmentCode_ExpeditionPointCode_Series",
                table: "NumberingSequences",
                columns: new[] { "TenantId", "Environment", "FiscalStampId", "DocumentTypeCode", "EstablishmentCode", "ExpeditionPointCode", "Series" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "IdempotencyRecords");

            migrationBuilder.DropTable(name: "NumberingSequences");

            migrationBuilder.DropTable(name: "FiscalStamps");

            migrationBuilder.DropIndex(
                name: "IX_Documents_TenantId_Environment_Kind_StampingNumber_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber",
                table: "Documents");

            migrationBuilder.DropColumn(name: "Address", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "CityCode", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "CityDescription", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "DepartmentCode", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "DepartmentDescription", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "DistrictCode", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "DistrictDescription", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "Email", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "HouseNumber", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "Phone", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "TaxpayerType", table: "TaxpayerProfiles");

            migrationBuilder.DropColumn(name: "NumberingSequenceId", table: "Documents");

            migrationBuilder.DropColumn(name: "StampingNumber", table: "Documents");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TenantId_Environment_Kind_EstablishmentCode_ExpeditionPointCode_ExternalDocumentNumber",
                table: "Documents",
                columns: new[] { "TenantId", "Environment", "Kind", "EstablishmentCode", "ExpeditionPointCode", "ExternalDocumentNumber" },
                unique: true);
        }
    }
}
